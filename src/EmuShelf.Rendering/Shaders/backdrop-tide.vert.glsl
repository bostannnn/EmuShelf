// PS1 · Crystal tide (Codex's motion lab, tide): a triangulated surface rises and falls with light shifting
// across its facets; 28 × 11 textured quads through Codex's raster (snapped, affine).

out vec2 vUv;
out float vShade;
out float vFog;
flat out int vTexture;
flat out int vTextureSet;
flat out vec3 vFogColour;

const float Rows[12] = float[12](2.3, 2.9, 3.6, 4.5, 5.7, 7.2, 9.2, 12.0, 16.0, 22.0, 32.0, 48.0);

float tide(float x, float z, float t)
{
    return 0.45 * sin(x * 0.58 + z * 0.5 - t * 0.65) + 0.25 * cos(z * 0.9 - t * 0.42) + clamp((abs(x) - 4.0) / 9.0, 0.0, 1.0) * 1.2;
}

void main()
{
    int quad = gl_VertexID / 6, k = gl_VertexID - quad * 6;
    int j = quad / 28, i = quad - j * 28 - 14;
    float t = uTime, x = float(i) * 1.5, z = Rows[j], zz = Rows[j + 1];
    vec3 corners[4] = vec3[4](vec3(x, tide(x, z, t), z), vec3(x, tide(x, zz, t), zz), vec3(x + 1.5, tide(x + 1.5, zz, t), zz), vec3(x + 1.5, tide(x + 1.5, z, t), z));
    vec3 position = corners[ps1Corner(k)];
    float shine = clamp(1.0 + (corners[0].y - corners[2].y) * 0.24, 0.72, 1.3);
    vUv = ps1Uv(k);
    vShade = shine * (k < 3 ? 0.95 : 1.02);
    vTexture = imod(i + j, 3) == 0 ? 7 : imod(i + j, 2) == 0 ? 0 : 3;
    vTextureSet = 0;
    vFogColour = rgb(225.0, 236.0, 229.0);
    float depth;
    gl_Position = codexClip(position, sin(t * 0.1) * 0.35, 3.1, 70.0, depth);
    vFog = (depth - 5.0) / 32.0;
}
