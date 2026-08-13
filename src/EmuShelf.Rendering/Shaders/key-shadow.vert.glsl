// Depth-only pass from the studio key. It uses the exact model matrix from the colour pass, so
// grooves, lips and raised moulding can shadow the cartridge itself instead of being described
// only by the normal map.

in vec3 aPosition;

uniform mat4 uModel;
uniform mat4 uLightViewProjection;

void main()
{
    gl_Position = uLightViewProjection * uModel * vec4(aPosition, 1.0);
}
