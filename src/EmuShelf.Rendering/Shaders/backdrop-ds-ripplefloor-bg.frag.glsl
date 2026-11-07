// DS · Ripple floor, paper (backdrops.js dsRippleFloor2): an off-white wash, lighter at the top.
out vec4 fragColor;
void main()
{
    float y = floor(pixelTopLeft(gl_FragCoord).y);
    fragColor = vec4(mix(rgb(240.0, 243.0, 247.0), rgb(226.0, 232.0, 240.0), y / uRes.y), 1.0);
}
