// PS1 · Folded field (Codex's motion lab, field + raster): the camera drifts over a textured, triangulated
// field that folds up at the sides. The PS1's look comes from the rasteriser: vertices snap to whole pixels
// and textures are mapped affinely (w = 1 here, so nothing is perspective-corrected), as Codex's raster did.

out vec2 vUv;
out float vDepth;
out float vShade;
flat out int vTexture;

const float Rows[12] = float[12](2.3, 2.9, 3.6, 4.5, 5.7, 7.2, 9.2, 12.0, 16.0, 22.0, 32.0, 48.0);

float elevation(float x, float z)
{
    return clamp((abs(x) - 2.6) / 6.0, 0.0, 1.0) * (1.4 + 0.6 * cos(z * 0.35 + x * 0.22)) + 0.15 * sin(z * 0.7 + x * 0.6);
}

void main()
{
    int quad = gl_VertexID / 6;
    int k = gl_VertexID - quad * 6;
    int j = quad / 30;
    int i = quad - j * 30 - 15;
    float x = float(i) * 1.5, z = Rows[j], zz = Rows[j + 1];
    vec3 a = vec3(x, elevation(x, z), z), b = vec3(x, elevation(x, zz), zz);
    vec3 c = vec3(x + 1.5, elevation(x + 1.5, zz), zz), d = vec3(x + 1.5, elevation(x + 1.5, z), z);
    // Two triangles: a b d, then d b c, with the prototype's texture corners.
    vec3 position = k == 0 ? a : k == 1 || k == 4 ? b : k == 5 ? c : d;
    vUv = k == 0 ? vec2(0.0, 15.0) : k == 1 || k == 4 ? vec2(0.0) : k == 5 ? vec2(15.0, 0.0) : vec2(15.0, 15.0);
    vShade = k < 3 ? 0.95 : 1.02;
    vTexture = imod(i * 7 + j * 3, 5);

    float t = uTime;
    float camX = sin(t * 0.17) * 0.7, camY = 3.5 + sin(t * 0.14) * 0.12, yaw = sin(t * 0.12) * 0.065;
    float ca = cos(yaw), sa = sin(yaw);
    float px = position.x - camX;
    float rx = px * ca - position.z * sa, rz = px * sa + position.z * ca;
    vec2 screen = floor(vec2(160.0 + rx * 145.0 / rz, 74.0 + (camY - position.y) * 145.0 / rz) + 0.5);
    vDepth = rz;
    gl_Position = pixelClip(screen, clamp(rz / 100.0, 0.0, 1.0) * 2.0 - 1.0);
}
