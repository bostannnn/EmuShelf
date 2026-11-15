// PSP · Glass ribbons, an edge segment's coverage (backdrops.js gSeg: one pixel wide), white at 40%, added.

flat in vec4 vSegment;
out vec4 fragColor;

void main()
{
    float cover = segmentCover(pixelTopLeft(gl_FragCoord), vSegment.xy, vSegment.zw, 1.0);
    fragColor = vec4(vec3(0.4 * cover), 0.0);
}
