// PSP · Glass ribbons, background (backdrops.js pspRibbon's gClear): a two-stop gradient in the theme hue, which drifts over time.

out vec4 fragColor;

// The PSP theme's slow hue (backdrops.js pspHue).
float pspHue(float t) { return 0.66 + 0.04 * sin(t * 0.03); }

void main()
{
    float h = pspHue(uTime);
    float u = pixelIndex(pixelTopLeft(gl_FragCoord).y) / (uRes.y - 1.0);
    fragColor = vec4(mix(hsl(h, 0.6, 0.12), hsl(h, 0.55, 0.34), clamp(u, 0.0, 1.0)), 1.0);
}
