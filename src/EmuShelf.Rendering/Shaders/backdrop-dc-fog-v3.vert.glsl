// Dreamcast · Block field (backdrops.js dcFog3): mostly white blocks with a few orange ones ripple out of a
// warm, bright fog as the camera glides (17 × 14, streaming toward you). Lit per pixel by backdrop-lit.frag.

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
    int block = gl_VertexID / 36;
    int j = block / 17;
    int i = block - j * 17 - 8;
    vec3 local, normal;
    boxVertex(gl_VertexID - block * 36, local, normal);
    float t = uTime;
    float z = jmod(float(j) * 1.3 - t * 1.1, 18.2) + 3.0;
    float wave = 0.5 + 0.5 * sin(float(i) * 0.5 + z * 0.45 - t * 0.6);
    float h = 0.2 + 1.1 * wave * wave;
    bool hot = hash1(float(i * 5 + j * 3)) > 0.88;
    vWorld = local * vec3(0.5, h / 2.0, 0.5) + vec3(float(i) * 1.3, h / 2.0 - 1.0, z);
    vNormal = normal;
    vBase = hot ? rgb(240.0, 124.0, 62.0) : mix(rgb(250.0, 250.0, 248.0), rgb(235.0, 240.0, 250.0), wave);
    float drift = sin(t * 0.07);
    Cam cam = camera(vec3(drift * 1.2, 2.4, 0.0), drift * -0.04, 0.16, uRes.x * 0.62, vec2(uRes.x / 2.0, uRes.y * 0.43));
    vEye = cam.pos;
    vLight = normalize(vec3(0.5, 0.8, -0.3));
    vMaterial = vec4(0.62, 0.3, 20.0, 0.1);
    vRimColour = vec3(1.0);
    vFogColour = rgb(253.0, 238.0, 224.0);
    vec3 eye = camView(cam, vWorld);
    vFog = clamp((eye.z - 5.0) / 15.0, 0.0, 1.0);
    gl_Position = camClip(cam, eye);
}
