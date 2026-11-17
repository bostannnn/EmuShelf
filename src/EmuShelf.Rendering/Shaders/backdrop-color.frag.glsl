// A pass whose colour is worked out per vertex (lines, particles, flat-shaded meshes).

in vec4 vColor;
#ifdef BACKDROP_SHARP
uniform float uPointRound;
#endif
out vec4 fragColor;

void main()
{
    fragColor = vColor;
#ifdef BACKDROP_SHARP
    // A sharp frame's particles are round dots (M47 B13); its other passes leave uPointRound at 0.
    if (uPointRound > 0.5)
        fragColor *= sharpPointCover(gl_PointCoord);
#endif
}
