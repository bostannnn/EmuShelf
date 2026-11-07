// Game Boy Advance · Isometric blocks (backdrops.js gbaIso): a field of isometric blocks rises and falls in
// waves, flat around the folder. The prototype painted 22 × 22 blocks back to front; here each pixel finds
// the frontmost block face that covers it.

out vec4 fragColor;

const vec3 Vio = vec3(112.0, 101.0, 167.0) / 255.0;
const vec3 Lav = vec3(178.0, 170.0, 230.0) / 255.0;
const vec3 Cream = vec3(248.0, 240.0, 220.0) / 255.0;

void main()
{
    vec2 p = pixelTopLeft(gl_FragCoord);
    float W = uRes.x, H = uRes.y;
    vec3 colour = mix(Lav, Cream, floor(p.y) / (H - 1.0));
    float column = floor((p.x - W / 2.0) / 12.0);
    // A block's faces span from 25 px above its base row to 6 px below it.
    int sHigh = min(42, int(ceil((p.y + 95.0) / 6.0)));
    int sLow = max(0, int(floor((p.y + 64.0) / 6.0)));
    for (int s = sHigh; s >= sLow; s--)
    {
        for (int k = 0; k < 2; k++)
        {
            int d = int(column) + k;
            if (((s + d) & 1) != 0)
                continue;
            int i = (s + d) / 2;
            int j = s - i;
            if (i < 0 || i >= 22 || j < 0 || j >= 22)
                continue;
            float x = W / 2.0 + float(i - j) * 12.0;
            float yb = -70.0 + float(i + j) * 6.0;
            float calm = smoothstep(30.0, 90.0, length(vec2(x - W / 2.0, (yb - H * 0.43) * 1.6)));
            float wave = 0.5 + 0.5 * sin(float(i) * 0.5 + float(j) * 0.35 - uTime * 1.2);
            float y = yb - (3.0 + 16.0 * wave * calm);
            float dx = p.x - x;
            if (abs(dx) / 12.0 + abs(p.y - y) / 6.0 <= 1.0)
            {
                fragColor = vec4(mix(Lav, Cream, wave * calm), 1.0);
                return;
            }
            if (dx >= 0.0 && dx <= 12.0 && p.y >= y + 6.0 - dx * 0.5 && p.y <= yb + 6.0 - dx * 0.5)
            {
                fragColor = vec4(mix(Vio, Lav, 0.55), 1.0);
                return;
            }
            if (dx >= -12.0 && dx <= 0.0 && p.y >= y + (dx + 12.0) * 0.5 && p.y <= yb + (dx + 12.0) * 0.5)
            {
                fragColor = vec4(mix(Vio, Lav, 0.25), 1.0);
                return;
            }
        }
    }
    fragColor = vec4(colour, 1.0);
}
