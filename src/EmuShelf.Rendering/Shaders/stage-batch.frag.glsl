// stage.frag for Big Type's batched sprites (M47 B6): the same colour, tint and circles, with each quad's opacity and texture from its
// instance, and its window rectangle tested here instead of by the scissor. A pixel is kept exactly when the scissor would keep it:
// its centre (gl_FragCoord, at half pixels) lies in [x0, x1) × [y0, y1) of whole pixels.

in vec2 vUv;
flat in float vOpacity;
flat in float vSource;
flat in vec4 vScissor;

uniform sampler2D uTexture;
uniform sampler2D uTexture1;
uniform vec3 uTint;
uniform float uTintAmount;
uniform vec4 uClip0;
uniform vec4 uClip1;
uniform vec4 uClip2;

out vec4 fragColor;

float circle(vec4 clip)
{
    if (clip.w == 0.0)
        return 1.0;
    float inside = clamp(clip.z - distance(gl_FragCoord.xy, clip.xy) + 0.5, 0.0, 1.0);
    return clip.w > 0.0 ? inside : 1.0 - inside;
}

void main()
{
    if (vScissor.z > vScissor.x &&
        (gl_FragCoord.x < vScissor.x || gl_FragCoord.x >= vScissor.z || gl_FragCoord.y < vScissor.y || gl_FragCoord.y >= vScissor.w))
        discard;
    vec4 colour = vSource < 0.5 ? texture(uTexture, vUv) : texture(uTexture1, vUv);
    if (uTintAmount > 0.0)
        colour.rgb = mix(colour.rgb, uTint * colour.a, uTintAmount);
    float cover = circle(uClip0) * circle(uClip1) * circle(uClip2);
    fragColor = colour * (vOpacity * cover);
}
