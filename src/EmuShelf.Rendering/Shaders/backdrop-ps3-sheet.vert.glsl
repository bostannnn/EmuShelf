// PS3 · Particle sheet (backdrops.js ps3Sheet): 61 × 40 glowing particles form a rolling sheet that streams
// toward you; the output keeps their trails (persistence) and blooms them. One particle per vertex.

out vec4 vColor;

void main()
{
    int zi = gl_VertexID / 61;
    int xi = gl_VertexID - zi * 61 - 30;
    float t = uTime;
    float z = 2.0 + float(zi) * 0.7, x = float(xi) * 0.45;
    float y = -1.4 + 0.6 * sin(x * 0.35 + t * 0.8) * cos(z * 0.22 - t * 0.5) + 0.25 * sin(z * 0.5 + x * 0.2 - t);
    Cam cam = camera(vec3(0.0, 1.4, 0.0), 0.0, 0.08, uRes.x * 0.62, vec2(uRes.x / 2.0, uRes.y * 0.43));
    vec3 eye = camView(cam, vec3(x, y, z));
    gl_PointSize = 1.0;
    if (eye.z < 0.5)
    {
        gl_Position = vec4(2.0, 2.0, 2.0, 1.0);
        vColor = vec4(0.0);
        return;
    }
    vec2 screen = floor(cam.c + vec2(eye.x, -eye.y) * cam.f / eye.z) + 0.5;
    float k = clamp((y + 2.0) / 1.4, 0.0, 1.0) * clamp(1.3 - z / 30.0, 0.0, 1.0);
    vColor = vec4(rgb(120.0 + k * 120.0, 160.0 + k * 80.0, 255.0) * (0.35 + k * 0.6), 0.0);
    gl_Position = pixelClip(screen, 0.0);
}
