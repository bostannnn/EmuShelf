// PS1 · Textured pipe (backdrops.js ps1Pipe): the camera glides down a gently bending tube of textured
// panels (16 rings × 12 panels); a triangle that reaches behind the camera is dropped whole, as gTris did.

out vec2 vUv;
out float vShade;
out float vFog;
flat out int vTexture;
flat out int vTextureSet;
flat out vec3 vFogColour;

vec2 centre(float z, float t) { return vec2(sin(z * 0.08 + t * 0.2) * 1.6, cos(z * 0.06 + t * 0.15) * 0.9); }

vec3 panel(vec2 c, float a, float z) { return vec3(c.x + cos(a) * 3.2 * 1.5, c.y + sin(a) * 3.2, z); }

void main()
{
    int quad = gl_VertexID / 6, k = gl_VertexID - quad * 6;
    int ring = quad / 12, i = quad - ring * 12;
    float t = uTime;
    float travel = jmod(t * 2.0, 1.6), firstRow = floor(t * 2.0 / 1.6);
    float z0 = 1.0 + float(ring) * 1.6 - travel, z1 = z0 + 1.6;
    vec2 c0 = centre(z0 + t * 2.0, t), c1 = centre(z1 + t * 2.0, t);
    float a0 = float(i) / 12.0 * PI * 2.0, a1 = float(i + 1) / 12.0 * PI * 2.0;
    vec3 corners[4] = vec3[4](panel(c0, a0, z0), panel(c1, a0, z1), panel(c1, a1, z1), panel(c0, a1, z0));
    Cam cam = camera(vec3(0.0), 0.0, 0.0, uRes.x * 0.55, vec2(uRes.x / 2.0, uRes.y * 0.43));
    // The triangle this vertex belongs to: a b d, or d b c.
    float nearest = k < 3 ? min(min(corners[0].z, corners[1].z), corners[3].z) : min(min(corners[3].z, corners[1].z), corners[2].z);
    vTexture = imod(i + (ring + int(firstRow)) * 5, 6);
    vTextureSet = 1;
    vShade = (0.8 + 0.2 * cos(a0 - 1.0)) * (k < 3 ? 0.95 : 1.0);
    vUv = ps1Uv(k);
    vFogColour = rgb(233.0, 233.0, 222.0);
    vec3 view = camView(cam, corners[ps1Corner(k)]);
    vFog = (view.z - 4.0) / 20.0;
    gl_Position = nearest < 0.25 ? Nowhere : snappedClip(cam, view);
}
