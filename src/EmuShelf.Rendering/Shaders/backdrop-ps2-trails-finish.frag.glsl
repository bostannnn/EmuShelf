// PS2 · Light trails over tiles, the floor (backdrops.js ps2Trails3): below the horizon row the trails are
// mirrored in a dark glass floor with faint tile lines in perspective. Drawn after the feedback, so the
// reflection never feeds back into the trails.

uniform sampler2D uScene;
out vec4 fragColor;

vec3 row(float x, float y) { return texelFetch(uScene, ivec2(int(x), int(uRes.y - 1.0 - y)), 0).rgb; }

void main()
{
    vec2 p = pixelTopLeft(gl_FragCoord);
    float x = floor(p.x), y = floor(p.y), t = uTime;
    const float horizon = 172.0, f = 300.0;
    vec3 colour = row(x, y);
    if (y > horizon)
    {
        float k = 0.36 * (1.0 - (y - horizon) / (uRes.y - horizon));
        float dist = 1.6 * f / (y - horizon);
        float X = (x - 240.0) * dist / f;
        bool line = jmod(dist + t * 1.2, 2.0) < 0.06 * dist / 6.0 + 0.02 || jmod(X, 2.0) < 0.05 * dist / 6.0 + 0.02;
        float g = line ? 22.0 * clamp(1.4 - dist / 24.0, 0.0, 1.0) / 255.0 : 0.0;
        colour = colour * vec3(0.5, 0.5, 0.55) + row(x, 2.0 * horizon - y) * k + g * vec3(0.7, 0.85, 1.4);
    }
    else if (y == horizon)
    {
        colour += rgb(40.0, 50.0, 80.0);
    }
    fragColor = vec4(colour, 1.0);
}
