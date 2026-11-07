// PS2 · Chrome sea, sky (backdrops.js ps2Sea): a hazy gradient with a low sun glowing ahead.

out vec4 fragColor;

void main()
{
    vec2 p = pixelTopLeft(gl_FragCoord);
    float x = floor(p.x), y = floor(p.y), t = uTime;
    vec3 colour = mix(rgb(132.0, 154.0, 214.0), rgb(222.0, 224.0, 236.0), clamp(y / 102.0, 0.0, 1.0));
    if (y < 130.0)
    {
        Cam cam = camera(vec3(sin(t * 0.05) * 1.4, 1.35 + sin(t * 0.3) * 0.04, 0.0), -sin(t * 0.05) * 0.04, 0.06, 300.0, vec2(240.0, 118.0));
        vec3 sun = camView(cam, cam.pos + normalize(vec3(0.0, 0.13, 1.0)) * 1000.0);
        vec2 s = vec2(cam.c.x + sun.x * cam.f / sun.z, cam.c.y - sun.y * cam.f / sun.z);
        float d2 = (x - s.x) * (x - s.x) + (y - s.y) * 1.25 * (y - s.y) * 1.25;
        float glow = exp(-d2 / 700.0) * 0.85 + exp(-d2 / 7000.0) * 0.22;
        colour += glow * (rgb(255.0, 246.0, 228.0) - colour);
    }
    fragColor = vec4(colour, 1.0);
}
