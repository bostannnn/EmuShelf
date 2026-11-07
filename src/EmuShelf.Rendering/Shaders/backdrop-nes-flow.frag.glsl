// NES · Palette flow (backdrops.js nesFlow): beveled 16×16 blocks carry diagonal bands of blue that flow by
// palette cycling alone; nothing is redrawn but the colours.

out vec4 fragColor;

void main()
{
    vec2 p = pixelTopLeft(gl_FragCoord);
    float x = floor(p.x), y = floor(p.y);
    float mx = floor(x / 16.0), my = floor(y / 16.0);
    int u = int(x - mx * 16.0), v = int(y - my * 16.0);
    float dist = length(vec2(mx * 16.0 + 8.0 - uRes.x / 2.0, (my * 16.0 + 8.0 - uRes.y * 0.43) * 1.3)) / 16.0;
    int phase = imod(int(mx + my) - int(floor(uTime * 6.0)), 8);
    int index = dist < 2.6 ? 0 : (phase < 3 ? 0 : phase < 5 ? 1 : phase == 5 ? 2 : 1);
    if (u == 0 || v == 0)
        index = 0;
    else if ((u == 15 || v == 15) && index < 3)
        index++;
    else if (index > 0 && u == 2 && v >= 2 && v < 6)
        index--;
    vec3 colour = index == 0 ? NesWhite : index == 1 ? NesSky : index == 2 ? NesBlue : NesDeepBlue;
    fragColor = vec4(colour, 1.0);
}
