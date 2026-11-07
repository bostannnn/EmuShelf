// Steam · Low-poly terrain, background: dark slate.

out vec4 fragColor;

void main()
{
    float u = floor(pixelTopLeft(gl_FragCoord).y) / (uRes.y - 1.0);
    fragColor = vec4(mix(rgb(23.0, 29.0, 40.0), rgb(27.0, 40.0, 56.0), u), 1.0);
}
