// Arcade · Vector valley (backdrops.js arcVector3): a green wireframe valley scrolls toward you between rolling
// vector hills, with one cyan octahedron turning above, drawn as added light with phosphor persistence.
// Vertices 0..2099: 25 lines running away (42 segments each); 2100..4211: 22 lines across (48 segments
// each), scrolling; 4212..4235: the octahedron's 12 edges. GL lines stand in for the 0.8–0.9px strokes.

out vec4 vColor;

float valley(float x, float z, float t)
{
    float Z = z + t * 2.5;
    return -1.6 + (abs(x) > 3.5 ? (0.9 * sin(x * 0.4 + Z * 0.3) + 0.6 * sin(Z * 0.21 - x * 0.2) + 1.2) * clamp((abs(x) - 3.5) / 4.0, 0.0, 1.0) : 0.0);
}

void main()
{
    float t = uTime;
    Cam cam = camera(vec3(0.0, 2.2, 0.0), 0.0, 0.14, uRes.x * 0.68, vec2(uRes.x / 2.0, uRes.y * 0.4));
    vec3 position;
    vec3 colour;
    if (gl_VertexID < 25 * 84)
    {
        int line = gl_VertexID / 84, point = (gl_VertexID - line * 84) / 2 + (gl_VertexID & 1);
        float x = float(line - 12) * 1.2, z = 2.0 + float(point) * 0.8;
        position = vec3(x, valley(x, z, t), z);
        colour = rgb(80.0, 255.0, 150.0) * 0.45 * (1.0 - float(abs(line - 12)) / 16.0) * 0.8;
    }
    else if (gl_VertexID < 25 * 84 + 22 * 96)
    {
        int id = gl_VertexID - 25 * 84, line = id / 96, point = (id - line * 96) / 2 + (id & 1);
        float z = 2.0 + float(line) * 1.6 - jmod(t * 2.5, 1.6), x = -14.4 + float(point) * 0.6;
        if (z < 2.0)
        {
            gl_Position = Nowhere;
            vColor = vec4(0.0);
            return;
        }
        position = vec3(x, valley(x, z, t), z);
        colour = rgb(80.0, 255.0, 150.0) * 0.5 * clamp(1.3 - z / 34.0, 0.0, 1.0) * 0.8;
    }
    else
    {
        // AR_OCT: ±1.4 on each axis; edges 0-2 0-3 0-4 0-5 1-2 1-3 1-4 1-5 2-4 2-5 3-4 3-5.
        int id = gl_VertexID - 25 * 84 - 22 * 96, edge = id / 2, end = id & 1;
        int a = edge < 4 ? 0 : edge < 8 ? 1 : edge < 10 ? 2 : 3;
        int b = edge < 8 ? 2 + (edge & 3) : 4 + (edge & 1);
        int v = end == 0 ? a : b;
        vec3 p = (v == 0 ? vec3(1.0, 0.0, 0.0) : v == 1 ? vec3(-1.0, 0.0, 0.0) : v == 2 ? vec3(0.0, 1.0, 0.0)
            : v == 3 ? vec3(0.0, -1.0, 0.0) : v == 4 ? vec3(0.0, 0.0, 1.0) : vec3(0.0, 0.0, -1.0)) * 1.4;
        position = rotateZXY(p, vec3(t * 0.5, t * 0.7, 0.0)) + vec3(0.0, 2.6, 14.0);
        colour = rgb(90.0, 220.0, 255.0) * 0.8 * 0.9;
    }
    vec3 eye = camView(cam, position);
    vColor = vec4(colour, 1.0);
    gl_Position = eye.z > 0.3 ? pixelClip(camScreen(cam, eye), 0.0) : Nowhere;
}
