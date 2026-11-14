// Premultiplied colour times the layer's opacity; blended with (ONE, ONE_MINUS_SRC_ALPHA).
// uTintAmount (0 unless set, M47 B4) replaces the colour with uTint at the layer's own coverage: a picture of white text or
// of a white veil mask is drawn in the zone's ink. At 0 the picture is drawn exactly as it was.

in vec2 vUv;

uniform sampler2D uTexture;
uniform float uOpacity;
uniform vec3 uTint;
uniform float uTintAmount;

out vec4 fragColor;

void main()
{
    vec4 colour = texture(uTexture, vUv);
    if (uTintAmount > 0.0)
        colour.rgb = mix(colour.rgb, uTint * colour.a, uTintAmount);
    fragColor = colour * uOpacity;
}
