// Arcade · Bulb wall (backdrops.js arcBulbs2): a wall of warm bulbs plays slow rings of light with the odd
// twinkle, framed by a gently chasing border. CRT scanlines and bloom in the output.

out vec4 fragColor;

void main()
{
    vec2 p = pixelTopLeft(gl_FragCoord);
    float t = uTime, W = uRes.x, H = uRes.y;
    const float spacing = 14.0;
    vec3 colour = mix(rgb(26.0, 4.0, 8.0), rgb(60.0, 12.0, 18.0), floor(p.y) / (H - 1.0));
    vec2 cell = floor(p / spacing);
    vec3 glow = vec3(0.0);
    vec3 bulb = vec3(0.0);
    float cover = 0.0;
    for (float gy = cell.y - 1.0; gy <= cell.y + 1.0; gy += 1.0)
    {
        for (float gx = cell.x - 1.0; gx <= cell.x + 1.0; gx += 1.0)
        {
            vec2 at = (vec2(gx, gy) + 0.5) * spacing;
            if (at.x < 0.0 || at.y < 0.0 || at.x >= W || at.y >= H)
                continue;
            float d = length(vec2(at.x - W / 2.0, (at.y - H * 0.43) * 1.5));
            bool edge = at.x < spacing || at.x > W - spacing || at.y < spacing || at.y > H - spacing;
            float ring = 0.5 + 0.5 * sin(d * 0.07 - t * 1.6);
            bool twinkle = hash1(gx * 7.1 + gy * 3.3 + floor(t * 2.0) * 0.17) > 0.96;
            bool on = edge ? imod(int(gx + gy + floor(t * 5.0)), 4) == 0 : d > 50.0 && (ring > 0.8 || twinkle);
            vec2 o = p - at;
            if (on)
                glow += rgb(255.0, 165.0, 80.0) * exp(-dot(o, o) / (4.2 * 4.2)) * ((edge ? 1.0 : ring) * 0.35 + 0.15);
            if (gx == cell.x && gy == cell.y)
            {
                cover = discCover(p, at, on ? 2.6 : 2.4, 1.0);
                bulb = on ? rgb(255.0, 238.0, 196.0) : mix(rgb(84.0, 34.0, 24.0), rgb(150.0, 80.0, 40.0), ring * 0.4);
            }
        }
    }
    colour = mix(colour + glow, bulb, cover);
    fragColor = vec4(colour, 1.0);
}
