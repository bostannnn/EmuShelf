// Mega Drive · Blast horizon (backdrops.js mdSpeed3): a raster-banded sky with a bright horizon line, a
// curving line-scrolled checker floor that dithers into the distance, and long speed streaks in the sky.

out vec4 fragColor;

vec3 skyBand(int i)
{
    if (i == 0) return rgb(0.0, 0.0, 72.0);
    if (i == 1) return rgb(0.0, 0.0, 109.0);
    if (i == 2) return rgb(0.0, 36.0, 109.0);
    if (i == 3) return rgb(36.0, 36.0, 145.0);
    if (i == 4) return rgb(36.0, 72.0, 182.0);
    if (i == 5) return rgb(72.0, 109.0, 218.0);
    return rgb(72.0, 182.0, 255.0);
}

void main()
{
    vec2 p = pixelTopLeft(gl_FragCoord);
    float x = floor(p.x), y = floor(p.y), t = uTime;
    vec3 navy = rgb(0.0, 0.0, 72.0), dark = rgb(0.0, 36.0, 109.0), blue = rgb(36.0, 72.0, 182.0), cyan = rgb(72.0, 182.0, 255.0);
    float hz = floor(uRes.y * 0.47);
    vec3 colour;
    if (y < hz)
    {
        float k = y / hz * 6.99, b = floor(k);
        colour = skyBand(k - b > bayer(x, y) ? int(min(6.0, b + 1.0)) : int(b));
    }
    else if (y == hz)
    {
        colour = vec3(1.0);
    }
    else
    {
        float z = 500.0 / (y - hz);
        int stripe = int(floor(z * 0.2 + t * 14.0)) & 1;
        float curve = sin(t * 0.4) * 60.0 / (1.0 + (y - hz) * 0.1);
        int check = (int(floor((x - uRes.x / 2.0 + curve) * z / 100.0)) & 1) ^ stripe;
        colour = check == 1 ? (stripe == 1 ? cyan : blue) : (stripe == 1 ? blue : dark);
        if (z > 35.0 && ((int(x) + int(y)) & 1) == 1)
            colour = blue;
    }
    for (int i = 0; i < 22; i++)
    {
        float fi = float(i);
        if (floor(hz * 0.15 + hash1(fi) * hz * 0.75) != y)
            continue;
        float speed = 160.0 + hash1(fi + 0.3) * 260.0, len = speed * 0.15;
        float head = jmod(hash1(fi + 0.6) * (uRes.x + 200.0) - t * speed, uRes.x + 200.0) - 60.0;
        float k = ceil(x - head);
        if (k >= 0.0 && k < len && floor(head + k) == x)
            colour = mix(colour, k < len * 0.2 ? vec3(1.0) : cyan, 1.0 - k / len);
    }
    fragColor = vec4(colour, 1.0);
}
