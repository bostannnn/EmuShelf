// A splat (backdrops.js pSplat): a single pixel, or a 3×3 block whose weight falls off from the centre.

in vec4 vColor;
flat in float vSpread;
out vec4 fragColor;

void main()
{
    float weight = 1.0;
    if (vSpread > 0.5)
    {
        vec2 offset = floor(gl_PointCoord * 3.0) - 1.0;
        weight = 1.0 / (1.0 + dot(offset, offset) * 1.6);
    }
    fragColor = vColor * weight;
}
