// PS3 · Particle tunnel (backdrops.js ps3Twin): two particle sheets, above and below, stream toward you and
// frame the folder in a tunnel of light. One particle per vertex: 2 sheets × 36 rows × 49.

out vec4 vColor;

void main()
{
    int sheet = gl_VertexID / (36 * 49), within = gl_VertexID - sheet * 36 * 49;
    int zi = within / 49, xi = within - zi * 49 - 24;
    float side = sheet == 0 ? -1.0 : 1.0, t = uTime;
    float z = 2.0 + jmod(float(zi) * 0.8 - t * 1.2, 28.8), x = float(xi) * 0.5;
    float y = side * (2.0 + 0.45 * sin(x * 0.35 + t * 0.7 + side) * cos(z * 0.25 - t * 0.4));
    Cam cam = camera(vec3(0.0), 0.0, 0.0, uRes.x * 0.62, vec2(uRes.x / 2.0, uRes.y * 0.43));
    vec3 eye = camView(cam, vec3(x, y, z));
    gl_PointSize = 1.0;
    if (eye.z < 0.5)
    {
        gl_Position = Nowhere;
        vColor = vec4(0.0);
        return;
    }
    float k = clamp(1.2 - z / 28.0, 0.0, 1.0);
    vColor = vec4(rgb(130.0 + k * 110.0, 170.0 + k * 70.0, 255.0) * (0.3 + k * 0.55), 0.0);
    gl_Position = pixelClip(floor(camScreen(cam, eye)) + 0.5, 0.0);
}
