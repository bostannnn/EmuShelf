// The native Handheld Stage: one textured quad per layer, placed in canvas pixels (1920×1080, y down) and
// scaled about a pivot (the centre pop, the plate pop). Attribute-less: four vertices as a triangle strip.

uniform vec4 uRect;      // x, y, width, height on the canvas
uniform vec2 uCanvas;    // canvas size
uniform vec3 uScale;     // scale, pivot x, pivot y
uniform vec4 uUv;        // u0, v0, u1, v1 at the quad's top-left and bottom-right corners

out vec2 vUv;

void main()
{
    vec2 corner = vec2(float(gl_VertexID & 1), float((gl_VertexID >> 1) & 1));
    vec2 position = uRect.xy + (corner * uRect.zw);
    position = uScale.yz + ((position - uScale.yz) * uScale.x);
    vUv = mix(uUv.xy, uUv.zw, corner);
    gl_Position = vec4((position.x / uCanvas.x * 2.0) - 1.0, 1.0 - (position.y / uCanvas.y * 2.0), 0.0, 1.0);
}
