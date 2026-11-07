// SNES · Mode 7 lake (backdrops.js snesLake2): a Mode 7 water plane slowly turns under an HDMA-banded sky,
// wobbling per scanline, its ripples drawn as soft colour bands.

out vec4 fragColor;

void main()
{
    vec2 p = pixelTopLeft(gl_FragCoord);
    float y = floor(p.y), t = uTime;
    float hz = floor(uRes.y * 0.42);
    vec3 colour;
    if (y < hz)
    {
        int band = int(min(6.0, floor(y / hz * 7.0)));
        colour = band == 0 ? rgb(198.0, 191.0, 225.0) : band == 1 ? rgb(208.0, 198.0, 228.0) : band == 2 ? rgb(218.0, 206.0, 230.0)
            : band == 3 ? rgb(228.0, 214.0, 232.0) : band == 4 ? rgb(236.0, 222.0, 232.0) : band == 5 ? rgb(242.0, 230.0, 231.0) : rgb(244.0, 234.0, 229.0);
    }
    else
    {
        float wobble = sin(y * 0.55 + t * 2.4) * (y - hz) * 0.025;
        vec3 plane = planeAt(p, hz, 1.0, 175.0, 0.22 * sin(t * 0.07), 0.0, t * 0.9, false, wobble);
        float X = plane.x, Z = plane.y;
        float w = sin(X * 0.7 + sin(Z * 0.4 + t * 0.6) * 1.4 + t * 0.5) * 0.6 + sin(Z * 1.1 - t * 0.8) * 0.4;
        int q = int(clamp(floor((w + 1.0) * 2.5), 0.0, 4.0));
        vec3 water = q == 0 ? rgb(150.0, 164.0, 204.0) : q == 1 ? rgb(166.0, 176.0, 209.0) : q == 2 ? rgb(189.0, 186.0, 222.0)
            : q == 3 ? rgb(208.0, 206.0, 232.0) : rgb(226.0, 224.0, 238.0);
        colour = mix(water, rgb(244.0, 234.0, 229.0), clamp((plane.z - 6.0) / 55.0, 0.0, 1.0));
    }
    fragColor = vec4(colour, 1.0);
}
