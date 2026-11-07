// Premultiplied colour times the layer's opacity; blended with (ONE, ONE_MINUS_SRC_ALPHA).

in vec2 vUv;

uniform sampler2D uTexture;
uniform float uOpacity;

out vec4 fragColor;

void main()
{
    fragColor = texture(uTexture, vUv) * uOpacity;
}
