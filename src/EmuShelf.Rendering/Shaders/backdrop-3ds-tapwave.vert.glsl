// 3DS · Tap wave (backdrops.js n3TapWave): the Tile wave's grid, but tiles pop out of the screen in rings
// from random touch points. Lit by backdrop-3ds-tiles.frag.

out vec3 vWorld;
out vec3 vNormal;
out vec3 vBase;
flat out vec3 vEye;
flat out float vLightPace;

void main()
{
    int tile = gl_VertexID / 36;
    int j = tile / 13 - 3;
    int i = tile - (tile / 13) * 13 - 6;
    float t = uTime;
    float sway = sin(t * 0.33) * 1.1;
    vEye = vec3(sway, 0.0, 0.0);
    vLightPace = 0.8;
    if (abs(i) <= 1 && abs(j) <= 1)
    {
        gl_Position = Nowhere;
        vWorld = vec3(0.0);
        vNormal = vec3(0.0, 0.0, -1.0);
        vBase = vec3(0.0);
        return;
    }
    vec3 local, face;
    boxVertex(gl_VertexID - tile * 36, local, face);
    vec2 at = vec2(float(i), float(j)) * 1.62;
    float pop = 0.0;
    for (float k = floor((t - 4.0) / 1.3); k <= floor(t / 1.3); k += 1.0)
    {
        float tapped = k * 1.3, age = t - tapped;
        if (tapped > t || age >= 4.0)
            continue;
        float a = hash1(k) * PI * 2.0;
        vec2 tap = vec2(cos(a) * (4.0 + hash1(k + 0.5) * 4.0), sin(a) * (2.0 + hash1(k + 0.7) * 2.4));
        float w = length(at - tap) - age * 5.0;
        if (w < 0.0 && w > -2.4)
            pop += sin(-w / 2.4 * PI) * (1.0 - age / 4.0);
    }
    pop = clamp(pop, 0.0, 1.0);
    vWorld = local * vec3(0.56, 0.56, 0.14) + vec3(at, 11.5 - pop * 1.6);
    vNormal = normalize(local * vec3(1.0, 1.0, 2.4));
    vBase = mix(rgb(246.0, 248.0, 249.0), rgb(168.0, 226.0, 232.0), 0.25 + pop * 0.75);
    Cam cam = camera(vEye, atan(-sway, 11.0), 0.0, 260.0, vec2(200.0, 104.0));
    gl_Position = camClip(cam, camView(cam, vWorld));
}
