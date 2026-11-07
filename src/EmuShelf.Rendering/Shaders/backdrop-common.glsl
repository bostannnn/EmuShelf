// Shared by every animated Stage backdrop (M45 H7): the uniforms each pass gets, and the helpers the
// motion lab's renderers used (backdrops.js), so a port reads like its prototype. Pixel coordinates are the
// prototype's: origin top-left, y down, pixel centres at +0.5.

uniform float uTime;   // seconds into the backdrop
uniform vec2 uRes;     // the backdrop's own resolution (its console's)
uniform float uFrame;  // the step counter (for frame-stepped effects)

const float PI = 3.14159265;

vec3 rgb(float r, float g, float b) { return vec3(r, g, b) / 255.0; }

// The prototype's hash (Math.sin based).
float hash1(float n) { return fract(sin(n * 127.1 + 311.7) * 43758.5453); }

// The prototype's smooth(a, b, x): a smoothstep that also allows a > b.
float sstep(float a, float b, float x)
{
    float t = clamp((x - a) / (b - a), 0.0, 1.0);
    return t * t * (3.0 - 2.0 * t);
}

// The prototype's mod: always positive.
float jmod(float x, float n) { return x - n * floor(x / n); }

int imod(int x, int n) { return int(jmod(float(x), float(n)) + 0.5); }

vec3 hsl(float h, float s, float l)
{
    h = fract(h);
    float a = s * min(l, 1.0 - l);
    vec3 k = mod(vec3(0.0, 8.0, 4.0) + h * 12.0, 12.0);
    return l - a * max(vec3(-1.0), min(min(k - 3.0, 9.0 - k), vec3(1.0)));
}

// The pixel being shaded, in the prototype's coordinates (fragment shaders only).
vec2 pixelTopLeft(vec4 fragCoord) { return vec2(fragCoord.x, uRes.y - fragCoord.y); }

// A vertical gradient through three stops at 0, mid and 1 (the prototype's gClear).
vec3 gradient3(float u, vec3 a, float mid, vec3 b, vec3 c)
{
    return u < mid ? mix(a, b, clamp(u / mid, 0.0, 1.0)) : mix(b, c, clamp((u - mid) / (1.0 - mid), 0.0, 1.0));
}

// The prototype's rotator: about z, then x, then y.
vec3 rotateZXY(vec3 v, vec3 r)
{
    float a = cos(r.x), b = sin(r.x), c = cos(r.y), d = sin(r.y), e = cos(r.z), f = sin(r.z);
    float x = v.x * e - v.y * f, y = v.x * f + v.y * e, z = v.z;
    float y2 = y * a - z * b;
    z = y * b + z * a;
    y = y2;
    return vec3(x * c + z * d, y, -x * d + z * c);
}

// The prototype's camera: a position, a yaw and a pitch; f in pixels; c the projection centre in pixels.
struct Cam { vec3 pos; float cw; float sw; float cp; float sp; float f; vec2 c; };

Cam camera(vec3 pos, float yaw, float pitch, float f, vec2 c)
{
    return Cam(pos, cos(yaw), sin(yaw), cos(pitch), sin(pitch), f, c);
}

vec3 camView(Cam k, vec3 p)
{
    vec3 d = p - k.pos;
    float x2 = d.x * k.cw - d.z * k.sw;
    float z = d.x * k.sw + d.z * k.cw;
    float y2 = d.y * k.cp + z * k.sp;
    z = -d.y * k.sp + z * k.cp;
    return vec3(x2, y2, z);
}

// A view-space point as a clip-space position: the prototype's screen mapping (sx = c.x + x f / z,
// sy = c.y - y f / z, in pixels, y down), written without the divide so the GPU clips behind the camera and
// interpolates perspective-correctly; depth runs from 0.25 to 200.
vec4 camClip(Cam k, vec3 v)
{
    const float zNear = 0.25, zFar = 200.0;
    return vec4(
        (k.c.x * 2.0 / uRes.x - 1.0) * v.z + v.x * k.f * 2.0 / uRes.x,
        (1.0 - k.c.y * 2.0 / uRes.y) * v.z + v.y * k.f * 2.0 / uRes.y,
        (v.z * (zFar + zNear) - 2.0 * zFar * zNear) / (zFar - zNear),
        v.z);
}

