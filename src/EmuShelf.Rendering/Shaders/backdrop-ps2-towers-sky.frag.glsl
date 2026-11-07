// PS2 · Glass towers, sky (backdrops.js ps2Towers): a pale blue gradient into the horizon haze.
out vec4 fragColor;
void main()
{
    float y = floor(pixelTopLeft(gl_FragCoord).y);
    fragColor = vec4(mix(rgb(138.0, 156.0, 218.0), rgb(208.0, 216.0, 242.0), clamp(y / 150.0, 0.0, 1.0)), 1.0);
}
