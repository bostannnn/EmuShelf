// Metallic-roughness PBR with image-based lighting. This is the whole point of moving the shelf hero
// onto the GPU: a keep case reads as plastic only when a broad soft highlight slides across it as
// the player turns it, and that needs a real prefiltered environment rather than a per-face tint.

in vec3 vObjectPosition;
in vec3 vObjectNormal;
in vec3 vWorldPosition;
in vec3 vNormal;
in vec2 vTexCoord;

out vec4 fragColor;

uniform vec3 uCameraPosition;
// Same uniform the vertex stage declares; needed here to put a panel's object-space normal into
// world space when a printed panel flattens the moulding underneath it.
uniform mat3 uNormalMatrix;

// --- material ------------------------------------------------------------------------------
uniform vec4 uBaseColorFactor;
uniform float uMetallicFactor;
uniform float uRoughnessFactor;
uniform sampler2D uBaseColorMap;
uniform sampler2D uMetallicRoughnessMap;
uniform sampler2D uNormalMap;
uniform float uHasBaseColorMap;
uniform float uHasMetallicRoughnessMap;
uniform float uHasNormalMap;

// --- artwork panel -------------------------------------------------------------------------
// Up to three panels (cover, back, spine) projected onto faces in object space. Packed as parallel
// arrays because uniform structs are awkward to set across the GL/GLES versions we target.
const int MAX_PANELS = 3;
uniform int uPanelCount;
uniform vec3 uPanelOrigin[MAX_PANELS];
uniform vec3 uPanelUEdge[MAX_PANELS];
uniform vec3 uPanelVEdge[MAX_PANELS];
uniform vec3 uPanelNormal[MAX_PANELS];
uniform vec4 uPanelTint[MAX_PANELS];
uniform float uPanelHasArt[MAX_PANELS];
// Centred sub-rectangle of the artwork this panel samples, so a portrait box scan can be fitted to
// a landscape cartridge label without being squashed.
uniform vec2 uPanelArtScale[MAX_PANELS];
uniform sampler2D uPanelArt0;
uniform sampler2D uPanelArt1;
uniform sampler2D uPanelArt2;
// Roughness the printed panel takes on, overriding the shell's own map. A printed sleeve under a
// clear overlay is glossier and flatter than the moulded plastic around it.
uniform float uPanelRoughness;
// Whether a printed panel overrides the shell's shading normal with the flat face normal. A
// cartridge label is a sticker laid over moulded grooves and hides them completely; a keep case's
// sleeve sits under a curved clear cover whose curvature is exactly what makes it look like a case.
uniform float uPanelFlattenNormal;

// --- lighting ------------------------------------------------------------------------------
uniform samplerCube uIrradianceMap;
uniform samplerCube uSpecularMap;
uniform float uSpecularMaxLod;
uniform float uExposure;

const float PI = 3.14159265359;

// Normal mapping without a TANGENT attribute (Schuler's cotangent frame). One of the three shells
// ships no tangents at all, and deriving the frame from derivatives keeps every model on one path.
mat3 cotangentFrame(vec3 N, vec3 p, vec2 uv)
{
    vec3 dp1 = dFdx(p);
    vec3 dp2 = dFdy(p);
    vec2 duv1 = dFdx(uv);
    vec2 duv2 = dFdy(uv);

    vec3 dp2perp = cross(dp2, N);
    vec3 dp1perp = cross(N, dp1);
    vec3 T = (dp2perp * duv1.x) + (dp1perp * duv2.x);
    vec3 B = (dp2perp * duv1.y) + (dp1perp * duv2.y);

    float invmax = inversesqrt(max(dot(T, T), max(dot(B, B), 1e-8)));
    return mat3(T * invmax, B * invmax, N);
}

vec4 samplePanelArt(int index, vec2 uv)
{
    // Sampler arrays cannot be dynamically indexed in GLSL ES 3.00, so branch explicitly.
    if (index == 0) { return texture(uPanelArt0, uv); }
    if (index == 1) { return texture(uPanelArt1, uv); }
    return texture(uPanelArt2, uv);
}

// Karis' analytic environment BRDF. Replaces the usual precomputed LUT and its extra texture unit;
// the error is far below what a plastic case at this size can show.
vec3 envBRDFApprox(vec3 specularColor, float roughness, float NoV)
{
    const vec4 c0 = vec4(-1.0, -0.0275, -0.572, 0.022);
    const vec4 c1 = vec4(1.0, 0.0425, 1.04, -0.04);
    vec4 r = (roughness * c0) + c1;
    float a004 = (min(r.x * r.x, exp2(-9.28 * NoV)) * r.x) + r.y;
    vec2 ab = (vec2(-1.04, 1.04) * a004) + r.zw;
    return (specularColor * ab.x) + ab.y;
}

