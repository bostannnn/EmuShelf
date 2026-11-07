// PS2 · Chrome sea, fog: the Gouraud colour fades into the horizon's haze with distance.

in vec3 vColour;
in float vDepth;
out vec4 fragColor;

void main()
{
    fragColor = vec4(mix(vColour, rgb(222.0, 224.0, 236.0), clamp((vDepth - 14.0) / 60.0, 0.0, 1.0)), 1.0);
}
