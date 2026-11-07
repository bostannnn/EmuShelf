// 3DS · Tile wave, lighting (backdrops.js fTris): the 3DS GPU's fragment lighting as the prototype did it, a
// stepped diffuse ramp, a lookup-table-crisp highlight and a Fresnel rim, per pixel, under a moving light.

in vec3 vWorld;
in vec3 vNormal;
in vec3 vBase;
flat in vec3 vEye;
flat in float vLightPace;
out vec4 fragColor;

void main()
{
    vec3 eye = vEye;
    float lt = uTime * vLightPace;
    vec3 light = normalize(vec3(cos(lt * 0.3) * 0.65, 0.62, -0.55 + sin(lt * 0.3) * 0.25));
    vec3 n = normalize(vNormal);
    vec3 view = normalize(eye - vWorld);
    float facing = dot(n, view);
    if (facing < 0.0)
    {
        n = -n;
        facing = -facing;
    }
    float d = dot(n, light);
    float diffuse = 0.55 * sstep(-0.05, 0.3, d) + 0.45 * clamp(d * 0.5 + 0.5, 0.0, 1.0);
    float highlight = sstep(0.3, 0.6, pow(max(0.0, dot(n, normalize(light + view))), 46.0));
    float rim = pow(1.0 - facing, 2.6) * 0.45;
    fragColor = vec4(vBase * (0.42 + 0.68 * diffuse) + highlight + rgb(214.0, 246.0, 250.0) * rim, 1.0);
}
