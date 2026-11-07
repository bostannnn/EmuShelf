// Game Boy Color · Tile palettes (backdrops.js gbcAttr): beveled 8×8 tiles change colour in a ripple from the
// folder; the GBC gave each tile one of eight palettes, so the colour changes tile by tile.

out vec4 fragColor;

void main()
{
    vec2 p = pixelTopLeft(gl_FragCoord);
    float x = floor(p.x), y = floor(p.y);
    float tx = floor(x / 8.0), ty = floor(y / 8.0);
    int u = int(x - tx * 8.0), v = int(y - ty * 8.0);
    float dist = length(vec2(tx * 8.0 + 4.0 - uRes.x / 2.0, (ty * 8.0 + 4.0 - uRes.y * 0.45) * 1.3)) / 8.0;
    int palette = imod(int(floor((dist - uTime * 2.0) / 2.2)), 4);
    vec3 ink = palette == 0 ? rgb(104.0, 176.0, 236.0) : palette == 1 ? rgb(118.0, 92.0, 198.0) : palette == 2 ? rgb(64.0, 176.0, 156.0) : rgb(214.0, 70.0, 118.0);
    vec3 paper = rgb(244.0, 244.0, 232.0);
    int index = 1;
    if (u == 0 || v == 0)
        index = 0;
    else if (u == 7 || v == 7)
        index = 2;
    else if (u == 2 && v == 2)
        index = 0;
    if (dist < 3.4)
        index = index == 2 ? 1 : 0;
    fragColor = vec4(index == 0 ? paper : index == 1 ? mix(ink, paper, 0.5) : ink, 1.0);
}
