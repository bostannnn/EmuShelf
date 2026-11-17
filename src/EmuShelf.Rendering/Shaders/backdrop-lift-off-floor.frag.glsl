// Lift-off, a floor face: flat (or graded down the front); a block's top shows its flat inner face inside the paler worn edge.

in vec3 vColour;
in vec3 vInner;
in vec2 vLocal;
flat in float vInset;
out vec4 fragColor;

void main()
{
    vec3 c = vColour;
    if (vInset > 0.5)
    {
        float edge = max(abs(vLocal.x), abs(vLocal.y)) - LiftInset;
        float w = max(fwidth(edge), 1e-4);
        c = mix(vInner, vColour, clamp(edge / w + 0.5, 0.0, 1.0));
    }
    fragColor = vec4(c / 255.0, 1.0);
}
