// Lift-off, the light (ideas-bg.js liftOff glow() and dot()): a soft glow behind each floating block (56), then 110 motes of dust
// climbing the beam, some as small glows and the rest as single soft dots. One quad each, added; sized in the prototype's pixels.

out vec2 vOffset;
flat out vec3 vShape;

void main()
{
    int splat = gl_VertexID / 6, k = gl_VertexID - splat * 6;
    vec2 cornerSign = vec2(k == 1 || k == 2 || k == 4 ? 1.0 : -1.0, k == 2 || k == 4 || k == 5 ? 1.0 : -1.0);
    Cam cam = liftCamera();
    float t = uTime;
    vec3 at;
    float radius, strength, kind;
    if (splat < LiftFlyCells)
    {
        vec2 f = liftFlyCell(splat), cell = liftCell(f.x, f.y, t);
        float v = cell.y;
        at = liftProject(cam, vec3(f.x * LiftSize, -LiftHalf + 7.4 * pow(v, 1.7), 0.2 + f.y * LiftSize));
        radius = min(46.0, 2.4 * cam.f / at.z);
        strength = cell.x == 1.0 ? 0.1 * sstep(1.0, 0.8, v) : 0.0;
        kind = 0.0;
    }
    else
    {
        float i = float(splat - LiftFlyCells);
        float hg = fract(hash1(i * 1.9 + 60.0) + t * 0.05 * (0.5 + hash1(i * 2.3 + 61.0)));
        float a = hash1(i * 3.1 + 62.0) * 2.0 * PI + t * 0.25 * (hash1(i * 4.7 + 63.0) - 0.5);
        float r = 2.6 * sqrt(hash1(i * 5.3 + 64.0)) * (0.45 + 0.55 * (1.0 - hg));
        at = liftProject(cam, vec3(LiftSpot.x + r * cos(a) + 1.7 * hg, hg * 8.2, LiftSpot.y + r * sin(a)));
        float s = sstep(0.0, 0.08, hg) * sstep(1.0, 0.8, hg) * (0.6 + 0.4 * sin(t * 1.3 + i * 2.1));
        bool glow = hash1(i * 7.1) > 0.78;
        radius = glow ? 3.4 : 1.0;
        strength = glow ? s * 0.55 : s * 0.8;
        kind = glow ? 0.0 : 1.0;
    }
    if (at.z < 0.3 || strength <= 0.0)
    {
        gl_Position = vec4(2.0, 2.0, 2.0, 1.0);
        vOffset = vec2(0.0);
        vShape = vec3(1.0, 0.0, 0.0);
        return;
    }
    vOffset = cornerSign * radius;
    vShape = vec3(radius, strength, kind);
    gl_Position = pixelClip(at.xy + vOffset, 0.0);
}
