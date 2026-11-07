// GameCube · Velvet shells (backdrops.js gcFur): a field of fine violet fibres sways in slow gusts, catching
// light at the tips. The prototype drew a fibre (three short segments) every 5 × 4 px, row by row; each pixel
// replays, in the same order, the fibres whose base is close enough to reach it. Bloom in the output.

out vec4 fragColor;

void main()
{
    vec2 p = pixelTopLeft(gl_FragCoord);
    float t = uTime;
    vec3 deep = rgb(22.0, 18.0, 56.0), lilac = rgb(176.0, 164.0, 236.0);
    vec3 colour = mix(rgb(52.0, 44.0, 120.0), rgb(104.0, 84.0, 200.0), floor(p.y) / (uRes.y - 1.0));
    // Fibres grow up to ~13 px upward and sideways from bases on a grid starting at (-4, -4).
    float rowLow = max(-4.0, ceil((floor(p.y) - 2.0 + 4.0) / 4.0) * 4.0 - 4.0);
    for (float by0 = rowLow; by0 <= floor(p.y) + 16.0; by0 += 4.0)
    {
        if (by0 >= uRes.y + 12.0)
            break;
        for (float bx0 = ceil((floor(p.x) - 15.0 + 4.0) / 5.0) * 5.0 - 4.0; bx0 <= floor(p.x) + 15.0; bx0 += 5.0)
        {
            if (bx0 < -4.0 || bx0 >= uRes.x + 4.0)
                continue;
            float h = hash1(bx0 * 0.13 + by0 * 0.71);
            vec2 base = vec2(bx0 + (h - 0.5) * 4.0, by0 + (hash1(by0 * 0.3 + bx0) - 0.5) * 3.0);
            float sway = sin(bx0 * 0.022 + t * 1.1) * 0.7 + sin(by0 * 0.035 - t * 0.7) * 0.45 + sin((bx0 + by0) * 0.01 + t * 0.4) * 0.3;
            float len = 8.0 + h * 5.0, sheen = 0.5 + 0.5 * sin(sway * 2.2 + bx0 * 0.01);
            vec2 from = base;
            for (int s = 1; s <= 3; s++)
            {
                float k = float(s) / 3.0;
                vec2 to = vec2(base.x + sin(sway * k) * len * k, base.y - cos(sway * k) * len * k * 0.85);
                float cover = segmentCover(p, from, to, 1.1);
                if (cover > 0.0)
                    colour = mix(colour, mix(deep, mix(lilac, rgb(255.0, 250.0, 255.0), sheen * 0.5), k * (0.7 + 0.3 * sheen)), cover * 0.9);
                from = to;
            }
        }
    }
    fragColor = vec4(colour, 1.0);
}
