// The Launcher's travelling selector: three concentric rounded-rect bands, a white keyline (4 px), the
// violet frame (14 px) and a white keyline (4 px), exactly the nested Borders the Avalonia selector draws
// (radii r, r - 4, r - 18, r - 22), at any size and radius the spring is passing through. Premultiplied.

in vec2 vUv;
in vec2 vLocal;

uniform vec2 uSize;
uniform float uRadius;
uniform float uOpacity;

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
    float i1 = inside(p, halfSize, 4.0, uRadius);
    float i2 = inside(p, halfSize, 18.0, uRadius);
    float i3 = inside(p, halfSize, 22.0, uRadius);
    vec3 violet = vec3(91.0, 88.0, 217.0) / 255.0;
    vec4 colour = vec4(1.0) * (i0 - i1) + vec4(violet, 1.0) * (i1 - i2) + vec4(1.0) * (i2 - i3);
    fragColor = colour * uOpacity;
}
