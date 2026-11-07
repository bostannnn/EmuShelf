// PSP · Theme wave (backdrops.js pspField2): a smooth wave surface in the theme colour rolls toward you,
// coloured per vertex by height with a soft highlight on the crests (44 columns × 32 depth rows).

out vec3 vColour;
out float vDepth;

float wave(float x, float z, float t) { return 0.55 * sin(x * 0.25 + z * 0.12 - t * 0.5) + 0.3 * sin(-x * 0.18 + z * 0.3 - t * 0.7); }

void main()
{
    int quad = gl_VertexID / 6;
    int k = gl_VertexID - quad * 6;
    int row = quad / 44;
    int column = quad - row * 44;
    int r = row + (k == 1 || k == 2 || k == 4 ? 1 : 0);
    int c = column + (k == 2 || k == 4 || k == 5 ? 1 : 0);
    float t = uTime, h = 0.66 + 0.04 * sin(t * 0.03);
    float z = 1.2 * pow(1.12, float(r));
    float x = (float(c) / 44.0 * 2.0 - 1.0) * (4.0 + z);
    float y = wave(x, z, t);
    const float e = 0.04;
    vec3 n = normalize(vec3(-(wave(x + e, z, t) - y) / e, 1.0, -(wave(x, z + e, t) - y) / e));
    float height = clamp((y + 0.9) / 1.8, 0.0, 1.0);
    vColour = mix(hsl(h + height * 0.05, 0.6, 0.18 + height * 0.3), vec3(1.0), pow(max(0.0, dot(n, normalize(vec3(0.0, 0.6, 0.8)))), 14.0) * 0.55);
    Cam cam = camera(vec3(0.0, 1.4, 0.0), 0.0, 0.06, uRes.x * 0.62, vec2(uRes.x / 2.0, uRes.y * 0.44));
    vec3 eye = camView(cam, vec3(x, y, z));
    vDepth = eye.z;
    gl_Position = camClip(cam, eye);
}
