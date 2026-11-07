// PS2 · Light trails, the particles (backdrops.js ps2Trails): 1500 glowing points in three tilted streams
// orbit the folder, brighter on the near side, plus 40 drifting sparkles; each a 3×3 or single-pixel splat.

out vec4 vColor;
flat out float vSpread;

void main()
{
    float t = uTime;
    vec2 at;
    vec3 colour;
    float strength, spread;
    if (gl_VertexID < 1500)
    {
        int i = gl_VertexID, s = i - (i / 3) * 3;
        float phase = float(i) / 1500.0 * PI * 2.0 * 7.0;
        float a = phase + t * (0.22 + 0.06 * float(s)) * (s == 1 ? -1.0 : 1.0);
        float r = 118.0 + 22.0 * sin(3.0 * a + t * 0.45 + float(s) * 2.0) + float(imod(i * 37, 17) - 8);
        float tilt = s == 0 ? -0.35 : s == 1 ? 0.25 : 0.05, squash = s == 0 ? 0.36 : s == 1 ? 0.46 : 0.3;
        vec2 o = vec2(cos(a) * r * 1.18, sin(a) * r * squash);
        vec2 turned = vec2(o.x * cos(tilt) - o.y * sin(tilt), o.x * sin(tilt) + o.y * cos(tilt));
        float depth = sin(a);
        strength = (0.1 + 0.08 * depth) * (0.6 + 0.4 * sin(float(i) * 1.7 + t * 2.0));
        at = vec2(240.0, 118.0 + float(s) * 6.0) + turned;
        colour = s == 0 ? rgb(110.0, 160.0, 255.0) : s == 1 ? rgb(175.0, 135.0, 255.0) : rgb(100.0, 230.0, 255.0);
        spread = depth > 0.4 ? 1.0 : 0.0;
    }
    else
    {
        float i = float(gl_VertexID - 1500);
        float a = i * 2.39 + t * 0.05, r = 60.0 + jmod(i * 53.0 + t * 14.0, 170.0);
        at = vec2(240.0 + cos(a) * r * 1.3, 118.0 + sin(a) * r * 0.6);
        strength = 0.25 * (0.5 + 0.5 * sin(t * 3.0 + i));
        colour = rgb(200.0, 220.0, 255.0);
        spread = 1.0;
    }
    vSpread = spread;
    vColor = vec4(colour * strength, 0.0);
    gl_PointSize = spread > 0.5 ? 3.0 : 1.0;
    gl_Position = pixelClip(floor(at + 0.5) + 0.5, 0.0);
}
