// Bloom, one step (backdrops.js pPresent / gOut): each target pixel averages the block of the source it
// covers (4×4, or 3×3 for the far layer) with four bilinear taps, then keeps only what is brighter than the
// threshold, scaled by how much brighter. A negative threshold is a plain downsample (the far glow layer).

uniform sampler2D uSource;
uniform vec2 uSourceSize;
uniform vec2 uTargetSize;
uniform float uThreshold;

out vec4 fragColor;

void main()
{
    vec2 ratio = uSourceSize / uTargetSize;
    vec2 centre = floor(gl_FragCoord.xy) * ratio + ratio * 0.5;
    vec2 quarter = ratio * 0.25;
    vec3 sum = texture(uSource, (centre + vec2(-quarter.x, -quarter.y)) / uSourceSize).rgb
             + texture(uSource, (centre + vec2(quarter.x, -quarter.y)) / uSourceSize).rgb
             + texture(uSource, (centre + vec2(-quarter.x, quarter.y)) / uSourceSize).rgb
             + texture(uSource, (centre + vec2(quarter.x, quarter.y)) / uSourceSize).rgb;
    vec3 average = sum * 0.25;
    if (uThreshold < 0.0)
    {
        fragColor = vec4(average, 1.0);
        return;
    }
    float luminance = dot(average, vec3(0.3, 0.55, 0.15));
    float strength = clamp((luminance - uThreshold) / (1.0 - uThreshold), 0.0, 1.5);
    fragColor = vec4(average * strength, 1.0);
}
