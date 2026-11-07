// GameCube · Glass lenses (backdrops.js gcLens): glass lenses glide over an indigo grid and bend it as they
// pass, the indirect-texture refraction the GameCube was known for. Bloom in the output.

out vec4 fragColor;

void main()
{
    vec2 p = pixelTopLeft(gl_FragCoord);
    float x = floor(p.x), y = floor(p.y), t = uTime, W = uRes.x, H = uRes.y;
    float X = x, Y = y, light = 0.0;
    for (int k = 0; k < 4; k++)
    {
        float fk = float(k);
        vec2 centre = vec2(W / 2.0 + cos(t * (0.21 + fk * 0.04) + fk * 1.7) * W * 0.36, H * 0.45 + sin(t * (0.27 + fk * 0.03) + fk * 2.3) * H * 0.3);
        float r = H * (0.17 + float(k & 1) * 0.07);
        vec2 d = vec2(x, y) - centre;
        float d2 = dot(d, d), r2 = r * r;
        if (d2 < r2)
        {
            float bend = 1.0 - d2 / r2;
            X -= d.x * bend * 0.5;
            Y -= d.y * bend * 0.5;
            light += pow(1.0 - bend, 10.0) * 0.5 + exp(-(pow(d.x + r * 0.35, 2.0) + pow(d.y + r * 0.4, 2.0)) / (r2 * 0.02)) * 0.6;
        }
    }
    float gx = min(jmod(X, 12.0), 12.0 - jmod(X, 12.0)), gy = min(jmod(Y, 12.0), 12.0 - jmod(Y, 12.0));
    float line = max(clamp(1.0 - gx, 0.0, 1.0), clamp(1.0 - gy, 0.0, 1.0));
    float dots = gx < 1.6 && gy < 1.6 ? 1.0 : 0.0;
    vec3 base = mix(rgb(52.0, 44.0, 120.0), rgb(104.0, 84.0, 200.0), y / H);
    fragColor = vec4(mix(mix(base, rgb(176.0, 164.0, 236.0), line * 0.45 + dots * 0.4), vec3(1.0), clamp(light, 0.0, 1.0)), 1.0);
}
