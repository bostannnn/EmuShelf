// PS2 · Rolling pearls, sky (backdrops.js ps2Orbs3): lavender into a pale haze at mid-frame.
out vec4 fragColor;
void main()
{
    float y = floor(pixelTopLeft(gl_FragCoord).y);
    fragColor = vec4(mix(rgb(186.0, 196.0, 234.0), rgb(224.0, 228.0, 246.0), clamp(y / (uRes.y * 0.5), 0.0, 1.0)), 1.0);
}
