// 3DS · Tile wave (backdrops.js n3Tiles): glossy pillow tiles push out of the screen in a slow wave, seen by
// a camera that sways around a fixed plane so the parallax reads as stereo depth. One box (36 vertices) per
// slot of a 13 × 7 grid; the nine slots behind the folder collapse to nothing.

out vec3 vWorld;
out vec3 vNormal;
out vec3 vBase;
flat out vec3 vEye;
flat out float vLightPace;

void main()
{
    int tile = gl_VertexID / 36;
    int within = gl_VertexID - tile * 36;
    int face = within / 6;
    int corner = within - face * 6;
    int j = tile / 13 - 3;
    int i = tile - (tile / 13) * 13 - 6;
    float sway = sin(uTime * 0.33) * 1.1;
    vEye = vec3(sway, 0.0, 0.0);
    vLightPace = 0.8;
    if (abs(i) <= 1 && abs(j) <= 1)
    {
        gl_Position = vec4(2.0, 2.0, 2.0, 1.0);
        vWorld = vec3(0.0);
        vNormal = vec3(0.0, 0.0, -1.0);
        vBase = vec3(0.0);
        return;
    }
    // The prototype's box(): six faces (+x, -x, +y, -y, +z, -z), each two triangles A B C, A C D.
    int axis = face / 2;
    float side = (face & 1) == 0 ? 1.0 : -1.0;
    vec3 n = vec3(0.0), u = vec3(0.0), v = vec3(0.0);
    n[axis] = side;
    u[(axis + 1) % 3] = 1.0;
    v[(axis + 2) % 3] = 1.0;
    vec2 ab = corner == 0 || corner == 3 ? vec2(-1.0, -1.0) : corner == 1 ? vec2(1.0, -1.0) : corner == 5 ? vec2(-1.0, 1.0) : vec2(1.0, 1.0);
    vec3 local = n + u * ab.x + v * ab.y;
    vec3 pillow = normalize(local * vec3(1.0, 1.0, 2.4));

    float t = uTime, fi = float(i), fj = float(j);
    float dist = length(vec2(fi * 0.75, fj));
    float wave = pow(max(0.0, sin(dist * 0.75 - t * 1.25)), 3.0);
    vec3 turn = vec3(-fj / dist * wave * 0.3, fi / dist * wave * 0.3, 0.0);
    vec3 world = rotateZXY(local * vec3(0.56, 0.56, 0.14), turn) + vec3(fi * 1.62, fj * 1.62, 11.5 - wave * 1.7);
    vWorld = world;
    vNormal = rotateZXY(pillow, turn);
    bool hot = ((i * 7 + j * 13) & 15) == 3;
    vBase = hot ? rgb(246.0, 150.0, 136.0) : mix(rgb(246.0, 248.0, 249.0), rgb(168.0, 226.0, 232.0), 0.25 + wave * 0.75);

    Cam cam = camera(vec3(sway, 0.0, 0.0), atan(-sway, 11.0), 0.0, 260.0, vec2(200.0, 104.0));
    gl_Position = camClip(cam, camView(cam, world));
}
