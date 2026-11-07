// Dreamcast · Blue sea (backdrops.js dcSea2): a smooth turquoise sea rolls under a Sega-blue sky with tight,
// bright sun glints; Gouraud colour per vertex over 48 columns × 33 depth rows, fogged into the haze.

out vec4 vColor;

float sea(float x, float z, float t)
{
    return 0.28 * sin(x * 0.55 + z * 0.4 - t) + 0.16 * sin(-x * 0.4 + z * 0.85 - t * 1.4) + 0.06 * sin(x * 1.6 + z * 1.3 - t * 2.2);
}

void main()
{
    int quad = gl_VertexID / 6;
    ivec2 o = quadCorner(gl_VertexID - quad * 6);
    int row = quad / 48 + o.x, column = quad - (quad / 48) * 48 + o.y;
    float t = uTime;
    float z = 1.2 * pow(1.12, float(row));
    float x = (float(column) / 48.0 * 2.0 - 1.0) * (4.0 + z);
    float y = sea(x, z, t);
    const float e = 0.04;
    vec3 n = normalize(vec3(-(sea(x + e, z, t) - y) / e, 1.0, -(sea(x, z + e, t) - y) / e));
    vec3 light = normalize(vec3(0.25, 0.65, 0.7));
    float d = max(0.0, dot(n, light));
    float glint = pow(max(0.0, dot(n, normalize(light + vec3(0.0, 0.25, -1.0)))), 60.0);
    vec3 colour = vec3(16.0 + d * 40.0 + glint * 240.0, 140.0 + d * 60.0 + glint * 115.0, 200.0 + d * 40.0 + glint * 55.0) / 255.0;
    Cam cam = camera(vec3(0.0, 1.7, 0.0), 0.0, 0.07, uRes.x * 0.62, vec2(uRes.x / 2.0, uRes.y * 0.42));
    vec3 eye = camView(cam, vec3(x, y, z));
    vColor = vec4(mix(colour, rgb(176.0, 222.0, 250.0), clamp((eye.z - 12.0) / 40.0, 0.0, 1.0)), 1.0);
    gl_Position = camClip(cam, eye);
}
