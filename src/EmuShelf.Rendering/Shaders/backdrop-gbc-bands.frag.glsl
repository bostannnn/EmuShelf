// Game Boy Color · Palette bands (backdrops.js gbcBands): soft colour bands drift down over a faint tile
// checker, the palette rewritten every three scanlines. The output adds 15-bit colour, the LCD grid and ghosting.

out vec4 fragColor;

vec3 band(int i)
{
    i = imod(i, 5);
    if (i == 0) return rgb(104.0, 176.0, 236.0);
    if (i == 1) return rgb(118.0, 92.0, 198.0);
    if (i == 2) return rgb(214.0, 70.0, 118.0);
    if (i == 3) return rgb(244.0, 196.0, 64.0);
    return rgb(64.0, 176.0, 156.0);
}

void main()
{
    vec2 p = pixelTopLeft(gl_FragCoord);
    float x = floor(p.x), y = floor(p.y);
    float k = jmod(floor(y / 3.0) * 3.0 / uRes.y * 0.8 + uTime * 0.04, 1.0) * 5.0;
    int i = int(floor(k));
    vec3 colour = mix(mix(band(i), band(i + 1), fract(k)), rgb(244.0, 244.0, 232.0), 0.5);
    if (((int(x) >> 3) + (int(y) >> 3) & 1) == 1)
        colour = mix(colour, vec3(1.0), 0.12);
    fragColor = vec4(colour, 1.0);
}
