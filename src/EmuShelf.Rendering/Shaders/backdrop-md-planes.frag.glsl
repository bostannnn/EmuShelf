// Mega Drive · Twin planes (backdrops.js mdPlanes): a checker floor and a teal ceiling, each line-scrolled
// into perspective, slide in opposite directions; far rows dither away. The output adds 9-bit colour and
// the composite blur.

out vec4 fragColor;

void main()
{
    vec2 p = pixelTopLeft(gl_FragCoord);
    float x = floor(p.x), y = floor(p.y);
    vec3 dark = rgb(0.0, 36.0, 109.0);
    float hz = uRes.y * 0.43;
    bool ceiling = hz - (y + 0.5) > 0.0;
    float dy = ceiling ? hz - (y + 0.5) : (y + 0.5) - hz;
    vec3 colour = dark;
    if (dy > 0.0)
    {
        float t = uTime;
        float z = (ceiling ? 700.0 : 500.0) / dy;
        float offset = sin(t * 0.5 + z * 0.02) * 30.0 / (1.0 + dy * 0.08) + t * 40.0 * (ceiling ? -0.5 : 1.0);
        int stripe = int(floor(z * 0.1 + t * (ceiling ? 2.0 : 5.0))) & 1;
        int check = (int(floor((x - uRes.x / 2.0 + offset) * z / 120.0)) & 1) ^ stripe;
        if (z > 60.0 && ((int(x) + int(y)) & 1) == 1)
            colour = dark;
        else if (ceiling)
            colour = check == 1 ? rgb(0.0, 145.0, 145.0) : dark;
        else
            colour = check == 1 ? rgb(36.0, 72.0, 182.0) : rgb(0.0, 0.0, 72.0);
    }
    fragColor = vec4(colour, 1.0);
}
