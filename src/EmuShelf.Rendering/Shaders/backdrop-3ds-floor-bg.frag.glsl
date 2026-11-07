// 3DS · Tile floor wave, background (backdrops.js fBg with the floor's paler top).

out vec4 fragColor;

void main()
{
    vec2 p = pixelTopLeft(gl_FragCoord);
    float x = floor(p.x), y = floor(p.y), t = uTime;
    vec3 c = mix(rgb(224.0, 238.0, 242.0), rgb(246.0, 248.0, 249.0), y / uRes.y);
    vec2 b1 = vec2(110.0 + sin(t * 0.07) * 70.0, 70.0 + cos(t * 0.05) * 25.0);
    vec2 b2 = vec2(300.0 + cos(t * 0.06) * 60.0, 150.0 + sin(t * 0.08) * 25.0);
    float g1 = exp(-dot(vec2(x, y) - b1, vec2(x, y) - b1) / 9000.0) * 0.4;
    float g2 = exp(-dot(vec2(x, y) - b2, vec2(x, y) - b2) / 7000.0) * 0.28;
    fragColor = vec4(c + (rgb(178.0, 226.0, 232.0) - c) * g1 + (rgb(252.0, 214.0, 204.0) - c) * g2, 1.0);
}
