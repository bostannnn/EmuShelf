// Dreamcast · Block field, sky: a warm gradient into bright fog.
out vec4 fragColor;
void main()
{
    float u = floor(pixelTopLeft(gl_FragCoord).y) / (uRes.y - 1.0);
    fragColor = vec4(gradient3(u, rgb(252.0, 222.0, 198.0), 0.42, rgb(253.0, 238.0, 224.0), rgb(253.0, 238.0, 224.0)), 1.0);
}
