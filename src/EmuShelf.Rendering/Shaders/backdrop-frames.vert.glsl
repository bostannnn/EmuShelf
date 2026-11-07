// PS1 · Passing frames (Codex's motion lab, frames): a procession of textured octagonal frames passes around
// the central opening; 10 frames × 8 segments × a face and an inner side, through Codex's raster.

out vec2 vUv;
out float vShade;
out float vFog;
flat out int vTexture;
flat out int vTextureSet;
flat out vec3 vFogColour;

vec3 framePoint(float radius, float a, float z, float turn) { return vec3(cos(a + turn) * radius * 1.55, sin(a + turn) * radius, z); }

void main()
{
    int quad = gl_VertexID / 6, k = gl_VertexID - quad * 6;
    int frame = quad / 16, within = quad - frame * 16, segment = within / 2, side = within & 1;
    float t = uTime;
    float z = 4.0 + jmod(float(frame) * 3.4 - t * 0.65, 34.0), r = 4.7;
    float turn = 0.09 * sin(t * 0.18 + z * 0.12) + z * 0.025;
    float a = float(segment) / 8.0 * PI * 2.0, b = float(segment + 1) / 8.0 * PI * 2.0;
    vec3 corners[4];
    int f3 = imod(frame, 3);
    if (side == 0)
    {
        corners = vec3[4](framePoint(r, a, z, turn), framePoint(r, b, z, turn), framePoint(r + 0.3, b, z, turn), framePoint(r + 0.3, a, z, turn));
        vTexture = f3 == 0 ? 5 : f3 == 1 ? 4 : 3;
        vShade = 1.06;
    }
    else
    {
        corners = vec3[4](framePoint(r, a, z, turn), framePoint(r, a, z + 0.55, turn), framePoint(r, b, z + 0.55, turn), framePoint(r, b, z, turn));
        vTexture = f3 == 0 ? 5 : 4;
        vShade = 0.78;
    }
    vShade *= k < 3 ? 0.95 : 1.02;
    vUv = ps1Uv(k);
    vTextureSet = 0;
    vFogColour = rgb(238.0, 233.0, 226.0);
    float depth;
    gl_Position = codexClip(corners[ps1Corner(k)], 0.0, 0.0, 79.0, depth);
    vFog = (depth - 6.0) / 29.0;
}
