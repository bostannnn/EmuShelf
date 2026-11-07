// PSP · Theme wave, fog toward the theme hue.

in vec3 vColour;
in float vDepth;
out vec4 fragColor;

void main()
{
    float h = 0.66 + 0.04 * sin(uTime * 0.03);
    fragColor = vec4(mix(vColour, hsl(h, 0.55, 0.26), clamp((vDepth - 10.0) / 32.0, 0.0, 1.0)), 1.0);
}