// A point in the prototype's pixel coordinates (y down) as a clip-space position, with a depth in [-1, 1].
vec4 pixelClip(vec2 p, float depth)
{
    return vec4(p.x / uRes.x * 2.0 - 1.0, 1.0 - p.y / uRes.y * 2.0, depth, 1.0);
}

// The prototype's perspective plane (gPlane): the floor below the horizon row hz (or the ceiling above it)
// for a camera hgt above it with focal length f, turned by yaw and moved to (x0, z0). Returns the plane
// point in .xy and its distance in .z, or a negative distance when the pixel shows no plane.
vec3 planeAt(vec2 p, float hz, float hgt, float f, float yaw, float x0, float z0, bool ceil, float shift)
{
    float dy = ceil ? hz - p.y : p.y - hz;
    if (dy <= 0.0)
        return vec3(0.0, 0.0, -1.0);
    float dist = hgt * f / dy;
    float lx = (p.x - uRes.x / 2.0 + shift) * dist / f;
    float cw = cos(yaw), sw = sin(yaw);
    return vec3(x0 + lx * cw + dist * sw, z0 - lx * sw + dist * cw, dist);
}

bool inTriangle(vec2 p, vec2 a, vec2 b, vec2 c)
{
    float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
    float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
    float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
    bool negative = d1 < 0.0 || d2 < 0.0 || d3 < 0.0;
    bool positive = d1 > 0.0 || d2 > 0.0 || d3 > 0.0;
    return !(negative && positive);
}

// ---- Mesh helpers (vertex shaders) ----

// A quad's six vertices as two triangles a b c and a c d, where a = (row, column), b = (row + 1, column),
// c = (row + 1, column + 1), d = (row, column + 1): returns the row and column offsets of vertex k.
ivec2 quadCorner(int k)
{
    return ivec2(k == 1 || k == 2 || k == 4 ? 1 : 0, k == 2 || k == 4 || k == 5 ? 1 : 0);
}

// The prototype's box(): vertex 0..35 of a unit box (corners at ±1) as its corner and its face's normal.
// Faces in the prototype's order (+x, -x, +y, -y, +z, -z), each two triangles A B C, A C D.
void boxVertex(int index, out vec3 local, out vec3 normal)
{
    int face = index / 6;
    int corner = index - face * 6;
    int axis = face / 2;
    float side = (face & 1) == 0 ? 1.0 : -1.0;
    vec3 n = vec3(0.0), u = vec3(0.0), v = vec3(0.0);
    n[axis] = side;
    u[(axis + 1) % 3] = 1.0;
    v[(axis + 2) % 3] = 1.0;
    vec2 ab = corner == 0 || corner == 3 ? vec2(-1.0, -1.0) : corner == 1 ? vec2(1.0, -1.0) : corner == 5 ? vec2(-1.0, 1.0) : vec2(1.0, 1.0);
    local = n + u * ab.x + v * ab.y;
    normal = n;
}

// A vertex that draws nothing (a skipped slot or a culled face).
const vec4 Nowhere = vec4(2.0, 2.0, 2.0, 1.0);

// ---- Lighting (fragment shaders) ----

// The prototype's per-pixel lit material (gTris with a material): wrapped diffuse, Blinn highlight, rim.
vec3 litMaterial(vec3 base, vec3 normal, vec3 world, vec3 eye, vec3 light, float ambient, float specular,
    float shininess, float rim, vec3 rimColour)
{
    vec3 n = normalize(normal);
    vec3 view = normalize(eye - world);
    float facing = dot(n, view);
    if (facing < 0.0)
    {
        n = -n;
        facing = -facing;
    }
    float d = dot(n, light);
    float k = ambient + (1.0 - ambient) * (0.75 * max(0.0, d) + 0.25 * (d * 0.5 + 0.5));
    float highlight = pow(max(0.0, dot(n, normalize(light + view))), shininess) * specular;
    return base * k + highlight + rimColour * pow(1.0 - facing, 3.0) * rim;
}

// ---- PS1 textures (16×16, read at whole texels) ----

