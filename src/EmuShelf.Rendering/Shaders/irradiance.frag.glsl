// Cosine-convolves the studio cubemap into the diffuse irradiance the shell's matte surfaces see.
// Baked once per accent colour, into a 32px cube — irradiance is so low-frequency that anything
// larger is wasted memory.

in vec2 vNdc;
out vec4 fragColor;

uniform samplerCube uEnvironment;

const float PI = 3.14159265359;

void main()
{
    vec3 N = normalize(faceDirection(vNdc));

    vec3 up = abs(N.y) > 0.99 ? vec3(0.0, 0.0, 1.0) : vec3(0.0, 1.0, 0.0);
    vec3 right = normalize(cross(up, N));
    up = cross(N, right);

    vec3 irradiance = vec3(0.0);
    float samples = 0.0;

    // Uniform lat/long march over the hemisphere. The sin() weight corrects for the poles being
    // oversampled by this parameterisation.
    const float phiStep = 0.025 * 2.0 * PI;
    const float thetaStep = 0.025 * 0.5 * PI;

    for (float phi = 0.0; phi < 2.0 * PI; phi += phiStep)
    {
        for (float theta = 0.0; theta < 0.5 * PI; theta += thetaStep)
        {
            vec3 tangentSample = vec3(sin(theta) * cos(phi), sin(theta) * sin(phi), cos(theta));
            vec3 world = (tangentSample.x * right) + (tangentSample.y * up) + (tangentSample.z * N);

            irradiance += texture(uEnvironment, world).rgb * cos(theta) * sin(theta);
            samples += 1.0;
        }
    }

    fragColor = vec4(PI * irradiance / max(samples, 1.0), 1.0);
}
