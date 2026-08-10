// Vertex stage for the physical-media shell. Object space is passed through untouched because the
// artwork panels are projected in object space (see MediaShellCatalog.Place) — that keeps the decal
// glued to the shell no matter how it is turned.

in vec3 aPosition;
in vec3 aNormal;
in vec2 aTexCoord;

uniform mat4 uModel;
uniform mat4 uViewProjection;
uniform mat3 uNormalMatrix;

out vec3 vObjectPosition;
// The panels are rectangles in object space, so deciding which face a fragment belongs to has to
// happen in object space too. Using the world normal for that test silently works at rest and puts
// the cover art on the back of the shell as soon as it is turned.
out vec3 vObjectNormal;
out vec3 vWorldPosition;
out vec3 vNormal;
out vec2 vTexCoord;

void main()
{
    vObjectPosition = aPosition;
    vObjectNormal = aNormal;

    vec4 world = uModel * vec4(aPosition, 1.0);
    vWorldPosition = world.xyz;
    vNormal = uNormalMatrix * aNormal;
    vTexCoord = aTexCoord;

    gl_Position = uViewProjection * world;
}
