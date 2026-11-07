// DS · Flipbook doodles (backdrops.js dsFlip): five hand-drawn outlines boil at 8 frames a second (every frame
// re-jitters the line) and morph from shape to shape, each with three sparkle dashes. 1px two-colour lines:
// per spot 72 segments of outline, then 3 dashes; endpoints on pixel centres, as Bresenham drew them.

out vec4 vColor;

vec2 shapeAt(int n, float a)
{
    if (n == 0)
        return vec2(cos(a), sin(a));
    if (n == 1)
    {
        float r = 1.0 + 0.38 * cos(5.0 * a);
        return vec2(r * cos(a), r * sin(a)) * 0.8;
    }
    if (n == 2)
    {
        float c = cos(a), s = sin(a);
        return vec2(sign(c) * pow(abs(c), 0.35), sign(s) * pow(abs(s), 0.35)) * 0.9;
    }
    if (n == 3)
    {
        float r = 1.0 + 0.18 * cos(8.0 * a);
        return vec2(r * cos(a), r * sin(a)) * 0.9;
    }
    float s = sin(a);
    return vec2(0.95 * s * s * s, -(0.78 * cos(a) - 0.3 * cos(2.0 * a) - 0.12 * cos(3.0 * a) - 0.05 * cos(4.0 * a)));
}

void main()
{
    int spot = gl_VertexID / 150, within = gl_VertexID - spot * 150;
    int segment = within / 2, end = within & 1;
    vec3 s = spot == 0 ? vec3(46.0, 52.0, 26.0) : spot == 1 ? vec3(212.0, 50.0, 24.0) : spot == 2 ? vec3(58.0, 112.0, 16.0)
        : spot == 3 ? vec3(200.0, 110.0, 18.0) : vec3(128.0, 16.0, 12.0);
    float t = uTime, i = float(spot), frame = floor(t * 8.0);
    vec3 colour = (spot & 1) == 1 ? rgb(106.0, 170.0, 222.0) : rgb(44.0, 56.0, 92.0);
    vec2 at;
    float alpha = 1.0;
    if (segment < 72)
    {
        float k = float(segment + end);
        float cycle = t * 0.33 + i * 0.9;
        int A = imod(int(floor(cycle)), 5), B = imod(A + 1, 5);
        float morph = sstep(0.55, 1.0, fract(cycle)), bounce = 1.0 + 0.08 * sin(t * 3.0 + i), rot = sin(t * 0.4 + i) * 0.3;
        float a = k / 72.0 * PI * 2.0;
        vec2 p = mix(shapeAt(A, a), shapeAt(B, a), morph);
        vec2 jitter = (vec2(hash1(frame * 7.1 + k * 0.37 + i), hash1(frame * 3.3 + k * 0.71 + i)) - 0.5) * 1.4;
        at = s.xy + vec2((p.x * cos(rot) - p.y * sin(rot)) * s.z, (p.x * sin(rot) + p.y * cos(rot)) * s.z / bounce) + jitter;
    }
    else
    {
        float m = float(segment - 72);
        float a = hash1(frame + m * 9.0 + i * 31.0) * PI * 2.0, r = s.z * 1.35 + hash1(frame * 2.0 + m + i) * 4.0;
        at = s.xy + vec2(cos(a), sin(a)) * (r + float(end) * 3.0);
        alpha = 0.8;
    }
    vColor = vec4(colour, alpha);
    gl_Position = pixelClip(floor(at + 0.5) + 0.5, 0.0);
}
