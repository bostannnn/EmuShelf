// PS2 · Glass towers (backdrops.js ps2Towers): see-through columns rise and fall over a reflective floor
// while the camera drifts. Alpha-blended like the PS2's overdraw: the towers come far to near (rows from the
// back, the outer ones first), and each tower's five faces in the prototype's order, farthest first.
// Vertices 0..4949 draw the mirrored reflection, 4950..9899 the towers themselves.

out vec4 vColor;

const float Shades[4] = float[4](0.86, 1.0, 0.9, 1.06);

vec3 sideCorner(int face, int corner, float x, float z, float h, float w)
{
    // Bottom corners P0..P3 and top corners Q0..Q3; side f runs P[f], P[f + 1], Q[f + 1], Q[f].
    int n = corner == 0 || corner == 3 ? face : imod(face + 1, 4);
    vec2 xz = n == 0 ? vec2(x - w, z - w) : n == 1 ? vec2(x + w, z - w) : n == 2 ? vec2(x + w, z + w) : vec2(x - w, z + w);
    return vec3(xz.x, corner >= 2 ? h : 0.0, xz.y);
}

void main()
{
    const int perPass = 165 * 30;
    bool mirrored = gl_VertexID < perPass;
    int id = mirrored ? gl_VertexID : gl_VertexID - perPass;
    int tower = id / 30, slot = (id - tower * 30) / 6, k = id - tower * 30 - slot * 6;
    int j = 10 - tower / 15, q = tower - (tower / 15) * 15;
    int i = q == 14 ? 0 : ((q & 1) == 0 ? -(7 - q / 2) : 7 - q / 2);
    if (abs(i) <= 1 && j < 6)
    {
        gl_Position = Nowhere;
        vColor = vec4(0.0);
        return;
    }
    float t = uTime;
    Cam cam = camera(vec3(sin(t * 0.07) * 1.6, 3.1, -3.0), -sin(t * 0.07) * 0.07, 0.27, 320.0, vec2(240.0, 104.0));
    float x = float(i) * 1.55 + float(j & 1) * 0.4, z = 2.5 + float(j) * 1.55;
    float wave = 0.5 + 0.5 * sin(t * 0.55 + float(i) * 0.62 + float(j) * 0.41);
    float h = (0.35 + 2.6 * wave * wave * (1.0 - float(abs(i)) * 0.03)) * (mirrored ? -1.0 : 1.0);
    const float w = 0.52;
    // The face in this slot: faces ranked by the distance of their centre, farthest first.
    float distances[5];
    for (int f = 0; f < 4; f++)
    {
        vec3 a = sideCorner(f, 0, x, z, h, w), b = sideCorner(f, 1, x, z, h, w);
        distances[f] = length(vec3((a.x + b.x) / 2.0, h / 2.0, (a.z + b.z) / 2.0) - cam.pos);
    }
    distances[4] = length(vec3(x, h, z) - cam.pos);
    int face = 0;
    for (int f = 0; f < 5; f++)
    {
        int rank = 0;
        for (int g = 0; g < 5; g++)
            if (distances[g] > distances[f] || (distances[g] == distances[f] && g < f))
                rank++;
        if (rank == slot)
            face = f;
    }
    vec3 lo = mirrored ? rgb(96.0, 112.0, 200.0) : rgb(30.0, 44.0, 160.0);
    vec3 hi = mirrored ? rgb(44.0, 62.0, 170.0) : rgb(96.0, 124.0, 228.0);
    vec3 top = mirrored ? rgb(160.0, 176.0, 236.0) : rgb(206.0, 218.0, 255.0);
    vec3 position;
    vec3 colour;
    float alpha;
    if (face == 4)
    {
        // The top: Q0 Q1 Q2, Q0 Q2 Q3.
        int n = k == 0 || k == 3 ? 0 : k == 1 ? 1 : k == 5 ? 3 : 2;
        position = sideCorner(n, 3, x, z, h, w);
        colour = top;
        alpha = mirrored ? 0.3 : 0.78;
    }
    else
    {
        // A side: a b c, a c d with a = P[f], b = P[f + 1], c = Q[f + 1], d = Q[f]; bottom lo, top hi.
        int corner = k == 0 || k == 3 ? 0 : k == 1 ? 1 : k == 5 ? 3 : 2;
        position = sideCorner(face, corner, x, z, h, w);
        colour = (corner >= 2 ? hi : lo) * Shades[face];
        alpha = mirrored ? 0.26 : 0.58;
    }
    vec3 eye = camView(cam, position);
    vColor = vec4(mix(colour, rgb(208.0, 216.0, 242.0), clamp((eye.z - 8.0) / 40.0, 0.0, 1.0)), alpha);
    gl_Position = camClip(cam, eye);
}
