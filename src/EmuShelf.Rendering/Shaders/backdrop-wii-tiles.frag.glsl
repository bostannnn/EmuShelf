// Wii · Tile floor (backdrops.js wiiTiles): a soft white floor of rounded tiles recedes into light while a
// Wii-blue sheen sweeps over it.

out vec4 fragColor;

void main()
{
    vec2 p = pixelTopLeft(gl_FragCoord);
    float t = uTime;
    vec3 colour = gradient3(floor(p.y) / (uRes.y - 1.0), rgb(250.0, 251.0, 252.0), 0.3, rgb(240.0, 244.0, 248.0), rgb(224.0, 228.0, 233.0));
    vec3 plane = planeAt(p, uRes.y * 0.3, 1.4, uRes.x * 0.6, sin(t * 0.07) * 0.12, 0.0, t * 0.5, false, 0.0);
    if (plane.z > 0.0)
    {
        float X = plane.x, Z = plane.y;
        vec2 uv = vec2(jmod(X, 1.2) - 0.6, jmod(Z, 1.2) - 0.6);
        bool inTile = length(max(vec2(0.0), abs(uv) - 0.38)) < 0.14;
        float sweep = exp(-pow(jmod(X * 0.4 + Z - t * 3.0, 26.0) - 13.0, 2.0) / 3.0);
        vec3 tile = inTile ? mix(rgb(244.0, 247.0, 250.0), vec3(1.0), clamp(0.5 - uv.y, 0.0, 1.0) * 0.5) : rgb(206.0, 222.0, 232.0);
        tile = mix(tile, inTile ? rgb(200.0, 234.0, 250.0) : rgb(52.0, 190.0, 237.0), sweep * 0.55);
        colour = mix(tile, rgb(244.0, 247.0, 250.0), clamp((plane.z - 6.0) / 30.0, 0.0, 1.0));
    }
    fragColor = vec4(colour, 1.0);
}
