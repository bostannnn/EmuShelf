// Big Type's batched sprites (M47 B6): sprite.vert's placement, with each quad's own values read from uQuads by instance instead of
// set as uniforms per draw, so a name's letters or a rolling line's pieces go in one draw. The arithmetic is sprite.vert's, line for
// line, so every quad lands on the same pixels. Five vec4s a quad: its rectangle; its texture coordinates; its linear transform; its
// translation, opacity and which of the two textures it reads; and the window rectangle it is cut to (x0, y0, x1, y1 in the surface's
// pixels; x1 <= x0 cuts nothing). Attribute-less: four vertices as a triangle strip, one instance a quad.

uniform vec4 uQuads[240];  // StageCompositor.BatchQuads quads at most
uniform vec2 uCanvas;
uniform vec3 uScale;
uniform vec2 uOffset;
uniform vec3 uGroup;
uniform vec3 uFit;         // where the canvas sits on the surface: x, y (from the bottom-left) and its scale

out vec2 vUv;
flat out float vOpacity;
flat out float vSource;
flat out vec4 vScissor;

void main()
{
    int base = gl_InstanceID * 5;
    vec4 rect = uQuads[base];
    vec4 uv = uQuads[base + 1];
    vec4 linear = uQuads[base + 2];
    vec4 extra = uQuads[base + 3];
    vec4 window = uQuads[base + 4];
    vec2 corner = vec2(float(gl_VertexID & 1), float((gl_VertexID >> 1) & 1));
    vec2 position = rect.xy + (corner * rect.zw);
    vec2 along = corner;
    if (window.z > window.x)
    {
        // A quad cut to a window (a roll's piece: no transform of its own) is shrunk to that window grown by a pixel each side, so
        // the pixels outside it are never shaded; the edges that move lie outside the window, and its texture coordinates follow
        // the same straight line, so every pixel the window keeps is drawn as before. The fragment test still decides each one.
        vec2 low = vec2((window.x - 1.0 - uFit.x) / uFit.z, uCanvas.y - ((window.w + 1.0 - uFit.y) / uFit.z));
        vec2 high = vec2((window.z + 1.0 - uFit.x) / uFit.z, uCanvas.y - ((window.y - 1.0 - uFit.y) / uFit.z));
        position = clamp(position, low, high);
        along = (position - rect.xy) / rect.zw;
    }
    position = vec2((position.x * linear.x) + (position.y * linear.z), (position.x * linear.y) + (position.y * linear.w)) + extra.xy;
    position = uScale.yz + ((position - uScale.yz) * uScale.x);
    position += uOffset;
    position = uGroup.yz + ((position - uGroup.yz) * uGroup.x);
    vUv = mix(uv.xy, uv.zw, along);
    vOpacity = extra.z;
    vSource = extra.w;
    vScissor = window;
    gl_Position = vec4((position.x / uCanvas.x * 2.0) - 1.0, 1.0 - (position.y / uCanvas.y * 2.0), 0.0, 1.0);
}
