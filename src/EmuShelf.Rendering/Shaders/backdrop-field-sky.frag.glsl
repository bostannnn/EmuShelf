// PS1 · Folded field, sky (Codex's motion lab, fillSky): a gradient stepped into 24 bands.

out vec4 fragColor;

void main()
{
    float y = floor(pixelTopLeft(gl_FragCoord).y);
    float u = floor(clamp(y / 83.0, 0.0, 1.0) * 24.0 + 0.5) / 24.0;
    fragColor = vec4(mix(rgb(188.0, 204.0, 213.0), rgb(233.0, 233.0, 222.0), u), 1.0);
}
