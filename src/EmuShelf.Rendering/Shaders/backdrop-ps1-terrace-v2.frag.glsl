// PS1 · Textured terraces, sky.
out vec4 fragColor;
void main()
{
    float u = floor(pixelTopLeft(gl_FragCoord).y) / (uRes.y - 1.0);
    fragColor = vec4(gradient3(u, rgb(188.0, 204.0, 213.0), 0.5, rgb(233.0, 233.0, 222.0), rgb(233.0, 233.0, 222.0)), 1.0);
}
