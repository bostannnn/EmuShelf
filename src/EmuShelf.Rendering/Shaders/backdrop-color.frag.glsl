// A pass whose colour is worked out per vertex (lines, particles, flat-shaded meshes).

in vec4 vColor;
out vec4 fragColor;

void main()
{
    fragColor = vColor;
}
