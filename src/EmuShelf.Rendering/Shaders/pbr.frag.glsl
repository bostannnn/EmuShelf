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
uniform float uDielectricReflectance;
uniform float uAmbientIntensity;
uniform float uShadowFillOcclusion;
uniform float uCavityStrength;
uniform float uNormalStrength;
uniform vec3 uBodyTint;
uniform float uBodyTintMix;
// Per-shell correction for a source whose authored albedo is darker than the real object's. A SNES
// shell's plastic is light grey; a scan authored under a brighter viewer can encode it far below
// that, and no exposure setting can fix a body that is dark before any light reaches it without
// blowing out the labels and every other shell beside it.
uniform float uBodyAlbedoScale;

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
// Physical width/height and corner radius (as a fraction of the shorter edge) let the decal mask
// stay circular at the corners even when the panel is a wide cartridge label.
uniform float uPanelAspect[MAX_PANELS];
uniform float uPanelCornerRadius[MAX_PANELS];
// Diagonal bite out of the panel's bottom-left corner, in the same units as the radius.
uniform float uPanelCutCorner[MAX_PANELS];
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
// World-space direction from the shaded point toward the large studio key. The environment keeps
// broad reflections; this direct term gives grooves, screws and bevels readable local contrast.
uniform vec3 uKeyDirection;
uniform vec3 uKeyRadiance;
uniform sampler2D uKeyShadowMap;
uniform mat4 uKeyLightViewProjection;
uniform float uHasKeyShadow;
// The expensive studio cubemap is neutral and shared. Only its dim room contribution picks up the
// focused platform colour at draw time, leaving the white softbox reflections photographic.
uniform vec3 uAmbientAccent;
uniform float uAmbientAccentMix;

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

float distributionGGX(float NoH, float roughness)
{
    float a = roughness * roughness;
    float a2 = a * a;
    float denominator = (NoH * NoH * (a2 - 1.0)) + 1.0;
    return a2 / max(PI * denominator * denominator, 1e-5);
}

float geometrySchlickGGX(float NoX, float roughness)
{
    float r = roughness + 1.0;
    float k = (r * r) / 8.0;
    return NoX / max((NoX * (1.0 - k)) + k, 1e-5);
}

vec3 fresnelSchlick(float HoV, vec3 F0)
{
    return F0 + ((1.0 - F0) * pow(1.0 - HoV, 5.0));
}

