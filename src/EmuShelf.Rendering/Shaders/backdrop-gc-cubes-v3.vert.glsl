// GameCube · Cube ripple (backdrops.js gcField2): a dense field of small glossy cubes (21 × 16) ripples out in
// rings under a slowly circling light while the camera glides. Lit per pixel by backdrop-lit.frag.

out vec3 vWorld;
out vec3 vNormal;
out vec3 vBase;
out float vFog;
flat out vec3 vEye;
flat out vec3 vLight;
flat out vec4 vMaterial;
flat out vec3 vRimColour;
flat out vec3 vFogColour;

void main()
{
    int cube = gl_VertexID / 36;
    int j = cube / 21;
    int i = cube - j * 21 - 10;
    vec3 local, normal;
    boxVertex(gl_VertexID - cube * 36, local, normal);
    float t = uTime, x = float(i) * 0.9, z = 2.5 + float(j) * 0.9;
    float wave = 0.5 + 0.5 * sin(length(vec2(x, z - 10.0)) * 0.55 - t * 1.2);
    float h = 0.15 + 0.9 * wave * wave;
    bool hot = hash1(float(i * 13 + j * 7)) > 0.97;
    vWorld = local * vec3(0.4, h / 2.0, 0.4) + vec3(x, h / 2.0 - 1.0, z);
    vNormal = normal;
    vBase = hot ? rgb(252.0, 196.0, 120.0) : mix(rgb(52.0, 44.0, 120.0), rgb(176.0, 164.0, 236.0), 0.25 + wave * 0.55);
    float drift = sin(t * 0.08);
    Cam cam = camera(vec3(drift * 1.5, 3.2, -1.0), drift * -0.05, 0.3, uRes.x * 0.62, vec2(uRes.x / 2.0, uRes.y * 0.4));
    vEye = cam.pos;
    vLight = normalize(vec3(cos(t * 0.3) * 0.5, 0.7, -0.4));
    vMaterial = vec4(0.28, 1.0, 50.0, 0.5);
    vRimColour = rgb(200.0, 190.0, 255.0);
    vFogColour = rgb(22.0, 18.0, 56.0);
    vec3 eye = camView(cam, vWorld);
    vFog = clamp((eye.z - 5.0) / 13.0, 0.0, 1.0);
    gl_Position = camClip(cam, eye);
}
