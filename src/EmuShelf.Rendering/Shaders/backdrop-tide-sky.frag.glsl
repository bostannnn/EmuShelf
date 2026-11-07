// PS1 · Crystal tide, sky (Codex's fillSky): a gradient stepped into 24 bands.
out vec4 fragColor;
void main()
{
    float y = floor(pixelTopLeft(gl_FragCoord).y);
    fragColor = vec4(mix(rgb(180.0, 205.0, 219.0), rgb(225.0, 236.0, 229.0), floor(clamp(y / 80.0, 0.0, 1.0) * 24.0 + 0.5) / 24.0), 1.0);
}
