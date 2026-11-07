// DS · Dot tunnel (backdrops.js dsDots): two rippling planes of pixel dots, above and below, stream toward you;
// near dots are 2 × 2, the fog steps in twelve bands. One dot per vertex: 34 rows × 33 columns × 2 planes,
// in the prototype's order (later dots overwrite earlier ones).

out vec4 vColor;

void main()
{
    int zi = gl_VertexID / 66, within = gl_VertexID - zi * 66;
    int xi = within / 2 - 16;
    float side = (within & 1) == 0 ? -1.0 : 1.0;
    float t = uTime, travel = jmod(t * 0.9, 1.0);
    float z = 2.5 + float(zi) - travel, x = float(xi) * 0.75;
    float wave = 0.5 * sin(x * 0.42 + t * 0.9 + side) * cos(z * 0.3 - t * 0.6);
    float y = side * (2.7 - wave * side);
    Cam cam = camera(vec3(0.0), sin(t * 0.09) * 0.05, 0.0, 200.0, vec2(128.0, 68.0));
    vec3 eye = camView(cam, vec3(x, y, z));
    if (eye.z < 0.5 || z < 1.0)
    {
        gl_PointSize = 1.0;
        gl_Position = Nowhere;
        vColor = vec4(0.0);
        return;
    }
    float k = floor(clamp((z - 3.0) / 28.0, 0.0, 1.0) * 12.0) / 12.0;
    vColor = vec4(mix(wave > 0.3 ? rgb(106.0, 170.0, 222.0) : rgb(44.0, 56.0, 92.0), rgb(234.0, 238.0, 244.0), k), 1.0);
    vec2 pixel = floor(camScreen(cam, eye) + 0.5);
    bool big = z < 9.0;
    gl_PointSize = big ? 2.0 : 1.0;
    gl_Position = pixelClip(pixel + (big ? 1.0 : 0.5), 0.0);
}
