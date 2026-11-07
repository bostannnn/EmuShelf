// Game Boy Color · Confetti (backdrops.js gbcConfetti2): confetti in two sizes tumbles over soft palette
// bands; the output's LCD ghosting smears it.

out vec4 fragColor;

void main()
{
    vec2 p = pixelTopLeft(gl_FragCoord);
    float x = floor(p.x), y = floor(p.y), t = uTime, W = uRes.x, H = uRes.y;
    vec3 colour = gbcBands(x, y, 0.8);
    for (int i = 0; i < 110; i++)
    {
        float fi = float(i);
        bool near = imod(i, 4) == 0;
        float px = floor(jmod(hash1(fi) * W + sin(t * 1.3 + fi) * (near ? 10.0 : 5.0), W + 6.0)) - 3.0;
        float py = floor(jmod(hash1(fi + 0.7) * H + t * (near ? 20.0 : 9.0 + hash1(fi + 0.2) * 8.0), H + 6.0)) - 3.0;
        vec2 o = vec2(x, y) - vec2(px, py);
        if (o.x < 0.0 || o.y < 0.0 || o.x > 1.0 || o.y > 1.0)
            continue;
        vec3 c = gbcColour(imod(i, 5) == 0 ? 4 : imod(i, 5) == 1 ? 2 : imod(i, 5) == 2 ? 3 : imod(i, 5) == 3 ? 1 : 0);
        if (near)
        {
            colour = o == vec2(1.0) ? mix(c, vec3(0.0), 0.2) : c;
            continue;
        }
        int phase = imod(int(floor(t * 6.0 + fi)), 4);
        if (o == vec2(0.0) || (phase == 0 && o == vec2(1.0, 0.0)) || (phase == 2 && o == vec2(0.0, 1.0)))
            colour = c;
    }
    fragColor = vec4(colour, 1.0);
}
