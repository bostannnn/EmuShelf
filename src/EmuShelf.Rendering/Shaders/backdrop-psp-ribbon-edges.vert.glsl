// PSP · Glass ribbons, the bright edges (backdrops.js pspRibbon's gStroke: white, one pixel wide, 40%, added). Each of the 60
// segments of an edge is a quad a little larger than the stroke; its fragment works out the prototype's coverage (gSeg), so the
// stroke keeps its width and soft edge at any draw size. Vertices 0..719 are the first ribbon's two edges, 720..1439 the second's.

flat out vec4 vSegment;

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
    int r = gl_VertexID / 720;
    int local = gl_VertexID - r * 720;
    int edge = local / 360;
    local -= edge * 360;
    int segment = local / 6, k = local - segment * 6;
    float t = uTime;
    Cam cam = pspRibbonCamera();
    vec3 a = camView(cam, pspRibbonPoint(r, edge, segment, t));
    vec3 b = camView(cam, pspRibbonPoint(r, edge, segment + 1, t));
    vec2 from = cam.c + vec2(a.x, -a.y) * cam.f / a.z;
    vec2 to = cam.c + vec2(b.x, -b.y) * cam.f / b.z;
    vec2 along = to - from;
    float length2 = dot(along, along);
    vec2 d = length2 > 1e-8 ? along * inversesqrt(length2) : vec2(1.0, 0.0);
    vec2 n = vec2(-d.y, d.x);
    // Half the stroke and two draw pixels for its soft edge.
    float pad = 0.5 + 2.0 / BACKDROP_PIXEL;
    bool far = k == 1 || k == 2 || k == 4;
    float side = (k == 2 || k == 4 || k == 5) ? 1.0 : -1.0;
    vec2 corner = (far ? to + d * pad : from - d * pad) + n * pad * side;
    vSegment = vec4(from, to);
    gl_Position = pixelClip(corner, 0.0);
}
