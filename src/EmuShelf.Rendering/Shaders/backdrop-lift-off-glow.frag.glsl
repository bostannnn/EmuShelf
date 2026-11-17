// Lift-off, one splat of light: a glow (1 - d²/r²)² or a one-pixel soft dot (the prototype's bilinear dot), added.

in vec2 vOffset;
flat in vec3 vShape;
out vec4 fragColor;

void main()
{
    float w;
    if (vShape.z > 0.5)
    {
        vec2 a = max(vec2(0.0), 1.0 - abs(vOffset));
        w = a.x * a.y * vShape.y;
    }
    else
    {
        float q = 1.0 - dot(vOffset, vOffset) / (vShape.x * vShape.x);
        w = q > 0.0 ? q * q * vShape.y : 0.0;
    }
    fragColor = vec4(liftTint(0.55) * w / 255.0, 0.0);
}
