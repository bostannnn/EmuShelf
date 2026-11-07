// Mega Drive · Checker tube (backdrops.js mdPipe2): a checker tube wraps all the way around and bends gently,
// darker overhead, dithering away in the distance, the fast pseudo-3D of 16-bit bonus stages.

out vec4 fragColor;

void main()
{
    vec2 p = pixelTopLeft(gl_FragCoord);
    float x = floor(p.x), y = floor(p.y), t = uTime;
    float f = uRes.x * 0.55;
    float dx = (p.x - uRes.x / 2.0) / f, dy = -(p.y - uRes.y * 0.43) / f;
    float z = 2.2 / max(0.02, length(vec2(dx, dy)));
    float angle = atan(dy, dx) + sin(z * 0.08 - t * 0.3) * 0.4;
    float q = angle * 8.0 / PI;
    int check = (int(floor(q)) + int(floor(z * 0.5 + t * 6.0))) & 1;
    bool upper = dy > 0.0;
    vec3 navy = rgb(0.0, 0.0, 72.0), dark = rgb(0.0, 36.0, 109.0), blue = rgb(36.0, 72.0, 182.0), cyan = rgb(72.0, 182.0, 255.0);
    vec3 colour = jmod(q, 1.0) < 0.08 ? vec3(1.0) : check == 1 ? (upper ? blue : cyan) : (upper ? dark : blue);
    if (z > 26.0 && ((int(x) + int(y)) & 1) == 1)
        colour = navy;
    if (z > 40.0)
        colour = navy;
    fragColor = vec4(colour, 1.0);
}
