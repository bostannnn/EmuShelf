// NES · Raster floor (backdrops.js nesRaster): a striped checker floor rushes toward the horizon and gently
// curves, built from per-scanline scroll and palette stripes, under a dithered sky; NES colours only.

out vec4 fragColor;

void main()
{
    vec2 p = pixelTopLeft(gl_FragCoord);
    float x = floor(p.x), y = floor(p.y), t = uTime;
    float hz = floor(uRes.y * 0.4);
    vec3 colour;
    if (y <= hz)
    {
        float k = y / hz * 2.99, b = floor(k);
        int band = k - b > bayer(x, y) ? int(b) + 1 : int(b);
        colour = band == 0 ? NesLavender : band == 1 ? NesSky : NesWhite;
    }
    else
    {
        float z = 600.0 / (y - hz);
        int stripe = int(floor(z * 0.12 + t * 3.0)) & 1;
        float offset = sin(t * 0.25) * 40.0 / (1.0 + (y - hz) * 0.08);
        int check = (int(floor((x - uRes.x / 2.0 + offset) * z / 120.0)) & 1) ^ stripe;
        colour = stripe == 1 ? (check == 1 ? NesBlue : NesSky) : (check == 1 ? NesSky : NesWhite);
        if (bayer(x, y) < clamp((z - 40.0) / 120.0, 0.0, 1.0))
            colour = NesLavender;
    }
    fragColor = vec4(colour, 1.0);
}
