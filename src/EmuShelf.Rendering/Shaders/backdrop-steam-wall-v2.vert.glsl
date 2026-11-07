// Steam · Library hall (backdrops.js steamWall2): walls of game covers slide past on both sides, mirrored in a
// dark glossy floor, in the slate fog. Drawn in the prototype's order in one alpha-blended pass: vertices
// 0..671 the reflections (half-way to the dark, at 50%), 672..677 the floor (55%), 678..1349 the covers.

out vec4 vColor;

void main()
{
    const int covers = 2 * 4 * 14 * 6;
    float t = uTime, fy = -2.9;
    vec3 dark = rgb(23.0, 29.0, 40.0);
    Cam cam = camera(vec3(0.0), 0.0, 0.0, uRes.x * 0.68, vec2(uRes.x / 2.0, uRes.y * 0.43));
    vec3 position;
    vec4 colour;
    if (gl_VertexID >= covers && gl_VertexID < covers + 6)
    {
        // The floor: (-30, 2) (30, 40) (30, 2) and (-30, 2) (-30, 40) (30, 40).
        int k = gl_VertexID - covers;
        vec2 xz = k == 0 || k == 3 ? vec2(-30.0, 2.0) : k == 1 || k == 5 ? vec2(30.0, 40.0) : k == 2 ? vec2(30.0, 2.0) : vec2(-30.0, 40.0);
        position = vec3(xz.x, fy, xz.y);
        colour = vec4(rgb(30.0, 40.0, 56.0), 0.55);
    }
    else
    {
        bool mirrored = gl_VertexID < covers;
        int id = mirrored ? gl_VertexID : gl_VertexID - covers - 6;
        int quad = id / 6, k = id - quad * 6;
        int s = quad / 56, r = (quad - s * 56) / 14, c = quad - s * 56 - r * 14;
        float side = s == 0 ? -1.0 : 1.0, fr = float(r), fk = float(c);
        float z = jmod(fk * 1.6 - t * 0.7 * side, 22.4) + 3.0, x = side * 4.6, y = fy + 1.05 + fr * 2.1;
        vec3 art = hsl(0.55 + hash1(fk * 7.0 + fr * 3.0 + side) * 0.12, 0.35, 0.28 + hash1(fk + fr + side * 0.5) * 0.18);
        vec3 top = mix(art, rgb(220.0, 235.0, 250.0), 0.25);
        // P0 (y-, z-) P1 (y-, z+) P2 (y+, z+) P3 (y+, z-): triangles P0 P1 P2 and P0 P2 P3; bottom art, top lighter.
        int n = k == 0 || k == 3 ? 0 : k == 1 ? 1 : k == 5 ? 3 : 2;
        bool high = n >= 2;
        position = vec3(x, y + (high ? 0.875 : -0.875), z + (n == 1 || n == 2 ? 0.6 : -0.6));
        vec3 shade = high ? top : art;
        colour = vec4(shade, 1.0);
        if (mirrored)
        {
            position.y = 2.0 * fy - position.y;
            colour = vec4(mix(shade, dark, 0.5), 0.5);
        }
    }
    vec3 eye = camView(cam, position);
    vColor = vec4(mix(colour.rgb, dark, clamp((eye.z - 6.0) / 22.0, 0.0, 1.0)), colour.a);
    gl_Position = camClip(cam, eye);
}
