// PS3 · Particle sheet, background (backdrops.js ps3Bg): deep blue-black.

out vec4 fragColor;

void main()
{
    float u = floor(pixelTopLeft(gl_FragCoord).y) / (uRes.y - 1.0);
    fragColor = vec4(gradient3(u, rgb(4.0, 6.0, 16.0), 0.6, rgb(18.0, 22.0, 50.0), rgb(40.0, 36.0, 82.0)), 1.0);
}
