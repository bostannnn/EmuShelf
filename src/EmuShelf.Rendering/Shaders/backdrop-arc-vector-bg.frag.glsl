// Arcade · Vector valley, background (backdrops.js arcVector3): the vector monitor's near-black, faintly green.
out vec4 fragColor;
void main()
{
    float u = floor(pixelTopLeft(gl_FragCoord).y) / (uRes.y - 1.0);
    fragColor = vec4(mix(rgb(2.0, 4.0, 6.0), rgb(3.0, 8.0, 8.0), u), 1.0);
}