// Filmic curve (Hill's ACES fit, condensed). Keeps the softbox highlight from clipping to a flat
// white blob, which is what makes the reflection read as a light source rather than a paint colour.
vec3 tonemap(vec3 x)
{
    const float a = 2.51;
    const float b = 0.03;
    const float c = 2.43;
    const float d = 0.59;
    const float e = 0.14;
    return clamp((x * ((a * x) + b)) / ((x * ((c * x) + d)) + e), 0.0, 1.0);
}

void main()
{
    vec3 N = normalize(vNormal);
    vec3 V = normalize(uCameraPosition - vWorldPosition);

    // Sketchfab exports these shells double-sided; flip inward-facing normals so the inside of a
    // case lid is lit rather than black.
    if (!gl_FrontFacing)
    {
        N = -N;
    }

    vec4 baseColor = uBaseColorFactor;
    if (uHasBaseColorMap > 0.5)
    {
        baseColor *= texture(uBaseColorMap, vTexCoord);
    }

    float metallic = uMetallicFactor;
    float roughness = uRoughnessFactor;
    if (uHasMetallicRoughnessMap > 0.5)
    {
        // glTF packs roughness in G and metalness in B.
        vec3 mr = texture(uMetallicRoughnessMap, vTexCoord).rgb;
        roughness *= mr.g;
        metallic *= mr.b;
    }

    if (uHasNormalMap > 0.5)
    {
        vec3 tangentNormal = (texture(uNormalMap, vTexCoord).xyz * 2.0) - 1.0;
        N = normalize(cotangentFrame(N, vWorldPosition, vTexCoord) * tangentNormal);
    }

    // Project the artwork panels. A fragment belongs to a panel when it lies inside the panel's
    // rectangle AND its face points the same way, so the cover cannot bleed onto the shell's sides.
    for (int i = 0; i < MAX_PANELS; i++)
    {
        if (i >= uPanelCount)
        {
            break;
        }

        vec3 local = vObjectPosition - uPanelOrigin[i];
        vec3 uEdge = uPanelUEdge[i];
        vec3 vEdge = uPanelVEdge[i];
        float u = dot(local, uEdge) / max(dot(uEdge, uEdge), 1e-8);
        float v = dot(local, vEdge) / max(dot(vEdge, vEdge), 1e-8);

        // Compare against the object-space geometric normal, not the shaded one: plastic grain in
        // the normal map must not punch holes in the label's edge, and the model's rotation must
        // not move the label onto another face.
        float facing = dot(normalize(vObjectNormal), uPanelNormal[i]);
        if (u < 0.0 || u > 1.0 || v < 0.0 || v > 1.0 || facing < 0.5)
        {
            continue;
        }

        vec4 art = uPanelTint[i];
        if (uPanelHasArt[i] > 0.5)
        {
            // v runs bottom-up in object space and top-down in image space.
            vec2 uv = vec2(u, 1.0 - v);
            uv = ((uv - 0.5) * uPanelArtScale[i]) + 0.5;
            if (uv.x >= 0.0 && uv.x <= 1.0 && uv.y >= 0.0 && uv.y <= 1.0)
            {
                vec4 sampled = samplePanelArt(i, uv);
                art.rgb = mix(art.rgb, sampled.rgb, sampled.a);
            }
        }

        baseColor.rgb = art.rgb;
        roughness = uPanelRoughness;
        metallic = 0.0;
        if (uPanelFlattenNormal > 0.5)
        {
            N = normalize(uNormalMatrix * uPanelNormal[i]);
        }
    }

    roughness = clamp(roughness, 0.045, 1.0);
    metallic = clamp(metallic, 0.0, 1.0);

    vec3 albedo = baseColor.rgb;
    // 4% normal-incidence reflectance is the dielectric standard; metals take their tint from albedo.
    vec3 F0 = mix(vec3(0.04), albedo, metallic);
    vec3 diffuseColor = albedo * (1.0 - metallic);

    float NoV = clamp(dot(N, V), 1e-4, 1.0);
    vec3 R = reflect(-V, N);

    vec3 irradiance = texture(uIrradianceMap, N).rgb;
    vec3 diffuse = irradiance * diffuseColor;

    vec3 prefiltered = textureLod(uSpecularMap, R, roughness * uSpecularMaxLod).rgb;
    vec3 specular = prefiltered * envBRDFApprox(F0, roughness, NoV);

    vec3 colour = (diffuse + specular) * uExposure;

    colour = tonemap(colour);
    // Manual encode: the target framebuffer is a plain RGBA8 surface the host composites, not an
    // sRGB-capable one, so we cannot lean on GL_FRAMEBUFFER_SRGB being available everywhere.
    colour = pow(colour, vec3(1.0 / 2.2));

    fragColor = vec4(colour, 1.0);
}
