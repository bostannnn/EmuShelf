// PS2 · Rolling pearls (backdrops.js ps2Orbs3): seven large glossy pearls roll in a slow circle on a tiled glass
// floor. Gouraud-shaded per vertex like the PS2's GS: environment reflection, a tight highlight and a rim.
// The pearls spin by their normals (the mesh itself stays put, as in the prototype). Vertices 0..11087 are
// the reflections (mirrored under the floor, half-way to its colour), 11088..22175 the pearls.

out vec4 vColor;

const float Metal[7] = float[7](0.75, 0.4, 0.85, 0.3, 0.6, 0.5, 0.8);

vec3 orbBase(int i)
{
    return i == 0 ? rgb(120.0, 140.0, 220.0) : i == 1 ? rgb(214.0, 218.0, 236.0) : i == 2 ? rgb(96.0, 110.0, 200.0)
        : i == 3 ? rgb(180.0, 196.0, 240.0) : i == 4 ? rgb(150.0, 160.0, 230.0) : i == 5 ? rgb(200.0, 206.0, 240.0)
        : rgb(110.0, 124.0, 210.0);
}

void main()
{
    const int perOrb = 22 * 13 * 6, perPass = perOrb * 7;
    bool mirrored = gl_VertexID < perPass;
    int id = mirrored ? gl_VertexID : gl_VertexID - perPass;
    int orb = id / perOrb;
    vec3 unit;
    if (!sphereVertex(id - orb * perOrb, 22, 13, unit))
    {
        gl_Position = Nowhere;
        vColor = vec4(0.0);
        return;
    }
    float t = uTime, fy = -1.6;
    float r = 0.85 + float(imod(orb, 3)) * 0.18;
    float a = float(orb) / 7.0 * PI * 2.0 + t * 0.11;
    vec3 centre = vec3(cos(a) * 5.2, fy + r, 13.0 + sin(a) * 3.4);
    // rotator(0, 0, -t·0.11·5.2/r): a turn about z.
    float spin = -t * 0.11 * 5.2 / r, cs = cos(spin), sn = sin(spin);
    vec3 n = vec3(unit.x * cs - unit.y * sn, unit.x * sn + unit.y * cs, unit.z);
    Cam cam = camera(vec3(0.0, 2.4, 0.0), 0.0, 0.2, 310.0, vec2(240.0, 108.0));
    vec3 L = normalize(vec3(-0.45, 0.6, -0.65)), Hh = normalize(L + vec3(0.0, 0.0, -1.0));
    // orbShade
    vec3 v = normalize(centre + n * r - cam.pos);
    float diff = max(0.0, dot(n, L)), spec = pow(max(0.0, dot(n, Hh)), 48.0);
    vec3 rr = v - n * 2.0 * dot(v, n);
    vec3 env = rr.y > 0.0 ? mix(rgb(214.0, 224.0, 250.0), rgb(120.0, 140.0, 214.0), clamp(rr.y * 1.6, 0.0, 1.0))
                          : mix(rgb(226.0, 226.0, 236.0), rgb(80.0, 88.0, 140.0), clamp(-rr.y * 3.0, 0.0, 1.0));
    float rim = pow(1.0 - max(0.0, -dot(v, n)), 3.0);
    vec3 colour = mix(orbBase(orb) * (0.45 + 0.65 * diff), env, Metal[orb]) + vec3(spec * 230.0 + rim * 70.0, spec * 230.0 + rim * 70.0, spec * 220.0 + rim * 80.0) / 255.0;
    vec3 position = centre + unit * r;
    if (mirrored)
    {
        position.y = 2.0 * fy - position.y;
        colour = mix(colour, rgb(206.0, 212.0, 238.0), 0.5);
    }
    vec3 eye = camView(cam, position);
    vColor = vec4(mix(colour, rgb(224.0, 228.0, 246.0), clamp((eye.z - 12.0) / 30.0, 0.0, 1.0)), 1.0);
    gl_Position = camClip(cam, eye);
}
