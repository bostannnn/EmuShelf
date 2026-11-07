// DS · Flipbook doodles, ruled paper (backdrops.js dPaper(9, ..., true)): a rule every ninth row.
out vec4 fragColor;
void main()
{
    float y = floor(pixelTopLeft(gl_FragCoord).y);
    vec3 paper = mix(rgb(236.0, 239.0, 243.0), rgb(230.0, 234.0, 240.0), y / uRes.y);
    fragColor = vec4(jmod(y, 9.0) == 8.0 ? rgb(214.0, 226.0, 240.0) : paper, 1.0);
}
