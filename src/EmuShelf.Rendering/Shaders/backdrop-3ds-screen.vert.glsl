// 3DS · Through the screen (ideas-3ds.js throughScreen, M47 B13): you glide through an endless run of walls, each with an opening
// shaped like the 3DS top screen (a rounded rectangle 6.2 × 3.72). Each wall is four bands of 32 quads round the opening: the inner
// reveal, the rounded lip, the face, and the face out to where the wall in front hides it. 13 walls, 2.3 apart, flowing toward you;
// the far ones drift and turn a little. The eye sways sideways while aimed at a plane 10 ahead. Vertex = wall · band · segment · corner.

out vec3 vView;
out vec3 vNormal;
out float vTint;
flat out float vOdd;
flat out vec3 vLight;

const int Segments = 32;
const int BandVertices = Segments * 6;
const int WallVertices = 4 * BandVertices;

// The rounded rectangle's point i (0..31): its position and its outward direction.
vec4 openingPoint(int i)
{
    int corner = i / 8, k = i - corner * 8;
    const float A = 3.1, B = 1.86, R = 0.8;
    vec2 centre = corner == 0 ? vec2(A - R, B - R) : corner == 1 ? vec2(-A + R, B - R) : corner == 2 ? vec2(-A + R, -B + R) : vec2(A - R, -B + R);
    float angle = float(corner) * PI * 0.5 + float(k) / 7.0 * PI * 0.5;
    vec2 d = vec2(cos(angle), sin(angle));
    return vec4(centre + d * R, d);
}

// The six rings of a wall: offset outward from the opening, depth, how much the normal faces you, and the tint.
vec4 ring(int r, float outer)
{
    return r == 0 ? vec4(-0.1, 0.9, 0.0, 0.1)
        : r == 1 ? vec4(0.0, 0.0, 0.0, 1.0)
        : r == 2 ? vec4(0.0, 0.0, 0.5, 1.0)
        : r == 3 ? vec4(0.12, 0.0, 1.0, 0.7)
        : r == 4 ? vec4(min(1.1, outer * 0.45), 0.0, 1.0, 0.12)
        : vec4(outer, 0.0, 1.0, 0.0);
}

void main()
{
    int wall = gl_VertexID / WallVertices;
    int rest = gl_VertexID - wall * WallVertices;
    int band = rest / BandVertices;
    rest -= band * BandVertices;
    int segment = rest / 6, corner = rest - segment * 6;
    // The bands join rings 0-1, 2-3, 3-4 and 4-5; each quad is a i, a j, b j and a i, b j, b i.
    int r0 = band == 0 ? 0 : band + 1;
    int r = (corner == 2 || corner == 4 || corner == 5) ? r0 + 1 : r0;
    int point = (corner == 1 || corner == 2 || corner == 4) ? (segment + 1) % Segments : segment;

    float t = uTime, spacing = 2.3, flow = t * 0.6, row = floor(flow / spacing), along = flow - row * spacing;
    float z = 3.7 + float(wall) * spacing - along, id = float(wall) + row, far = clamp((z - 3.7) / 20.0, 0.0, 1.0);
    float turn = 0.1 * sin(id * 0.9 + t * 0.13);
    vec2 shift = vec2(sin(id * 0.7 + t * 0.1) * 0.8, cos(id * 0.55 + t * 0.08) * 0.45) * far;
    float outer = wall == 0 ? 9.0 : 3.3 * (z / (z - spacing) - 1.0) + 1.3;

    vec4 q = openingPoint(point), g = ring(r, outer);
    float facing = 1.0 - g.z, l = 1.0 / max(length(vec2(facing, g.z)), 1e-6);
    vec2 local = q.xy + q.zw * g.x, normal = -q.zw * facing * l;
    float ca = cos(turn), sa = sin(turn);
    vec3 world = vec3(shift + vec2(local.x * ca - local.y * sa, local.x * sa + local.y * ca), z + g.y);
    vec3 n = vec3(normal.x * ca - normal.y * sa, normal.x * sa + normal.y * ca, -g.z * l);

    float sway = sin(t * 0.3) * 0.34;
    Cam cam = camera(vec3(sway, 0.0, 0.0), atan(-sway, 10.0), 0.0, 250.0, vec2(238.0, 106.0));
    vView = camView(cam, world);
    vNormal = camView(cam, cam.pos + n);
    vLight = camView(cam, cam.pos + normalize(vec3(0.2, 0.45, 0.9)));
    vTint = g.w;
    vOdd = mod(id, 2.0);
    gl_Position = camClip(cam, vView);
}
