// PS1 · Folded field, texels: one of the prototype's 16×16 textures (a base colour, noise, a dark edge and a
// small cross), read at whole texels, shaded per face and fogged toward the horizon.

in vec2 vUv;
in float vDepth;
in float vShade;
flat in int vTexture;
out vec4 fragColor;

vec3 textureBase(int n)
{
    if (n == 0) return vec3(124.0, 153.0, 160.0);
    if (n == 1) return vec3(151.0, 164.0, 168.0);
    if (n == 2) return vec3(176.0, 176.0, 184.0);
    if (n == 3) return vec3(157.0, 169.0, 163.0);
    return vec3(137.0, 151.0, 171.0);
}

void main()
{
    int u = int(clamp(floor(vUv.x), 0.0, 15.0)), v = int(clamp(floor(vUv.y), 0.0, 15.0));
    vec3 base = textureBase(vTexture);
    float noise = (hash1(float(vTexture * 256 + v * 16 + u)) - 0.5) * 13.0 + (imod(u + v, 7) == 0 ? 6.0 : 0.0) + (u == 0 || v == 0 ? -7.0 : 0.0);
    vec3 texel = base + noise;
    if ((u == 3 && v >= 3 && v <= 5) || (v == 3 && u >= 3 && u <= 5))
        texel = base + 20.0;
    vec3 colour = texel / 255.0 * vShade;
    fragColor = vec4(mix(colour, rgb(233.0, 233.0, 222.0), clamp((vDepth - 5.0) / 29.0, 0.0, 0.97)), 1.0);
}
