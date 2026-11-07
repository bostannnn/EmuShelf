// SNES · Beveled mosaic (backdrops.js snesMosaic2): big beveled pastel tiles sway in a gentle wave while
// light moves across them, calm around the folder.

out vec4 fragColor;

const int Pick[10] = int[10](0, 1, 0, 5, 0, 2, 0, 3, 0, 4);

void main()
{
    vec2 p = pixelTopLeft(gl_FragCoord);
    float x = floor(p.x), y = floor(p.y), t = uTime;
    float u = x + 5.0 * sin(y * 0.03 + t * 0.35), v = y + 4.0 * sin(x * 0.025 - t * 0.3);
    float tx = floor(u / 16.0), ty = floor(v / 16.0), gx = jmod(u, 16.0), gy = jmod(v, 16.0);
    vec3 c = snesPalette(Pick[imod(int(tx + ty * 3.0), 10)]);
    if (gx < 1.5 || gy < 1.5)
        c = mix(c, rgb(255.0, 250.0, 240.0), 0.45);
    else if (gx > 14.5 || gy > 14.5)
        c = mix(c, rgb(110.0, 100.0, 150.0), 0.18);
    float light = 0.12 * (sin(tx * 0.4 + ty * 0.5 - t * 0.5) + 1.0);
    float quiet = exp(-(pow(x - uRes.x / 2.0, 2.0) / 3200.0 + pow(y - uRes.y * 0.43, 2.0) / 1700.0)) * 0.7;
    fragColor = vec4(mix(mix(c, rgb(255.0, 252.0, 244.0), light), rgb(246.0, 238.0, 236.0), quiet), 1.0);
}
