// Dreamcast · Blue sea, sky: Sega blue down to a bright haze.
out vec4 fragColor;
void main()
{
    float u = floor(pixelTopLeft(gl_FragCoord).y) / (uRes.y - 1.0);
    fragColor = vec4(gradient3(u, rgb(30.0, 110.0, 220.0), 0.42, rgb(160.0, 215.0, 250.0), rgb(160.0, 215.0, 250.0)), 1.0);
}
