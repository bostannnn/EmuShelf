// Lift-off, the blocks in the air (ideas-bg.js liftOff): each floating cell's block climbs into the light, turning about its own
// axis, shrinking a little and fading out near the top, its faces lit from above and brighter the higher it is. 56 cells × 6 faces
// × 6 vertices; a face turned away is dropped, as the prototype drops it.

out vec4 vColor;

const float Faces[36] = float[36](
    0.0, -1.0, 0.0, 3.0, 7.0, 4.0,  0.0, 1.0, 1.0, 2.0, 6.0, 5.0,  1.0, -1.0, 0.0, 1.0, 5.0, 4.0,
    1.0, 1.0, 3.0, 2.0, 6.0, 7.0,  2.0, -1.0, 0.0, 1.0, 2.0, 3.0,  2.0, 1.0, 4.0, 5.0, 6.0, 7.0);

vec3 corner(int q)
{
    return vec3(q == 1 || q == 2 || q == 5 || q == 6 ? 1.0 : -1.0, q == 2 || q == 3 || q == 6 || q == 7 ? 1.0 : -1.0, q >= 4 ? 1.0 : -1.0);
}

mat3 rotation(vec3 k, float a)
{
    float c = cos(a), s = sin(a), q = 1.0 - c;
    // Row-major as the prototype's rotAxis, written into GLSL's columns.
    return mat3(c + k.x * k.x * q, k.y * k.x * q + k.z * s, k.z * k.x * q - k.y * s,
                k.x * k.y * q - k.z * s, c + k.y * k.y * q, k.z * k.y * q + k.x * s,
                k.x * k.z * q + k.y * s, k.y * k.z * q - k.x * s, c + k.z * k.z * q);
}

void main()
{
    int slot = gl_VertexID / 36, local = gl_VertexID - slot * 36, face = local / 6, k = local - face * 6;
    vec2 f = liftFlyCell(slot);
    float t = uTime;
    vec2 cell = liftCell(f.x, f.y, t);
    Cam cam = liftCamera();
    float X = f.x * LiftSize, Z = 0.2 + f.y * LiftSize;
    float v = cell.y, h = hash2(f.x, f.y);
    float y = -LiftHalf + 7.4 * pow(v, 1.7), angle = pow(v, 1.5) * (2.5 + 4.5 * h), alpha = sstep(1.0, 0.8, v);
    float size = LiftHalf * (1.0 - 0.1 * v);
    mat3 m = rotation(normalize(vec3(h - 0.5, 0.35, hash1(h * 91.7) - 0.5)), angle);
    vec3 centre = vec3(X, y, Z);
    int axis = int(Faces[face * 6]);
    vec3 n = m[axis] * Faces[face * 6 + 1];
    vec3 eye = camView(cam, centre);
    if (cell.x != 1.0 || eye.z < 0.3 || dot(cam.pos - centre - n * size, n) <= 0.0)
    {
        gl_Position = vec4(2.0, 2.0, 2.0, 1.0);
        vColor = vec4(0.0);
        return;
    }
    int q = int(Faces[face * 6 + 2 + (k == 0 || k == 3 ? 0 : k == 1 ? 1 : k == 2 || k == 4 ? 2 : 3)]);
    vec3 world = centre + m * (corner(q) * size);
    float lit = (0.26 + 0.86 * max(0.0, dot(n, vec3(0.32, 0.86, -0.4)))) * (0.75 + 0.5 * v);
    vColor = vec4(liftRamp(lit, eye.z) / 255.0, alpha);
    gl_Position = camClip(cam, camView(cam, world));
}
