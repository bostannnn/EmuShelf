// A PS1-style texel: one of the 16×16 textures read at whole texels (affinely interpolated, since the vertex
// shader leaves w = 1), shaded per face and fogged.

in vec2 vUv;
in float vShade;
in float vFog;
flat in int vTexture;
flat in int vTextureSet;   // 0: Codex's textures, 1: the motion lab's
flat in vec3 vFogColour;
out vec4 fragColor;

void main()
{
    int u = int(clamp(floor(vUv.x), 0.0, 15.0)), v = int(clamp(floor(vUv.y), 0.0, 15.0));
    vec3 texel = vTextureSet == 0 ? codexTexel(vTexture, u, v) : labTexel(vTexture, u, v);
    fragColor = vec4(mix(texel * vShade, vFogColour, clamp(vFog, 0.0, 0.97)), 1.0);
}
