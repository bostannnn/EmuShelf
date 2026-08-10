// GGX-prefilters the studio cubemap into the specular chain the shell reflects. Mip N holds the
// environment as seen at roughness N/maxLod, so the fragment shader gets a whole family of blurs
// from one textureLod().

in vec2 vNdc;
out vec4 fragColor;

uniform samplerCube uEnvironment;
uniform float uRoughness;
uniform float uEnvironmentSize;
uniform int uSampleCount;

const float PI = 3.14159265359;

// Van der Corput radical inverse — the low-discrepancy half of a Hammersley sequence.
float radicalInverseVdC(uint bits)
{
    bits = (bits << 16u) | (bits >> 16u);
    bits = ((bits & 0x55555555u) << 1u) | ((bits & 0xAAAAAAAAu) >> 1u);
    bits = ((bits & 0x33333333u) << 2u) | ((bits & 0xCCCCCCCCu) >> 2u);
    bits = ((bits & 0x0F0F0F0Fu) << 4u) | ((bits & 0xF0F0F0F0u) >> 4u);
    bits = ((bits & 0x00FF00FFu) << 8u) | ((bits & 0xFF00FF00u) >> 8u);
    return float(bits) * 2.3283064365386963e-10;
}

vec3 importanceSampleGGX(vec2 xi, vec3 N, float roughness)
{
    float a = roughness * roughness;

    float phi = 2.0 * PI * xi.x;
    float cosTheta = sqrt((1.0 - xi.y) / (1.0 + (((a * a) - 1.0) * xi.y)));
    float sinTheta = sqrt(1.0 - (cosTheta * cosTheta));

    vec3 h = vec3(cos(phi) * sinTheta, sin(phi) * sinTheta, cosTheta);

    vec3 up = abs(N.z) < 0.999 ? vec3(0.0, 0.0, 1.0) : vec3(1.0, 0.0, 0.0);
    vec3 tangent = normalize(cross(up, N));
    vec3 bitangent = cross(N, tangent);

    return normalize((tangent * h.x) + (bitangent * h.y) + (N * h.z));
}

void main()
{
    vec3 N = normalize(faceDirection(vNdc));
    // The usual split-sum simplification: assume the viewer looks straight down the normal.
    vec3 R = N;
    vec3 V = N;

    vec3 colour = vec3(0.0);
    float totalWeight = 0.0;

    for (int i = 0; i < uSampleCount; i++)
    {
        vec2 xi = vec2(float(i) / float(uSampleCount), radicalInverseVdC(uint(i)));
        vec3 H = importanceSampleGGX(xi, N, uRoughness);
        vec3 L = normalize((2.0 * dot(V, H) * H) - V);

        float NoL = dot(N, L);
        if (NoL <= 0.0)
        {
            continue;
        }

        // Sample from a mip chosen by how much solid angle this sample covers. Without this the
        // bright softboxes alias into a field of sparkling dots at mid roughness.
        float NoH = max(dot(N, H), 0.0);
        float VoH = max(dot(V, H), 0.0);
        float d = (NoH * NoH * ((uRoughness * uRoughness * uRoughness * uRoughness) - 1.0)) + 1.0;
        float D = (uRoughness * uRoughness * uRoughness * uRoughness) / max(PI * d * d, 1e-6);
        float pdf = ((D * NoH) / (4.0 * max(VoH, 1e-4))) + 1e-4;

        float texelSolidAngle = (4.0 * PI)
            / (6.0 * uEnvironmentSize * uEnvironmentSize);
        float sampleSolidAngle = 1.0 / (float(uSampleCount) * pdf);
        float mip = uRoughness <= 0.0
            ? 0.0
            : max(0.5 * log2(sampleSolidAngle / texelSolidAngle), 0.0);

        colour += textureLod(uEnvironment, L, mip).rgb * NoL;
        totalWeight += NoL;
    }

    fragColor = vec4(colour / max(totalWeight, 1e-4), 1.0);
}
