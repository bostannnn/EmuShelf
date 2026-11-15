// Lift-off, the prototype's present(): the lower left darkened a little for the white name (its veil, 0.24), and a half-level
// ordered dither against banding. The bloom follows in the output pass.

uniform sampler2D uScene;
out vec4 fragColor;

void main()
{
    vec3 c = texelFetch(uScene, ivec2(gl_FragCoord.xy), 0).rgb;
    vec2 p = pixelTopLeft(gl_FragCoord) / uRes;
    float veil = sstep(0.8, 0.3, p.x) * (0.45 + 0.55 * sstep(0.02, 0.5, p.y));
    fragColor = vec4(c * (1.0 - 0.24 * veil) + (bayer(gl_FragCoord.x, gl_FragCoord.y) - 0.5) / 255.0, 1.0);
}
