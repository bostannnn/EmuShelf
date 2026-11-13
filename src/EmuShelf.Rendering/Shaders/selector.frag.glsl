// The Launcher's travelling selector: up to three concentric rounded-rect bands, at any size and radius the spring
// is passing through. Each band runs from the previous inset to its own (uInsets) in its colour (uBand1..3,
// premultiplied); a band with no width draws nothing. Today's look: a white keyline (4 px), the violet frame (14 px)
// and a white keyline (4 px), exactly the nested Borders its Avalonia selector draws (radii r, r - 4, r - 18, r - 22).
// The Big Type look (M47 B3): one white band of 8 px.

in vec2 vUv;
in vec2 vLocal;

uniform vec2 uSize;
uniform float uRadius;
uniform float uOpacity;
uniform vec4 uFill;      // when its alpha is set: a filled rounded rect in this (premultiplied) colour instead
uniform vec3 uInsets;    // where each band ends, from the outer edge in
uniform vec4 uBand1;
uniform vec4 uBand2;
uniform vec4 uBand3;

out vec4 fragColor;

float roundedBox(vec2 p, vec2 halfSize, float radius)
{
    radius = min(radius, min(halfSize.x, halfSize.y));
    vec2 q = abs(p) - halfSize + radius;
    return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;
}

float inside(vec2 p, vec2 halfSize, float inset, float radius)
{
    float d = roundedBox(p, max(halfSize - inset, vec2(0.0)), max(radius - inset, 0.0));
    return 1.0 - smoothstep(-0.6, 0.6, d);
}

void main()
{
    vec2 halfSize = uSize * 0.5;
    vec2 p = vLocal - halfSize;
    float i0 = inside(p, halfSize, 0.0, uRadius);
    if (uFill.a > 0.0)
    {
        fragColor = uFill * i0 * uOpacity;
        return;
    }
    float i1 = inside(p, halfSize, uInsets.x, uRadius);
    float i2 = inside(p, halfSize, uInsets.y, uRadius);
    float i3 = inside(p, halfSize, uInsets.z, uRadius);
    vec4 colour = uBand1 * (i0 - i1) + uBand2 * (i1 - i2) + uBand3 * (i2 - i3);
    fragColor = colour * uOpacity;
}
