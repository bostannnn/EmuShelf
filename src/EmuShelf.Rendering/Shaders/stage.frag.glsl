// Premultiplied colour times the layer's opacity; blended with (ONE, ONE_MINUS_SRC_ALPHA).
// uTintAmount (0 unless set, M47 B4) replaces the colour with uTint at the layer's own coverage: a picture of white text or
// of a white veil mask is drawn in the zone's ink. At 0 the picture is drawn exactly as it was.
// uClip0..2 (all zero unless set, M47 B6) cut the layer to circles in the surface's pixels: x, y from the bottom-left, the
// radius, and w = 1 to keep what is inside, -1 what is outside (0 is off), with a one-pixel soft edge. Big Type's flood and
// wipe use them; with all three off the picture is drawn exactly as it was.

in vec2 vUv;

uniform sampler2D uTexture;
uniform float uOpacity;
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
    vec4 colour = texture(uTexture, vUv);
    if (uTintAmount > 0.0)
        colour.rgb = mix(colour.rgb, uTint * colour.a, uTintAmount);
    float cover = circle(uClip0) * circle(uClip1) * circle(uClip2);
    fragColor = colour * (uOpacity * cover);
}