// Codex's textures (Folded field, Crystal tide, Passing frames): a base colour, noise, a dark edge, a cross.
vec3 codexTexel(int n, int u, int v)
{
    vec3 base = n == 0 ? vec3(124.0, 153.0, 160.0) : n == 1 ? vec3(151.0, 164.0, 168.0) : n == 2 ? vec3(176.0, 176.0, 184.0)
        : n == 3 ? vec3(157.0, 169.0, 163.0) : n == 4 ? vec3(137.0, 151.0, 171.0) : n == 5 ? vec3(180.0, 156.0, 175.0)
        : n == 6 ? vec3(185.0, 175.0, 149.0) : vec3(116.0, 164.0, 170.0);
    float noise = (hash1(float(n * 256 + v * 16 + u)) - 0.5) * 13.0 + (imod(u + v, 7) == 0 ? 6.0 : 0.0) + (u == 0 || v == 0 ? -7.0 : 0.0);
    if ((u == 3 && v >= 3 && v <= 5) || (v == 3 && u >= 3 && u <= 5))
        return (base + 20.0) / 255.0;
    return (base + noise) / 255.0;
}

// The motion lab's PS1 textures (Textured terraces, Textured pipe; backdrops.js gTex / PST).
vec3 labTexel(int n, int u, int v)
{
    vec3 base = n == 0 ? vec3(124.0, 153.0, 160.0) : n == 1 ? vec3(151.0, 164.0, 168.0) : n == 2 ? vec3(176.0, 176.0, 184.0)
        : n == 3 ? vec3(157.0, 169.0, 163.0) : n == 4 ? vec3(185.0, 175.0, 149.0) : vec3(137.0, 151.0, 171.0);
    bool motif = n == 0 || n == 2 || n == 4;
    float noise = (hash1(float((n + 1) * 31 + v * 16 + u)) - 0.5) * 16.0 + (imod(u + v, 7) == 0 ? 6.0 : 0.0) + (u == 0 || v == 0 ? -9.0 : 0.0);
    float cross = motif && ((u == 4 && v >= 4 && v <= 7) || (v == 4 && u >= 4 && u <= 7)) ? 22.0 : 0.0;
    return (base + noise + cross) / 255.0;
}

// ---- 2D helpers (fragment shaders) ----

const float BayerMatrix[16] = float[16](0.0, 8.0, 2.0, 10.0, 12.0, 4.0, 14.0, 6.0, 3.0, 11.0, 1.0, 9.0, 15.0, 7.0, 13.0, 5.0);

// The prototype's 4×4 ordered-dither threshold at a pixel (0–15 / 16).
float bayer(float x, float y) { return BayerMatrix[(int(y) & 3) * 4 + (int(x) & 3)] / 16.0; }

// A polygon as the canvas filled it: vertices rounded to whole pixels, tested at pixel centres.
bool inQuad(vec2 p, vec2 a, vec2 b, vec2 c, vec2 d)
{
    a = floor(a + 0.5); b = floor(b + 0.5); c = floor(c + 0.5); d = floor(d + 0.5);
    return inTriangle(p, a, b, c) || inTriangle(p, a, c, d);
}

// The prototype's soft disc (gDisc) coverage at a pixel centre.
float discCover(vec2 p, vec2 centre, float radius, float soft) { return clamp((radius - length(p - centre)) / soft + 0.5, 0.0, 1.0); }

// The prototype's thick segment (gSeg) coverage at a pixel centre.
float segmentCover(vec2 p, vec2 a, vec2 b, float width)
{
    vec2 ab = b - a;
    float u = clamp(dot(p - a, ab) / max(dot(ab, ab), 1e-9), 0.0, 1.0);
    return clamp(width / 2.0 - length(p - a - u * ab) + 0.5, 0.0, 1.0);
}

// The NES master-palette colours the NES backdrops use.
const vec3 NesWhite = vec3(236.0, 238.0, 236.0) / 255.0;
const vec3 NesSky = vec3(168.0, 204.0, 236.0) / 255.0;
const vec3 NesBlue = vec3(76.0, 154.0, 236.0) / 255.0;
const vec3 NesDeepBlue = vec3(8.0, 76.0, 196.0) / 255.0;
const vec3 NesNavy = vec3(0.0, 30.0, 116.0) / 255.0;
const vec3 NesLavender = vec3(188.0, 188.0, 236.0) / 255.0;
const vec3 NesDeepPurple = vec3(48.0, 50.0, 236.0) / 255.0;

