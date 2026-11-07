// PS2 · Chrome sea (backdrops.js ps2Sea): a smooth reflective surface rolls toward a hazy low sun. Per
// vertex, as the PS2 lit it: an environment reflection of the sky, Fresnel, and the sun's glint, Gouraud-
// interpolated across a dense grid (44 columns × 26 depth rows, widening with distance).

out vec3 vColour;
out float vDepth;

float sea(float x, float z, float t)
{
    return 0.24 * sin(0.55 * x + 0.42 * z - t * 0.9) + 0.15 * sin(-0.33 * x + 0.8 * z - t * 1.25) + 0.07 * sin(1.6 * x + 1.15 * z - t * 2.1);
}

void main()
{
    int quad = gl_VertexID / 6;
    int k = gl_VertexID - quad * 6;
    int row = quad / 44;
    int column = quad - row * 44;
    // Triangles a b c and a c d: a = (row, column), b = (row + 1, column), c = (row + 1, column + 1), d = (row, column + 1).
    int r = row + (k == 1 || k == 2 || k == 4 ? 1 : 0);
    int c = column + (k == 2 || k == 4 || k == 5 ? 1 : 0);

    float t = uTime;
    Cam cam = camera(vec3(sin(t * 0.05) * 1.4, 1.35 + sin(t * 0.3) * 0.04, 0.0), -sin(t * 0.05) * 0.04, 0.06, 300.0, vec2(240.0, 118.0));
    float z = 1.1 * pow(1.17, float(r));
    float w = z * 1.05 + 4.0;
    float x = (float(c) / 44.0 * 2.0 - 1.0) * w + cam.pos.x;
    float y = sea(x, z, t);
    const float e = 0.06;
    vec3 n = normalize(vec3(-(sea(x + e, z, t) - y) / e, 1.0, -(sea(x, z + e, t) - y) / e));
    vec3 position = vec3(x, y, z);
    vec3 view = normalize(position - cam.pos);
    vec3 reflected = view - n * 2.0 * dot(view, n);
    vec3 zenith = rgb(132.0, 154.0, 214.0), horizon = rgb(222.0, 224.0, 236.0), deep = rgb(46.0, 68.0, 132.0);
    vec3 sky = reflected.y > 0.0 ? mix(horizon, zenith, clamp(reflected.y * 2.4, 0.0, 1.0)) : horizon;
    float toSun = max(0.0, dot(reflected, normalize(vec3(0.0, 0.13, 1.0))));
    float sun = (pow(toSun, 90.0) * 380.0 + pow(toSun, 12.0) * 30.0) / 255.0;
    float fresnel = 0.05 + 0.7 * pow(1.0 - max(0.0, -dot(view, n)), 5.0);
    vColour = mix(deep, sky, fresnel) + sun * vec3(1.0, 0.95, 0.85);
    vec3 eye = camView(cam, position);
    vDepth = eye.z;
    gl_Position = camClip(cam, eye);
}
