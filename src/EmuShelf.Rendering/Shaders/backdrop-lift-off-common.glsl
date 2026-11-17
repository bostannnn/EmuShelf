// Lift-off (ideas-bg.js liftOff, M47 B13): what every pass of the default background shares. Included after backdrop-common.
// Its palette is built only from the colour it is given (uColour, the focused item's), so a change of colour is a change of input.

uniform vec3 uColour;

// The prototype's tones(c): sh(k) toward a near-black of the colour's own hue, ti(k) toward white; 0..255.
vec3 liftColour() { return uColour * 255.0; }
vec3 liftShade(float k) { vec3 c = liftColour(); return mix(c, c * 0.05 + vec3(4.0, 4.0, 8.0), k); }
vec3 liftTint(float k) { return mix(liftColour(), vec3(255.0), k); }

float hash2(float a, float b) { return hash1(a * 12.9898 + b * 78.233 + 0.5); }

const float LiftSize = 1.25, LiftHalf = 0.56, LiftInset = 0.47;
const vec2 LiftSpot = vec2(2.5, 8.6);

Cam liftCamera() { return camera(vec3(0.0, 3.7, -1.8), 0.0, 0.29, 310.0, vec2(240.0, 122.0)); }

// A floor cell's state at time t: x 0 resting, 1 floating up, 2 a new block rising into the gap; y how far along.
vec2 liftCell(float i, float j, float t)
{
    vec2 d = vec2(i * LiftSize, 0.2 + j * LiftSize) - LiftSpot;
    if (length(d) >= 3.5 || hash2(i + 31.0, j + 17.0) >= 0.56)
        return vec2(0.0);
    float period = 11.0 + 9.0 * hash2(i + 7.0, j + 3.0), u = fract(t / period + hash2(i + 1.0, j + 9.0));
    return u < 0.7 ? vec2(1.0, u / 0.7) : vec2(2.0, (u - 0.7) / 0.3);
}

// The prototype's ramp(I, z): dark, mid and light from the colour, fading to the haze far off.
vec3 liftRamp(float light, float z)
{
    light = clamp(light, 0.0, 1.3);
    vec3 dark = liftShade(0.88), mid = liftShade(0.4), lit = liftTint(0.36);
    vec3 c = light < 0.5 ? mix(dark, mid, light * 2.0) : mix(mid, lit, (light - 0.5) * 1.25);
    return mix(c, liftShade(0.12), pow(clamp((z - 8.0) / 20.0, 0.0, 1.0), 1.4));
}

// A view-space point as screen pixels (the prototype's proj), and its depth in z.
vec3 liftProject(Cam cam, vec3 world)
{
    vec3 e = camView(cam, world);
    return vec3(cam.c + vec2(e.x, -e.y) * cam.f / e.z, e.z);
}

// The floating blocks: 8 rows (3..10) of 7 cells (-1..5) around the spotlight.
const int LiftFlyCells = 56;
vec2 liftFlyCell(int slot) { return vec2(float(slot - (slot / 7) * 7) - 1.0, float(slot / 7) + 3.0); }
