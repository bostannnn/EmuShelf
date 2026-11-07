// Game Boy Color · Glare lattice (backdrops.js gbcSheen2): a beveled diamond lattice over drifting palette
// bands, with a soft glare sliding across the unlit screen.

out vec4 fragColor;

void main()
{
    vec2 p = pixelTopLeft(gl_FragCoord);
    float x = floor(p.x), y = floor(p.y);
    vec3 colour = gbcBands(x, y, 0.6);
    float diamond = abs(jmod(x, 12.0) - 5.5) + abs(jmod(y, 12.0) - 5.5);
    float k = diamond < 4.0 ? (diamond > 3.0 ? -0.18 : 0.22) : 0.0;
    float s = jmod(x * 0.7 + y - uTime * 22.0, 220.0);
    if (s < 18.0)
        k += 0.35 * (1.0 - abs(s - 9.0) / 9.0);
    if (k > 0.0)
        colour = mix(colour, vec3(1.0), k);
    else if (k < 0.0)
        colour = mix(colour, rgb(60.0, 70.0, 90.0), -k);
    fragColor = vec4(colour, 1.0);
}
