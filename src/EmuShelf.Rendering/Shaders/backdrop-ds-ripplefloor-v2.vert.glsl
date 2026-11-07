// DS · Ripple floor (backdrops.js dsRippleFloor2): a graph-paper floor drawn as single dots, where every 0.8 s a
// tap sends a ripple out that lifts the grid into ice blue and sinks it into navy, in the DS's stepped fog.
// Vertices 0..13719 are the 49 lines running away from you (280 dots each), the rest the 38 across (551 each).

out vec4 vColor;

float height(float x, float z, float t)
{
    float s = 0.0;
    for (float k = floor((t - 5.0) / 0.8); k <= floor(t / 0.8); k += 1.0)
    {
        float tk = k * 0.8, age = t - tk;
        if (tk > t || age >= 5.0)
            continue;
        vec2 tap = vec2((hash1(k) - 0.5) * 14.0, 5.0 + hash1(k + 0.5) * 14.0);
        float w = length(vec2(x, z) - tap) - age * 2.6;
        if (w < 0.0 && w > -3.0)
            s += sin(w * 3.0) * 0.5 * (1.0 - age / 5.0) * (1.0 + w / 3.0);
    }
    return s;
}

void main()
{
    float t = uTime, x, z;
    if (gl_VertexID < 49 * 280)
    {
        int line = gl_VertexID / 280;
        x = float(line - 24) * 0.75;
        z = 2.0 + float(gl_VertexID - line * 280) * 0.1;
    }
    else
    {
        int id = gl_VertexID - 49 * 280, line = id / 551;
        z = 2.0 + float(line) * 0.75;
        x = -22.0 + float(id - line * 551) * 0.08;
    }
    float y = height(x, z, t);
    Cam cam = camera(vec3(0.0, 2.4, 0.0), 0.0, 0.2, 200.0, vec2(128.0, 58.0));
    vec3 eye = camView(cam, vec3(x, y, z));
    gl_PointSize = 1.0;
    if (eye.z < 0.5)
    {
        gl_Position = Nowhere;
        vColor = vec4(0.0);
        return;
    }
    float fog = floor(clamp((z - 3.0) / 26.0, 0.0, 1.0) * 8.0) / 8.0;
    vec3 ink = y > 0.12 ? rgb(106.0, 170.0, 222.0) : y < -0.12 ? rgb(44.0, 56.0, 92.0) : rgb(150.0, 162.0, 188.0);
    vColor = vec4(mix(ink, rgb(240.0, 243.0, 247.0), fog * 0.85), 1.0);
    gl_Position = pixelClip(floor(camScreen(cam, eye) + 0.5) + 0.5, 0.0);
}
