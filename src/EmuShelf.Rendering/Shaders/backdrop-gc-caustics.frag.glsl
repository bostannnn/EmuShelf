// GameCube · Caustic floor (backdrops.js gcCaustics): underwater light caustics dance across an indigo floor,
// the pattern warped twice the way indirect texturing warps one texture by another. Bloom in the output.

out vec4 fragColor;

void main()
{
    vec2 p = pixelTopLeft(gl_FragCoord);
    float x = floor(p.x), y = floor(p.y);
    float W = uRes.x, H = uRes.y, t = uTime;
    vec3 deep = rgb(22.0, 18.0, 56.0), indigo = rgb(52.0, 44.0, 120.0), violet = rgb(104.0, 84.0, 200.0);
    float hz = floor(H * 0.16);
    if (y < hz)
    {
        fragColor = vec4(mix(deep, indigo, y / hz), 1.0);
        return;
    }
    float zf = 26.0 * (H / 135.0) / (y - hz + 6.0);
    float fade = clamp(1.0 - zf * 0.55, 0.0, 1.0);
    vec3 base = mix(indigo, violet, fade * 0.8);
    float a = (x - W / 2.0) / (W / 240.0) * zf * 0.18, b = (zf * 2.2 + t * 0.22) * 3.0;
    a += 0.6 * sin(b * 1.3 + t * 0.7);
    b += 0.6 * cos(a * 1.1 - t * 0.6);
    a += 0.4 * sin(b * 1.7 - t * 0.5);
    float light = pow(1.0 - abs(sin(a) + sin(b)) / 2.0, 7.0) * fade;
    fragColor = vec4(base + light * vec3(200.0, 190.0, 255.0) / 255.0, 1.0);
}
