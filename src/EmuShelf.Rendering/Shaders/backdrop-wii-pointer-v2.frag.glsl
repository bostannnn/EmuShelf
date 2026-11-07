// Wii · Pointer field (backdrops.js wiiPointer2): a blue pointer glides over a field of dots that part around
// it, and every 2.2 s a click sends a wave through the field.

out vec4 fragColor;

vec2 pointer(float s)
{
    return vec2(uRes.x / 2.0 + uRes.x * 0.4 * sin(s * 0.37) + uRes.x * 0.08 * sin(s * 1.1),
        uRes.y * 0.45 + uRes.y * 0.3 * sin(s * 0.53 + 1.0) + uRes.y * 0.07 * cos(s * 1.3));
}

void main()
{
    vec2 p = pixelTopLeft(gl_FragCoord);
    float y = floor(p.y), t = uTime, W = uRes.x, H = uRes.y;
    vec3 colour = mix(rgb(250.0, 251.0, 252.0), rgb(224.0, 228.0, 233.0), y / (H - 1.0));
    if (jmod(y, 4.0) == 0.0)
        colour *= vec3(0.988, 0.99, 0.992);
    vec2 at = pointer(t);
    float spacing = W / 40.0;
    vec2 cell = floor(p / spacing);
    for (float gy = cell.y - 2.0; gy <= cell.y + 2.0; gy += 1.0)
    {
        for (float gx = cell.x - 2.0; gx <= cell.x + 2.0; gx += 1.0)
        {
            vec2 grid = (vec2(gx, gy) + 0.5) * spacing;
            if (grid.x < 0.0 || grid.y < 0.0 || grid.x >= W || grid.y >= H)
                continue;
            vec2 d = grid - at;
            float dl = max(length(d), 1.0);
            vec2 dot2 = grid + d / dl * exp(-dl * dl / (W * W * 0.006)) * spacing * 0.9;
            float hot = exp(-dl * dl / (W * W * 0.01));
            for (int k = 0; k < 3; k++)
            {
                float tapped = (floor(t / 2.2) - float(k)) * 2.2, age = t - tapped;
                if (age < 0.0 || age >= 3.0)
                    continue;
                vec2 e = grid - pointer(tapped);
                float el = max(length(e), 1.0), w = el - age * W * 0.12;
                if (abs(w) < spacing * 1.5)
                {
                    float push = cos(w / (spacing * 1.5) * PI / 2.0) * (1.0 - age / 3.0);
                    dot2 += e / el * push * spacing * 0.5;
                    hot = max(hot, push * 0.8);
                }
            }
            float cover = discCover(p, dot2, 1.3 + hot * 1.4, 1.0);
            if (cover > 0.0)
                colour = mix(colour, mix(rgb(196.0, 204.0, 214.0), rgb(52.0, 190.0, 237.0), hot), cover * 0.9);
        }
    }
    colour = mix(colour, rgb(52.0, 190.0, 237.0), discCover(p, at, 7.0, 1.5) * 0.85);
    colour = mix(colour, vec3(1.0), discCover(p, at, 3.2, 1.0));
    fragColor = vec4(colour, 1.0);
}
