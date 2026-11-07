// PSP · Theme wave, the crest lines: three thin glowing lines ride the surface (the PSP menu wave's
// signature), each drawn once in white and twice more, a pixel and a half above and below, as its glow.

out vec4 vColor;

float wave(float x, float z, float t) { return 0.55 * sin(x * 0.25 + z * 0.12 - t * 0.5) + 0.3 * sin(-x * 0.18 + z * 0.3 - t * 0.7); }

void main()
{
    int copy = gl_VertexID / 480;
    int within = gl_VertexID - copy * 480;
    int crest = within / 160;
    int point = (within - crest * 160) / 2 + (within & 1);
    float t = uTime, h = 0.66 + 0.04 * sin(t * 0.03);
    float z = crest == 0 ? 3.2 : crest == 1 ? 5.5 : 9.0;
    float x = -12.0 + float(point) * 0.3;
    Cam cam = camera(vec3(0.0, 1.4, 0.0), 0.0, 0.06, uRes.x * 0.62, vec2(uRes.x / 2.0, uRes.y * 0.44));
    vec3 eye = camView(cam, vec3(x, wave(x, z, t) + 0.02, z));
    vec2 screen = cam.c + vec2(eye.x, -eye.y) * cam.f / eye.z;
    screen.y += copy == 1 ? -1.5 : copy == 2 ? 1.5 : 0.0;
    vColor = copy == 0 ? vec4(vec3(0.35), 1.0) : vec4(hsl(h, 0.6, 0.7) * 0.08, 1.0);
    gl_Position = pixelClip(screen, 0.0);
}
