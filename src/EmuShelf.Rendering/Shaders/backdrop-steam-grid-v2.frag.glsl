// Steam · Library floor (backdrops.js steamGrid2): a dark floor of portrait cover tiles, each with two-tone
// "art", glowing one by one under a slow sweep of light.

out vec4 fragColor;

void main()
{
    vec2 p = pixelTopLeft(gl_FragCoord);
    float t = uTime;
    vec3 dark = rgb(23.0, 29.0, 40.0);
    vec3 colour = mix(dark, rgb(27.0, 40.0, 56.0), floor(p.y) / (uRes.y - 1.0));
    vec3 plane = planeAt(p, uRes.y * 0.3, 1.5, uRes.x * 0.6, sin(t * 0.05) * 0.08, 0.0, t * 0.3, false, 0.0);
    if (plane.z > 0.0)
    {
        float X = plane.x, Z = plane.y;
        float u = jmod(X, 1.3), v = jmod(Z, 1.9);
        vec3 tile;
        if (u < 0.1 || v < 0.1)
        {
            tile = rgb(14.0, 18.0, 26.0);
        }
        else
        {
            float id = hash1(floor(X / 1.3) * 7.7 + floor(Z / 1.9) * 3.1);
            float hue = 0.5 + id * 0.18, diagonal = (u / 1.3 + v / 1.9) / 2.0;
            vec3 art = mix(hsl(hue, 0.35, 0.22), hsl(hue + 0.06, 0.45, 0.42), diagonal > 0.5 ? 1.0 : diagonal * 2.0);
            float glow = pow(max(0.0, sin(t * 0.5 + id * 20.0)), 10.0) * 0.5;
            float sweep = exp(-pow(jmod(X * 0.5 + Z - t * 2.5, 30.0) - 15.0, 2.0) / 6.0) * 0.25;
            tile = mix(art, rgb(180.0, 220.0, 250.0), glow + sweep);
        }
        colour = mix(tile, dark, clamp((plane.z - 5.0) / 24.0, 0.0, 1.0));
    }
    fragColor = vec4(colour, 1.0);
}
