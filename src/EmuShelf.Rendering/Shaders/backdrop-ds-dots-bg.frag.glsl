// DS · Dot tunnel, background (backdrops.js dsDots): pale blue-grey into the fog colour.
out vec4 fragColor;
void main()
{
    float y = floor(pixelTopLeft(gl_FragCoord).y);
    fragColor = vec4(mix(rgb(228.0, 233.0, 241.0), rgb(234.0, 238.0, 244.0), y / uRes.y), 1.0);
}
