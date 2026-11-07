// Steam · Low-poly terrain (backdrops.js steamMesh): a flat-shaded low-poly landscape in Steam blues glides
// toward you (22 columns × 20 depth rows). Each vertex works out its whole triangle, so the face is lit flat.

out vec4 vColor;

float ground(float x, float z, float travel)
{
    float Z = z + travel;
    return 0.7 * sin(x * 0.3 + Z * 0.2) + 0.5 * sin(-x * 0.22 + Z * 0.37) + min(1.2, abs(x) * 0.12) - 0.4;
}

vec3 corner(int r, int c, float travel)
{
    float z = 1.6 * pow(1.18, float(r));
    float x = (float(c) / 22.0 * 2.0 - 1.0) * (4.0 + z * 0.9);
    return vec3(x, ground(x, z, travel), z);
}

void main()
{
    int quad = gl_VertexID / 6;
    int k = gl_VertexID - quad * 6;
    int row = quad / 22;
    int column = quad - row * 22;
    float travel = uTime * 0.9;
    vec3 a = corner(row, column, travel), b = corner(row + 1, column, travel);
    vec3 c = corner(row + 1, column + 1, travel), d = corner(row, column + 1, travel);
    // Triangles a b c and a c d.
    vec3 p0 = a, p1 = k < 3 ? b : c, p2 = k < 3 ? c : d;
    vec3 n = normalize(cross(p1 - p0, p2 - p0));
    if (n.y < 0.0)
        n = -n;
    vec3 position = k == 0 || k == 3 ? a : k == 1 ? b : k == 5 ? d : c;
    vec3 face = mix(rgb(24.0, 36.0, 54.0), rgb(88.0, 160.0, 214.0), pow(clamp(dot(n, normalize(vec3(-0.4, 0.7, -0.3))), 0.0, 1.0), 3.0) * 0.6);
    Cam cam = camera(vec3(0.0, 2.4, 0.0), 0.0, 0.2, uRes.x * 0.62, vec2(uRes.x / 2.0, uRes.y * 0.43));
    vec3 eye = camView(cam, position);
    vColor = vec4(mix(face, rgb(23.0, 29.0, 40.0), clamp((eye.z - 8.0) / 30.0, 0.0, 1.0)), 1.0);
    gl_Position = camClip(cam, eye);
}
