// Transparent receiving plane for the physical shelf. Only its analytic soft shadows are visible;
// the app's themed background remains the actual floor colour.

in vec3 aPosition;

uniform mat4 uViewProjection;
uniform float uPlaneY;
uniform vec2 uPlaneCentre;
uniform vec2 uPlaneExtent;

out vec2 vPlanePosition;

void main()
{
    vec2 position = uPlaneCentre + (aPosition.xz * uPlaneExtent);
    vPlanePosition = position;
    gl_Position = uViewProjection * vec4(position.x, uPlaneY, position.y, 1.0);
}
