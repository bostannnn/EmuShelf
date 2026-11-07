// NES · Attribute ripple (backdrops.js nesMeta): rounded 16×16 blocks change palette in a ripple from the
// folder. NES palettes apply per 16×16 attribute block, so the colour change travels in blocky steps; only
// NES master-palette colours appear.

out vec4 fragColor;

vec3 nesColour(int ramp, int index)
{
    if (index == 0) return rgb(236.0, 238.0, 236.0);
    if (ramp == 0) return index == 1 ? rgb(168.0, 204.0, 236.0) : index == 2 ? rgb(76.0, 154.0, 236.0) : rgb(8.0, 76.0, 196.0);
    if (ramp == 1) return index == 1 ? rgb(188.0, 188.0, 236.0) : index == 2 ? rgb(120.0, 124.0, 236.0) : rgb(48.0, 50.0, 236.0);
    return index == 1 ? rgb(160.0, 214.0, 228.0) : index == 2 ? rgb(56.0, 180.0, 204.0) : rgb(0.0, 102.0, 120.0);
}

void main()
{
    vec2 p = pixelTopLeft(gl_FragCoord);
    float x = floor(p.x), y = floor(p.y);
    float mx = floor(x / 16.0), my = floor(y / 16.0);
    int u = int(x - mx * 16.0), v = int(y - my * 16.0);
    float dist = length(vec2(mx * 16.0 + 8.0 - uRes.x / 2.0, (my * 16.0 + 8.0 - uRes.y * 0.43) * 1.3)) / 16.0;
    int phase = int(jmod(floor((dist - uTime * 1.6) / 1.6), 4.0));
    int ramp = phase == 3 ? 1 : phase; // the palette sequence 0, 1, 2, 1
    int index = 1;
    if (u == 0 || v == 0)
        index = 0;
    else if (u == 15 || v == 15 || (u == 14 && v > 1) || (v == 14 && u > 1))
        index = 2;
    else if ((u == 2 && v >= 2 && v < 7) || (v == 2 && u >= 2 && u < 7))
        index = 0;
    if (dist < 2.6)
        index = index == 2 ? 1 : 0;
    fragColor = vec4(nesColour(ramp, index), 1.0);
}
