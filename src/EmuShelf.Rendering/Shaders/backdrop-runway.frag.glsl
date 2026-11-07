// SNES · Prism runway (Codex's motion lab, experiments.fragment.html runway): a 512×512 tile map sampled
// in Mode 7 perspective, slowly turning and scrolling under a stepped sky, with three sprite-like prisms
// spinning at the edges. The tile map is the prototype's, worked out per texel instead of stored.

out vec4 fragColor;

vec3 palette(int i)
{
    if (i == 0) return rgb(238.0, 229.0, 238.0);
    if (i == 1) return rgb(189.0, 175.0, 218.0);
    if (i == 2) return rgb(153.0, 194.0, 185.0);
    if (i == 3) return rgb(229.0, 184.0, 187.0);
    if (i == 4) return rgb(232.0, 210.0, 165.0);
    return rgb(166.0, 176.0, 209.0);
}

vec3 tileMap(int x, int y)
{
    int tx = x >> 5, ty = y >> 5, u = x & 31, v = y & 31;
    vec3 c = palette(((tx + ty) & 1) == 1 ? 0 : 1);
    if (imod(tx, 8) == 0 || imod(ty, 8) == 0) c = rgb(234.0, 225.0, 241.0);
    if (imod(tx + ty * 3, 13) == 3) c = palette(2);
    if (imod(tx * 3 + ty, 17) == 6) c = palette(3);
    if (imod(tx + ty, 19) == 8) c = palette(4);
    if (u < 1 || v < 1) c = mix(c, rgb(115.0, 105.0, 155.0), 0.13);
    if (u == 2 || v == 2) c = mix(c, rgb(255.0, 248.0, 239.0), 0.3);
    if (((u == 6 && v >= 6 && v < 12) || (v == 6 && u >= 6 && u < 12)) && imod(tx + ty, 3) == 0)
        c = mix(c, rgb(255.0, 252.0, 240.0), 0.5);
    return c;
}

// One spinning prism (the prototype's diamond): a soft shadow, the gem, its left half and top facet, a glint.
vec3 prism(vec3 colour, vec2 p, vec2 at, float size, float spin, vec3 body, vec3 side, vec3 facet)
{
    float width = abs(cos(spin)) * size * 0.65 + size * 0.25;
    vec2 shadow = floor(vec2(at.x, at.y + size * 0.85) + 0.5);
    vec2 e = (p - shadow) / vec2(size * 0.72, size * 0.16);
    if (dot(e, e) <= 1.0)
        colour = mix(colour, rgb(94.0, 82.0, 124.0), 0.157);
    vec2 top = floor(vec2(at.x, at.y - size) + 0.5), left = floor(vec2(at.x - width, at.y) + 0.5);
    vec2 bottom = floor(vec2(at.x, at.y + size) + 0.5), right = floor(vec2(at.x + width, at.y) + 0.5);
    vec2 inner = floor(vec2(at.x, at.y - size * 0.13) + 0.5);
    if (inTriangle(p, top, left, bottom) || inTriangle(p, top, bottom, right))
        colour = body;
    if (inTriangle(p, top, left, bottom))
        colour = side;
    if (inTriangle(p, top, right, inner))
        colour = facet;
    vec2 glint = floor(vec2(at.x - width * 0.45, at.y - size * 0.4) + 0.5);
    if (p.x >= glint.x && p.x < glint.x + 2.0 && p.y >= glint.y && p.y < glint.y + 2.0)
        colour = rgb(255.0, 244.0, 220.0);
    return colour;
}

void main()
{
    vec2 p = pixelTopLeft(gl_FragCoord);
    float x = floor(p.x), y = floor(p.y), t = uTime;
    vec3 fog = rgb(244.0, 234.0, 229.0);
    vec3 colour = mix(rgb(198.0, 191.0, 225.0), fog, floor(clamp(y / 67.0, 0.0, 1.0) * 24.0 + 0.5) / 24.0);
    if (y >= 68.0)
    {
        float a = 0.24 * sin(t * 0.115), ca = cos(a), sa = sin(a);
        float z = 2600.0 / (y - 67.0);
        float haze = pow(clamp((z - 45.0) / 420.0, 0.0, 1.0), 0.8) * 0.95;
        float xx = (x - 160.0) * z / 175.0, zz = z + t * 16.0;
        int u = int(jmod(floor(xx * ca - zz * sa + 256.0), 512.0));
        int v = int(jmod(floor(xx * sa + zz * ca + 256.0), 512.0));
        colour = mix(tileMap(u, v), fog, haze);
    }
    colour = prism(colour, p, vec2(34.0 + sin(t * 0.2) * 4.0, 103.0 + sin(t * 0.35) * 2.0), 13.0, t * 0.35,
        rgb(185.0, 127.0, 187.0), rgb(151.0, 105.0, 165.0), rgb(226.0, 180.0, 214.0));
    colour = prism(colour, p, vec2(285.0 + sin(t * 0.2 + 2.0) * 5.0, 88.0 + cos(t * 0.3) * 2.0), 10.0, t * 0.35 + 1.0,
        rgb(116.0, 188.0, 174.0), rgb(82.0, 156.0, 156.0), rgb(182.0, 221.0, 197.0));
    colour = prism(colour, p, vec2(308.0 + sin(t * 0.16) * 4.0, 153.0 + sin(t * 0.28) * 2.0), 17.0, t * 0.26,
        rgb(209.0, 172.0, 115.0), rgb(182.0, 146.0, 105.0), rgb(241.0, 213.0, 160.0));
    fragColor = vec4(colour, 1.0);
}
