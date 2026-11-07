// PS3 · Light mesh (backdrops.js ps3Wire): the particle sheet's rolling surface drawn as a glowing wire mesh,
// added light on deep blue, with the output's trails and bloom. GL lines stand in for the prototype's
// antialiased 0.8px strokes (their average coverage, 0.8, folded into the colour). Vertices 0..3821 are the
// 49 lines running away from you (39 segments each), the rest the 38 lines across (56 segments each).

out vec4 vColor;

float sheet(float x, float z, float t) { return -1.4 + 0.6 * sin(x * 0.35 + t * 0.8) * cos(z * 0.22 - t * 0.5) + 0.25 * sin(z * 0.5 + x * 0.2 - t); }

void main()
{
    float t = uTime, x, z;
    vec3 colour;
    if (gl_VertexID < 49 * 78)
    {
        int line = gl_VertexID / 78, point = (gl_VertexID - line * 78) / 2 + (gl_VertexID & 1);
        x = float(line - 24) * 0.55;
        z = 2.0 + float(point) * 0.7;
        colour = rgb(120.0, 170.0, 255.0) * 0.22;
    }
    else
    {
        int id = gl_VertexID - 49 * 78, line = id / 112, point = (id - line * 112) / 2 + (id & 1);
        z = 2.0 + float(line) * 0.75;
        x = -14.0 + float(point) * 0.5;
        colour = rgb(150.0, 190.0, 255.0) * 0.3 * clamp(1.3 - z / 30.0, 0.0, 1.0);
    }
    Cam cam = camera(vec3(0.0, 1.4, 0.0), 0.0, 0.08, uRes.x * 0.62, vec2(uRes.x / 2.0, uRes.y * 0.43));
    vec3 eye = camView(cam, vec3(x, sheet(x, z, t), z));
    vColor = vec4(colour * 0.8, 1.0);
    gl_Position = eye.z > 0.4 ? pixelClip(camScreen(cam, eye), 0.0) : Nowhere;
}
