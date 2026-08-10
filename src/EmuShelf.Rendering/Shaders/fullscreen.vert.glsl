// Attribute-less fullscreen triangle, used by every cubemap-baking pass. One oversized triangle
// beats a quad: no diagonal seam, no vertex buffer, no VAO state to get wrong.

out vec2 vNdc;

void main()
{
    vec2 corner = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
    vNdc = (corner * 2.0) - 1.0;
    gl_Position = vec4(vNdc, 0.0, 1.0);
}
