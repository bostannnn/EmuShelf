// 3DS · Tile floor wave (backdrops.js n3FloorWave): the Tile wave's glossy pillow tiles laid as a floor
// (17 × 12), lifting in a slow ring wave while the view sways for stereo depth. Lit by backdrop-3ds-tiles.frag.

out vec3 vWorld;
out vec3 vNormal;
out vec3 vBase;
flat out vec3 vEye;
flat out float vLightPace;

void main()
{
    int tile = gl_VertexID / 36;
    int j = tile / 17;
    int i = tile - j * 17 - 8;
    vec3 local, face;
    boxVertex(gl_VertexID - tile * 36, local, face);
    vec3 pillow = normalize(local * vec3(1.0, 1.0, 2.4));
    float t = uTime, fi = float(i), fj = float(j);
    float x = fi * 1.2, z = 4.0 + fj * 1.2;
    float wave = pow(max(0.0, sin(length(vec2(x, z - 9.0)) * 0.6 - t * 1.2)), 3.0);
    vec3 flat90 = vec3(PI / 2.0, 0.0, 0.0);
    vWorld = rotateZXY(local * vec3(0.56, 0.56, 0.14), flat90) + vec3(x, -1.6 + wave * 0.9, z);
    vNormal = rotateZXY(pillow, flat90);
    bool hot = ((i * 7 + j * 13) & 15) == 3;
    vBase = hot ? rgb(246.0, 150.0, 136.0) : mix(rgb(246.0, 248.0, 249.0), rgb(168.0, 226.0, 232.0), 0.25 + wave * 0.75);
    float sway = sin(t * 0.33) * 0.9;
    vEye = vec3(sway, 2.6, 0.0);
    vLightPace = 0.7;
    Cam cam = camera(vEye, atan(-sway, 10.0), 0.28, 260.0, vec2(200.0, 96.0));
    gl_Position = camClip(cam, camView(cam, vWorld));
}
