// DS · Ripple floor, the tap marks: a 5 × 3 navy stamp where each tap lands, fading over 0.35 s. Nine tap
// slots (the taps of the last five seconds), one quad each.

out vec4 vColor;

void main()
{
    int slot = gl_VertexID / 6, k = gl_VertexID - slot * 6;
    float t = uTime;
    float key = floor((t - 5.0) / 0.8) + float(slot), age = t - key * 0.8;
    Cam cam = camera(vec3(0.0, 2.4, 0.0), 0.0, 0.2, 200.0, vec2(128.0, 58.0));
    vec3 eye = camView(cam, vec3((hash1(key) - 0.5) * 14.0, 0.0, 5.0 + hash1(key + 0.5) * 14.0));
    if (age < 0.0 || age >= 0.35 || eye.z <= 0.5)
    {
        gl_Position = Nowhere;
        vColor = vec4(0.0);
        return;
    }
    vec2 centre = floor(camScreen(cam, eye) + 0.5);
    ivec2 corner = quadCorner(k);
    vec2 at = centre + vec2(corner.y == 0 ? -2.0 : 3.0, corner.x == 0 ? -1.0 : 2.0);
    vColor = vec4(rgb(44.0, 56.0, 92.0), 1.0 - age / 0.35);
    gl_Position = pixelClip(at, 0.0);
}
