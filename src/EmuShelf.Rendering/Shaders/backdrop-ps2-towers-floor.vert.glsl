// PS2 · Glass towers, the floor: a faint checker of 2.5-unit squares laid at 70% over the reflection.

out vec4 vColor;

void main()
{
    int quad = gl_VertexID / 6, k = gl_VertexID - quad * 6;
    int zi = quad / 15, xi = quad - zi * 15;
    float x = -18.0 + float(xi) * 2.5, z = -1.0 + float(zi) * 2.5;
    // Triangles (x, z) (x+, z) (x+, z+) and (x, z) (x+, z+) (x, z+), as the prototype listed them.
    vec2 offset = k == 0 || k == 3 ? vec2(0.0) : k == 1 ? vec2(2.5, 0.0) : k == 5 ? vec2(0.0, 2.5) : vec2(2.5);
    float shade = ((int(floor(x / 2.5 + 0.5)) + int(floor(z / 2.5 + 0.5))) & 1) == 1 ? 1.0 : 0.965;
    float t = uTime;
    Cam cam = camera(vec3(sin(t * 0.07) * 1.6, 3.1, -3.0), -sin(t * 0.07) * 0.07, 0.27, 320.0, vec2(240.0, 104.0));
    vec3 eye = camView(cam, vec3(x + offset.x, 0.0, z + offset.y));
    vColor = vec4(mix(rgb(184.0, 194.0, 232.0) * shade, rgb(208.0, 216.0, 242.0), clamp((eye.z - 8.0) / 40.0, 0.0, 1.0)), 0.7);
    gl_Position = camClip(cam, eye);
}
