// PSP · Theme wave, sky (backdrops.js pspField2): a deep gradient in the theme hue, which drifts over time.

out vec4 fragColor;

void main()
{
    float h = 0.66 + 0.04 * sin(uTime * 0.03);
    float u = floor(pixelTopLeft(gl_FragCoord).y) / (uRes.y - 1.0);
    fragColor = vec4(gradient3(u, hsl(h, 0.6, 0.1), 0.45, hsl(h, 0.55, 0.28), hsl(h, 0.55, 0.28)), 1.0);
}
