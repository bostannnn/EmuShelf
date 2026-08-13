// Screen-space-cheap, world-space-stable approximation of a large softbox shadow. A tight contact
// lobe anchors each object while a wider offset lobe conveys the key-light direction.

in vec2 vPlanePosition;
out vec4 fragColor;

const int MAX_SHADOWS = 7;
uniform int uShadowCount;
// centre x/z and half-width/depth of the medium's footprint.
uniform vec4 uShadowFootprint[MAX_SHADOWS];
// Per-item opacity; focused media lift slightly and therefore cast a softer, lighter shadow.
uniform float uShadowOpacity[MAX_SHADOWS];

void main()
{
    float alpha = 0.0;
    for (int i = 0; i < MAX_SHADOWS; i++)
    {
        if (i >= uShadowCount)
        {
            break;
        }

        vec4 footprint = uShadowFootprint[i];
        vec2 radius = max(footprint.zw, vec2(0.015));
        vec2 contactDelta = (vPlanePosition - footprint.xy) / radius;
        float contact = exp(-dot(contactDelta, contactDelta) * 3.2);

        vec2 castCentre = footprint.xy + vec2(0.055, -0.085);
        vec2 castDelta = (vPlanePosition - castCentre) / (radius * vec2(1.35, 2.1));
        float castShadow = exp(-dot(castDelta, castDelta) * 2.0);

        float itemAlpha = max(contact * 0.34, castShadow * 0.19) * uShadowOpacity[i];
        alpha = 1.0 - ((1.0 - alpha) * (1.0 - itemAlpha));
    }

    // Premultiplied black: RGB stays zero while alpha darkens the Avalonia backdrop underneath.
    fragColor = vec4(0.0, 0.0, 0.0, clamp(alpha, 0.0, 0.52));
}
