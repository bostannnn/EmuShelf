// PS1 · Textured terraces (backdrops.js ps1Terrace2): a dense field of textured stepped blocks (19 × 13)
// shifts level by level as a low, slow camera drifts over it; each block shows its top, its front and its
// inner side. Snapped vertices, affine textures, fog.

out vec2 vUv;
out float vShade;
out float vFog;
flat out int vTexture;
flat out int vTextureSet;
flat out vec3 vFogColour;

void main()
{
    int column = gl_VertexID / 18, within = gl_VertexID - column * 18, face = within / 6, k = within - face * 6;
    int j = column / 19, i = column - j * 19 - 9;
    float t = uTime, x = float(i) * 1.2, z = 2.0 + float(j) * 1.2, s = 0.6;
    float raw = 1.4 + 1.1 * sin(float(i) * 0.45 + t * 0.18) + 0.9 * cos(float(j) * 0.5 - t * 0.15);
    float h = abs(i) <= 1 && j < 4 ? 0.05 : max(0.05, floor(raw * 4.0 + 0.5) / 4.0 * 0.35);
    float y0 = -1.0, y1 = y0 + h, sx = x < 0.0 ? x + s : x - s;
    vec3 corners[4];
    if (face == 0)
    {
        corners = vec3[4](vec3(x - s, y1, z - s), vec3(x - s, y1, z + s), vec3(x + s, y1, z + s), vec3(x + s, y1, z - s));
        vTexture = imod(i * 3 + j, 6);
        vShade = 1.0;
    }
    else if (face == 1)
    {
        corners = vec3[4](vec3(x - s, y0, z - s), vec3(x - s, y1, z - s), vec3(x + s, y1, z - s), vec3(x + s, y0, z - s));
        vTexture = imod(i + j * 2, 6);
        vShade = 0.72;
    }
    else
    {
        corners = vec3[4](vec3(sx, y0, z - s), vec3(sx, y1, z - s), vec3(sx, y1, z + s), vec3(sx, y0, z + s));
        vTexture = imod(i + j, 6);
        vShade = 0.82;
    }
    vShade *= k < 3 ? 0.95 : 1.0;
    vUv = ps1Uv(k);
    vTextureSet = 1;
    vFogColour = rgb(233.0, 233.0, 222.0);
    Cam cam = camera(vec3(sin(t * 0.1) * 0.6, 3.4, -1.0), sin(t * 0.08) * 0.05, 0.32, uRes.x * 0.55, vec2(uRes.x / 2.0, uRes.y * 0.4));
    vec3 view = camView(cam, corners[ps1Corner(k)]);
    vFog = (view.z - 5.0) / 16.0;
    gl_Position = snappedClip(cam, view);
}
