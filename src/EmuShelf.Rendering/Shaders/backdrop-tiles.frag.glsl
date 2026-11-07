// SNES · Floating tiles (Codex's motion lab, tiles): isometric tile clusters bob at different phases with
// drawn shadows and bevels, around an open middle. Painted in the prototype's order; each pixel keeps the
// last tile face that covers it.

out vec4 fragColor;

void main()
{
    vec2 p = pixelTopLeft(gl_FragCoord);
    float y0 = floor(p.y), t = uTime;
    vec3 colour = mix(rgb(219.0, 222.0, 236.0), rgb(242.0, 236.0, 224.0), floor(clamp(y0 / 180.0, 0.0, 1.0) * 24.0 + 0.5) / 24.0);
    vec3 shade = rgb(74.0, 80.0, 123.0);
    for (int j = -4; j < 8; j++)
    {
        for (int i = -7; i < 8; i++)
        {
            float ax = 160.0 + float(i - j) * 22.0, ay = 86.0 + float(i + j) * 10.0;
            bool kept = (ax < 90.0 || ax > 235.0 || ay > 130.0 || ay < 23.0) && ax > -40.0 && ax < 360.0 && ay > -25.0 && ay < 210.0;
            if (!kept)
                continue;
            float lift = 4.0 + sin(t * 0.5 + float(i) * 0.6 + float(j) * 0.3) * 4.0;
            float x = ax + sin(t * 0.12) * 5.0, y = ay - lift;
            const float size = 18.0;
            if (abs(p.x - x) > size + 1.0 || p.y < y - 10.0 || p.y > ay + 15.0)
                continue;
            int n = imod(i * 3 + j, 5);
            vec3 c = snesPalette(n);
            if (inQuad(p, vec2(x - size, ay + 5.0), vec2(x, ay + 14.0), vec2(x + size, ay + 5.0), vec2(x, ay - 4.0)))
                colour = mix(colour, rgb(118.0, 111.0, 140.0), 0.102);
            if (inQuad(p, vec2(x - size, y), vec2(x, y + 9.0), vec2(x, y + 14.0), vec2(x - size, y + 5.0)))
                colour = mix(c, shade, 0.25);
            if (inQuad(p, vec2(x, y + 9.0), vec2(x + size, y), vec2(x + size, y + 5.0), vec2(x, y + 14.0)))
                colour = mix(c, shade, 0.12);
            if (inQuad(p, vec2(x, y - 9.0), vec2(x + size, y), vec2(x, y + 9.0), vec2(x - size, y)))
                colour = c;
            if (inQuad(p, vec2(x, y - 6.0), vec2(x + 12.0, y), vec2(x, y + 6.0), vec2(x - 12.0, y)))
                colour = mix(c, rgb(255.0, 247.0, 228.0), 0.28);
            vec2 glint = floor(vec2(x, y) + 0.5) - 1.0;
            if (n == 2 && p.x >= glint.x && p.x < glint.x + 3.0 && p.y >= glint.y && p.y < glint.y + 2.0)
                colour = rgb(255.0, 245.0, 218.0);
        }
    }
    fragColor = vec4(colour, 1.0);
}
