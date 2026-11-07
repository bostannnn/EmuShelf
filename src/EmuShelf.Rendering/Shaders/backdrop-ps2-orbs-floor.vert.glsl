// PS2 · Rolling pearls, the floor: 2-unit glass tiles at 62% over the mirrored pearls.

out vec4 vColor;

void main()
{
    int quad = gl_VertexID / 6, k = gl_VertexID - quad * 6;
    int zi = quad / 20, xi = quad - zi * 20 - 10;
    float x = float(xi) * 2.0, z = 3.0 + float(zi) * 2.0, fy = -1.6;
    // Triangles (x, z) (x+2, z+2) (x+2, z) and (x, z) (x, z+2) (x+2, z+2).
    vec2 offset = k == 0 || k == 3 ? vec2(0.0) : k == 1 || k == 5 ? vec2(2.0) : k == 2 ? vec2(2.0, 0.0) : vec2(0.0, 2.0);
    vec3 floorColour = rgb(206.0, 212.0, 238.0) * (((xi + zi) & 1) == 1 ? 1.0 : 0.95);
    Cam cam = camera(vec3(0.0, 2.4, 0.0), 0.0, 0.2, 310.0, vec2(240.0, 108.0));
    vec3 eye = camView(cam, vec3(x + offset.x, fy, z + offset.y));
    vColor = vec4(mix(floorColour, rgb(224.0, 228.0, 246.0), clamp((eye.z - 12.0) / 30.0, 0.0, 1.0)), 0.62);
    gl_Position = camClip(cam, eye);
}