// The Game Boy Color palette, and its drifting palette-band background (backdrops.js gbcBands / gbcBandsBg).
vec3 gbcColour(int i)
{
    i = imod(i, 5);
    if (i == 0) return rgb(104.0, 176.0, 236.0);
    if (i == 1) return rgb(118.0, 92.0, 198.0);
    if (i == 2) return rgb(214.0, 70.0, 118.0);
    if (i == 3) return rgb(244.0, 196.0, 64.0);
    return rgb(64.0, 176.0, 156.0);
}

vec3 gbcBands(float x, float y, float lift)
{
    float k = jmod(floor(y / 3.0) * 3.0 / uRes.y * 0.8 + uTime * 0.04, 1.0) * 5.0;
    int i = int(floor(k));
    vec3 colour = mix(mix(gbcColour(i), gbcColour(i + 1), fract(k)), rgb(244.0, 244.0, 232.0), lift);
    if (((int(x) >> 3) + (int(y) >> 3) & 1) == 1)
        colour = mix(colour, vec3(1.0), 0.12);
    return colour;
}

// The SNES pastel palette (Codex's PALETTE, reused by the motion lab's SNES backdrops).
vec3 snesPalette(int i)
{
    if (i == 0) return rgb(238.0, 229.0, 238.0);
    if (i == 1) return rgb(189.0, 175.0, 218.0);
    if (i == 2) return rgb(153.0, 194.0, 185.0);
    if (i == 3) return rgb(229.0, 184.0, 187.0);
    if (i == 4) return rgb(232.0, 210.0, 165.0);
    return rgb(166.0, 176.0, 209.0);
}

// A view-space point in the prototype's pixel coordinates (y down).
vec2 camScreen(Cam k, vec3 v) { return k.c + vec2(v.x, -v.y) * k.f / v.z; }

// The prototype's sphere(nu, nv) as quads: vertex `index` of quad (i, j) as a unit-sphere point (= its normal);
// returns false for the two degenerate triangles at the poles (the prototype skipped them).
bool sphereVertex(int index, int nu, int nv, out vec3 point)
{
    int quad = index / 6, k = index - quad * 6;
    int i = quad / nv, j = quad - i * nv;
    if ((k < 3 && j == 0) || (k >= 3 && j == nv - 1))
    {
        point = vec3(0.0);
        return false;
    }
    ivec2 o = quadCorner(k); // rows step i (theta), columns step j (phi): a=(i,j) b=(i+1,j) c=(i+1,j+1) d=(i,j+1)
    float theta = float(i + o.x) / float(nu) * PI * 2.0, phi = float(j + o.y) / float(nv) * PI;
    point = vec3(sin(phi) * cos(theta), cos(phi), sin(phi) * sin(theta));
    return true;
}

// ---- PS1 projection (vertex shaders): whole-pixel vertices and w = 1, so everything interpolates affinely ----

// Codex's raster camera: no pitch or yaw, focal length 145, centred at x = 160.
vec4 codexClip(vec3 v, float camX, float camY, float cy, out float depth)
{
    float rz = v.z;
    vec2 screen = floor(vec2(160.0 + (v.x - camX) * 145.0 / rz, cy + (camY - v.y) * 145.0 / rz) + 0.5);
    depth = rz;
    return pixelClip(screen, clamp(rz / 100.0, 0.0, 1.0) * 2.0 - 1.0);
}

// The motion lab's camera with snapped vertices (gTris with snap).
vec4 snappedClip(Cam k, vec3 view)
{
    return pixelClip(floor(camScreen(k, view) + 0.5), clamp(view.z / 100.0, 0.0, 1.0) * 2.0 - 1.0);
}

// A textured quad a b c d drawn as the prototype's two triangles a b d and d b c: which corner vertex k is
// (0 a, 1 b, 2 c, 3 d), its texture coordinate, and its shade factor.
int ps1Corner(int k) { return k == 0 ? 0 : k == 1 || k == 4 ? 1 : k == 5 ? 2 : 3; }
vec2 ps1Uv(int k) { return k == 0 ? vec2(0.0, 15.0) : k == 1 || k == 4 ? vec2(0.0) : k == 5 ? vec2(15.0, 0.0) : vec2(15.0, 15.0); }
