// 3DS · Through the screen, the sky (ideas-3ds.js throughScreen's sky(), M47 B13): the console's colour shaded nearly to its wine,
// with a wide glow and a bright core where the run of openings leads. Drawn from the colour it is given (uColour), then the
// prototype's output curve and grain (present()). The walls' fog fades into the same sky (backdrop-3ds-screen.frag).

uniform vec3 uColour;
out vec4 fragColor;

// The prototype's palette: v from -1 (deep wine) through 0 (the colour) to 1 (near-white), in 0..255.
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

// The prototype's output: a little grain against banding, then a gentle contrast curve.
vec3 screenTone(vec3 c)
{
    float i = floor(gl_FragCoord.y) * uRes.x * BACKDROP_PIXEL + floor(gl_FragCoord.x);
    vec3 x = clamp((c + vec3(hash1(i * 0.731 + hash1(i * 0.113) * 9.7) - 0.5)) / 255.0, 0.0, 1.0);
    return x + (x * x * (3.0 - 2.0 * x) - x) * 0.4;
}

void main()
{
    fragColor = vec4(screenTone(screenSky(pixelTopLeft(gl_FragCoord), uTime)), 1.0);
}
