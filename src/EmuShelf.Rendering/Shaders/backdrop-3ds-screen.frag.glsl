// 3DS · Through the screen, a wall's fragment (ideas-3ds.js shade(), M47 B13): the 3DS-style per-pixel light from the prototype's
// lookup tables (a soft diffuse ramp, a crisp gloss, a Fresnel rim), the tint toward the lip, and fog into the sky behind; then the
// prototype's output curve and grain. Odd and even walls take the two materials. Every colour comes from uColour.

uniform vec3 uColour;
in vec3 vView;
in vec3 vNormal;
in float vTint;
flat in float vOdd;
flat in vec3 vLight;
out vec4 fragColor;

vec3 screenPalette(float v)
{
    vec3 c = uColour * 255.0;
    return v >= 0.0 ? mix(c, vec3(255.0, 250.0, 251.0), v) : mix(c, c * vec3(0.2, 0.12, 0.2), -v);
}

vec3 screenSky(vec2 p, float t)
{
    vec3 s = screenPalette(-0.97);
    vec2 d = p - vec2(238.0 + sin(t * 0.11) * 8.0, 106.0 + cos(t * 0.09) * 4.0);
    float e = d.x * d.x / (120.0 * 120.0) + d.y * d.y / (78.0 * 78.0);
    if (e < 16.0)
        s += (screenPalette(-0.05) - s) * exp(-e) * 0.95;
    e = d.x * d.x / (40.0 * 40.0) + d.y * d.y / (26.0 * 26.0);
    if (e < 16.0)
        s += (screenPalette(0.8) - s) * exp(-e) * 0.9;
    return s;
}

vec3 screenTone(vec3 c)
{
    float i = floor(gl_FragCoord.y) * uRes.x * BACKDROP_PIXEL + floor(gl_FragCoord.x);
    vec3 x = clamp((c + vec3(hash1(i * 0.731 + hash1(i * 0.113) * 9.7) - 0.5)) / 255.0, 0.0, 1.0);
    return x + (x * x * (3.0 - 2.0 * x) - x) * 0.4;
}

void main()
{
    vec3 n = normalize(vNormal), v = -normalize(vView), l = normalize(vLight);
    float nv = dot(n, v);
    if (nv < 0.0)
    {
        n = -n;
        nv = -nv;
    }
    float nl = dot(n, l);
    float d = pow(0.35 * sstep(-0.1, 0.4, nl) + 0.65 * sstep(-0.55, 1.0, nl), 1.15);
    bool odd = vOdd > 0.5;
    vec3 colour = mix(screenPalette(odd ? -0.985 : -0.975), screenPalette(odd ? -0.55 : -0.45), d);
    if (vTint > 0.0)
        colour += (screenPalette(odd ? 0.5 : 0.62) - colour) * vTint * (0.35 + 0.65 * d);
    float nh = (nl + nv) / sqrt(2.0004 + 2.0 * dot(l, v));
    if (nh > 0.0)
        colour += vec3(255.0, 247.0, 249.0) * sstep(0.3, 0.6, pow(min(1.0, nh), 70.0)) * 0.5;
    colour += screenPalette(0.45) * pow(clamp(1.0 - nv, 0.0, 1.0), 2.6) * 0.9;
    float fog = (vView.z - 4.2) / 27.0;
    if (fog > 0.0)
    {
        fog = min(fog, 1.0);
        colour += (screenSky(pixelTopLeft(gl_FragCoord), uTime) - colour) * fog * (2.0 - fog);
    }
    fragColor = vec4(screenTone(colour), 1.0);
}
