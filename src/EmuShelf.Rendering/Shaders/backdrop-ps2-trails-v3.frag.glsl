// PS2 · Light trails over tiles, frame feedback (backdrops.js ps2Trails): last frame decays toward the deep
// blue gradient, so everything drawn on top leaves a fading trail.

uniform sampler2D uPrevious;
uniform float uHasPrevious;
uniform float uSteps;
out vec4 fragColor;

void main()
{
    float y = floor(pixelTopLeft(gl_FragCoord).y);
    vec3 base = mix(rgb(16.0, 26.0, 86.0), rgb(40.0, 64.0, 168.0), y / uRes.y);
    if (uHasPrevious < 0.5)
    {
        fragColor = vec4(base, 1.0);
        return;
    }
    vec3 previous = texelFetch(uPrevious, ivec2(gl_FragCoord.xy), 0).rgb;
    fragColor = vec4(base + (previous - base) * pow(0.86, clamp(uSteps, 0.3, 3.0)), 1.0);
}
