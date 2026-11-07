// A native scene's sprite: a quad placed in canvas pixels (y down), scaled about its own pivot (a focus
// lift), moved by its track (a page or a folder column), then scaled about its group's pivot (a folder's
// zoom). Attribute-less: four vertices as a triangle strip.

uniform vec4 uRect;      // x, y, width, height on the canvas, at rest
uniform vec2 uCanvas;    // canvas size
uniform vec3 uScale;     // own scale, pivot x, pivot y
uniform vec2 uOffset;    // track offset
uniform vec3 uGroup;     // group scale, pivot x, pivot y
uniform vec4 uUv;        // u0, v0, u1, v1 at the quad's top-left and bottom-right corners

out vec2 vUv;
out vec2 vLocal;

void main()
{
    vec2 corner = vec2(float(gl_VertexID & 1), float((gl_VertexID >> 1) & 1));
    vec2 position = uRect.xy + (corner * uRect.zw);
    position = uScale.yz + ((position - uScale.yz) * uScale.x);
    position += uOffset;
    position = uGroup.yz + ((position - uGroup.yz) * uGroup.x);
    vUv = mix(uUv.xy, uUv.zw, corner);
    vLocal = corner * uRect.zw;
    gl_Position = vec4((position.x / uCanvas.x * 2.0) - 1.0, 1.0 - (position.y / uCanvas.y * 2.0), 0.0, 1.0);
}
