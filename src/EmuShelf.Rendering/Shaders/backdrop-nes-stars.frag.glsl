// NES · Starfield flicker (backdrops.js nesStars2): star sprites drift in three layers over a dithered
// navy-and-violet nebula, twinkling; a row with more than eight sprites flickers, as the NES had to.

out vec4 fragColor;

const float Speeds[3] = float[3](5.0, 11.0, 22.0);

vec2 starAt(int i)
{
    float x = floor(jmod(hash1(float(i)) * (uRes.x + 16.0) - uTime * Speeds[i - (i / 3) * 3], uRes.x + 16.0)) - 8.0;
    return vec2(x, floor(hash1(float(i) + 0.31) * (uRes.y - 8.0)));
}

void main()
{
    vec2 p = pixelTopLeft(gl_FragCoord);
    float x = floor(p.x), y = floor(p.y), t = uTime;
    float glow = (0.5 + 0.5 * sin(x * 0.018 + y * 0.05 + t * 0.12)) * (0.5 + 0.5 * sin(y * 0.035 - x * 0.006 - t * 0.08));
    float d = bayer(x, y);
    vec3 colour = glow > d * 0.5 + 0.55 ? NesDeepPurple : glow * 1.5 > d ? NesDeepBlue : NesNavy;
    int frame = int(floor(t * 30.0)), twinkle = int(floor(t * 4.0));
    for (int i = 0; i < 96; i++)
    {
        vec2 s = starAt(i);
        vec2 o = vec2(x, y) - (s + 3.0);
        if (abs(o.x) > 2.0 || abs(o.y) > 2.0)
            continue;
        int layer = i - (i / 3) * 3;
        if (layer == 2 && imod(twinkle + i, 4) == 0)
            layer = 1;
        vec3 star;
        if (o == vec2(0.0))
            star = layer == 0 ? NesBlue : NesWhite;
        else if (layer > 0 && abs(o.x) + abs(o.y) == 1.0)
            star = layer == 2 ? NesSky : NesBlue;
        else if (layer == 2 && abs(o.x) + abs(o.y) == 2.0 && (o.x == 0.0 || o.y == 0.0))
            star = NesBlue;
        else
            continue;
        // More than eight sprites on this 8-px row: every other one sits out each frame.
        int row = int(s.y) >> 3, count = 0, rank = 0;
        for (int k = 0; k < 96; k++)
        {
            if ((int(starAt(k).y) >> 3) == row)
            {
                if (k < i) rank++;
                count++;
            }
        }
        if (count > 8 && ((rank + frame) & 1) == 1)
            continue;
        colour = star;
    }
    fragColor = vec4(colour, 1.0);
}