// Visibility of the large studio key. A compact 3x3 PCF kernel keeps the shadow soft enough to
// match the broad light while still resolving the SNES shell's raised rails, lip and screw wells.
// Only the direct key is attenuated: the surrounding studio remains visible in shadow, as it would
// in product photography.
float keyVisibility(float geometricNoL)
{
    if (uHasKeyShadow < 0.5)
    {
        return 1.0;
    }

    vec4 lightClip = uKeyLightViewProjection * vec4(vWorldPosition, 1.0);
    vec3 projected = (lightClip.xyz / max(lightClip.w, 1e-6)) * 0.5 + 0.5;
    if (projected.x <= 0.0 || projected.x >= 1.0
        || projected.y <= 0.0 || projected.y >= 1.0
        || projected.z <= 0.0 || projected.z >= 1.0)
    {
        return 1.0;
    }

    vec2 texel = 1.0 / vec2(textureSize(uKeyShadowMap, 0));
    float bias = max(0.0022 * (1.0 - geometricNoL), 0.00045);
    float visibility = 0.0;
    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            float closest = texture(uKeyShadowMap, projected.xy + vec2(x, y) * texel).r;
            visibility += projected.z - bias <= closest ? 1.0 : 0.0;
        }
    }

    return visibility / 9.0;
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
    float cavity = 1.0;

    // Sketchfab exports these shells double-sided; flip inward-facing normals so the inside of a
    // case lid is lit rather than black.
    if (!gl_FrontFacing)
    {
        N = -N;
    }
    // Shadow bias follows the broad geometric surface, not high-frequency normal-map grain. Using
    // the perturbed normal here makes adjacent texels choose different biases and creates moire-like
    // shadow acne on an otherwise flat cartridge face.
    vec3 geometricN = N;

    vec4 baseColor = uBaseColorFactor;
    if (uHasBaseColorMap > 0.5)
    {
        baseColor *= texture(uBaseColorMap, vTexCoord);
    }
    baseColor.rgb = mix(baseColor.rgb, uBodyTint, uBodyTintMix);
    // Body only: the printed panels below overwrite this, so a label keeps its scraped colour.
    baseColor.rgb = min(baseColor.rgb * uBodyAlbedoScale, vec3(1.0));

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
        tangentNormal = normalize(vec3(tangentNormal.xy * uNormalStrength, tangentNormal.z));
        // The source normal map already identifies fine wells, seams and moulded grain. Its Z
        // component measures how far a texel turns away from the broad surface, providing a stable
        // texture-space cavity cue that survives rotation and avoids screen-space AO halos.
        float normalRelief = 1.0 - clamp(tangentNormal.z, 0.0, 1.0);
        // Ignore shallow scan waviness; only deliberate, strongly turned relief earns cavity.
        cavity = 1.0 - (uCavityStrength * smoothstep(0.065, 0.50, normalRelief));
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
        if (facing < 0.5)
        {
            continue;
        }

        float aspect = max(uPanelAspect[i], 1e-4);
        float corner = clamp(uPanelCornerRadius[i], 0.0, 0.499);
        vec2 panelPoint = (vec2(u, v) - 0.5) * vec2(aspect, 1.0);
        vec2 halfSize = vec2(aspect * 0.5, 0.5);
        vec2 rounded = abs(panelPoint) - (halfSize - vec2(corner));
        float edgeDistance = length(max(rounded, vec2(0.0)))
            + min(max(rounded.x, rounded.y), 0.0) - corner;
        // Remove the bottom-left corner along a diagonal. Combining by max keeps this one signed
        // distance field, so the same derivative-based antialiasing covers the cut edge too.
        float cut = clamp(uPanelCutCorner[i], 0.0, 0.9);
        if (cut > 0.0)
        {
            float fromCorner =
                ((panelPoint.x + halfSize.x) + (panelPoint.y + halfSize.y) - cut) * 0.70710678;
            edgeDistance = max(edgeDistance, -fromCorner);
        }

        float antialiasWidth = max(fwidth(edgeDistance), 1e-4);
        float panelMask = 1.0 - smoothstep(-antialiasWidth, antialiasWidth, edgeDistance);
        if (panelMask <= 0.0)
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

        baseColor.rgb = mix(baseColor.rgb, art.rgb, panelMask);
        roughness = mix(roughness, uPanelRoughness, panelMask);
        metallic = mix(metallic, 0.0, panelMask);
        if (uPanelFlattenNormal > 0.5)
        {
            N = normalize(mix(N, normalize(uNormalMatrix * uPanelNormal[i]), panelMask));
        }
        // Ink/paper is laid over the body. It should not inherit plastic-grain cavity and become a
        // dirty-looking recess merely because its colour is projected onto body fragments.
        cavity = mix(cavity, 1.0, panelMask);
    }

    roughness = clamp(roughness, 0.045, 1.0);
    metallic = clamp(metallic, 0.0, 1.0);

    vec3 albedo = baseColor.rgb;
    // Dielectrics normally sit close to 4%; a per-shell correction compensates source scans whose
    // material was tuned for a different viewer. Metals still take their tint from albedo.
    vec3 F0 = mix(vec3(uDielectricReflectance), albedo, metallic);
    vec3 diffuseColor = albedo * (1.0 - metallic);

    float NoV = clamp(dot(N, V), 1e-4, 1.0);
    vec3 R = reflect(-V, N);

    vec3 ambientTint = mix(
        vec3(1.0),
        max(uAmbientAccent, vec3(0.08)),
        uAmbientAccentMix * 0.18);
    vec3 irradiance = texture(uIrradianceMap, N).rgb * ambientTint;
    vec3 diffuse = irradiance * diffuseColor;

    vec3 prefiltered = textureLod(uSpecularMap, R, roughness * uSpecularMaxLod).rgb
        * mix(vec3(1.0), ambientTint, 0.22);
    vec3 specular = prefiltered * envBRDFApprox(F0, roughness, NoV);

    vec3 L = normalize(uKeyDirection);
    vec3 H = normalize(V + L);
    float NoL = max(dot(N, L), 0.0);
    float geometricNoL = max(dot(geometricN, L), 0.0);
    float NoH = max(dot(N, H), 0.0);
    float HoV = max(dot(H, V), 0.0);
    vec3 directF = fresnelSchlick(HoV, F0);
    float directD = distributionGGX(NoH, roughness);
    float directG = geometrySchlickGGX(NoV, roughness) * geometrySchlickGGX(NoL, roughness);
    vec3 directSpecular = (directD * directG * directF) / max(4.0 * NoV * NoL, 1e-4);
    vec3 directDiffuse = ((vec3(1.0) - directF) * diffuseColor) / PI;
    float visibility = keyVisibility(geometricNoL);
    float ambientVisibility = mix(1.0, visibility, uShadowFillOcclusion);
    diffuse *= uAmbientIntensity * ambientVisibility * cavity;
    // Reflections remain visible in shade, but no longer glow uniformly across deep moulding.
    specular *= uAmbientIntensity
        * mix(1.0, ambientVisibility, 0.42)
        * mix(1.0, cavity, 0.38);
    vec3 direct = (directDiffuse + directSpecular) * uKeyRadiance * NoL * visibility
        * mix(1.0, cavity, 0.22);

    vec3 colour = (diffuse + specular + direct) * uExposure;

    colour = tonemap(colour);
    // Manual encode: the target framebuffer is a plain RGBA8 surface the host composites, not an
    // sRGB-capable one, so we cannot lean on GL_FRAMEBUFFER_SRGB being available everywhere.
    colour = pow(colour, vec3(1.0 / 2.2));

    fragColor = vec4(colour, 1.0);
}
