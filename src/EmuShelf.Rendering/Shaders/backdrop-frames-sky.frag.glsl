// PS1 · Passing frames, sky (Codex's fillSky).
out vec4 fragColor;
void main()
{
    float y = floor(pixelTopLeft(gl_FragCoord).y);
    fragColor = vec4(mix(rgb(204.0, 205.0, 225.0), rgb(238.0, 233.0, 226.0), floor(clamp(y / 155.0, 0.0, 1.0) * 24.0 + 0.5) / 24.0), 1.0);
}
