// Nintendo DS · Tap ripples (backdrops.js dsRipples): taps land around the folder and send out 1-px rings on
// dotted paper. The output keeps the DS's 6-bit colour channels.

out vec4 fragColor;

void main()
{
    vec2 p = pixelTopLeft(gl_FragCoord);
    float x = floor(p.x), y = floor(p.y);
    vec3 colour = mix(rgb(236.0, 239.0, 243.0), rgb(230.0, 234.0, 240.0), y / uRes.y);
    if (jmod(x, 8.0) == 0.0 && jmod(y, 8.0) == 0.0)
        colour = rgb(224.0, 230.0, 238.0);
    const float period = 0.85;
    float t = uTime;
    for (float k = floor((t - 3.4) / period); k <= floor(t / period); k += 1.0)
    {
        float tapped = k * period;
        float age = t - tapped;
        if (tapped > t || age >= 3.4)
            continue;
        float a = hash1(k) * PI * 2.0, r = 0.55 + hash1(k + 0.5) * 0.45;
        vec2 tap = vec2(128.0 + cos(a) * r * 118.0, 68.0 + sin(a) * r * 58.0);
        vec3 ink = jmod(k, 4.0) != 0.0 ? rgb(106.0, 170.0, 222.0) : rgb(120.0, 136.0, 170.0);
        float radius = age * 30.0, fade = 0.8 * pow(1.0 - age / 3.4, 1.5);
        float d = length(vec2(x, y) - tap);
        for (int ring = 0; ring < 3; ring++)
        {
            float rr = radius - float(ring) * 7.0;
            if (rr > 0.0 && abs(d - rr) < 0.55)
            {
                colour = mix(colour, ink, fade * (1.0 - float(ring) * 0.28));
                break;
            }
        }
        vec2 offset = vec2(x, y) - floor(tap + 0.5);
        if (age < 0.3 && dot(offset, offset) <= 4.0)
            colour = mix(colour, ink, 1.0 - age / 0.3);
    }
    fragColor = vec4(colour, 1.0);
}
