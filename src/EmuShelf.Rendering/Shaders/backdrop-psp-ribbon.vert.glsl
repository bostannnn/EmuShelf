// PSP · Glass ribbons (backdrops.js pspRibbon): two translucent twisting ribbons flow across the theme gradient. Each ribbon is 60
// quads between its two edges, Gouraud-shaded from the darker upper edge to the paler lower one, laid at 32% (no depth). Vertices
// 0..359 are the first ribbon, 360..719 the second (a pass's First picks which, so each ribbon's edges are drawn right after it).

out vec4 vColor;

// The PSP theme's slow hue (backdrops.js pspHue).
float pspHue(float t) { return 0.66 + 0.04 * sin(t * 0.03); }

// Ribbon r's edge e (0 the upper, 1 the lower) at sample i of 0..60, in world space (backdrops.js pspRibbon).
vec3 pspRibbonPoint(int r, int e, int i, float t)
{
    float fr = float(r);
    float x = -14.0 + float(i) * 28.0 / 60.0, base = r == 1 ? -1.6 : -2.6;
    float y = base + sin(x * 0.22 + t * (0.4 + fr * 0.1) + fr * 2.0) * 1.1;
    float z = 12.0 + sin(x * 0.15 - t * 0.3 + fr) * 2.0;
    float tw = sin(x * 0.18 + t * 0.5 + fr * 1.3) * 0.9;
    return e == 0 ? vec3(x, y - tw * 0.5, z - tw) : vec3(x, y + tw * 0.5 + 0.25, z + tw);
}

Cam pspRibbonCamera() { return camera(vec3(0.0), 0.0, 0.0, uRes.x * 0.62, vec2(uRes.x / 2.0, uRes.y * 0.44)); }

void main()
{
    int r = gl_VertexID / 360;
    int local = gl_VertexID - r * 360;
    int quad = local / 6, k = local - quad * 6;
    // Two triangles a b c and a c d: a = upper edge at quad, b = lower at quad, c = lower at quad + 1, d = upper at quad + 1.
    int edge = (k == 1 || k == 2 || k == 4) ? 1 : 0;
    int i = quad + ((k == 2 || k == 4 || k == 5) ? 1 : 0);
    float t = uTime, h = pspHue(t);
    Cam cam = pspRibbonCamera();
    vec3 colour = edge == 1 ? hsl(h + 0.03, 0.5, 0.75) : hsl(h, 0.55, 0.55);
    vColor = vec4(colour, 0.32);
    gl_Position = camClip(cam, camView(cam, pspRibbonPoint(r, edge, i, t)));
}
