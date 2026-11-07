// A per-pixel lit material (the prototype's gTris with a material), for boxes and fields: the vertex shader
// hands over the base colour, the eye, the light, the material and the fog.

in vec3 vWorld;
in vec3 vNormal;
in vec3 vBase;
in float vFog;
flat in vec3 vEye;
flat in vec3 vLight;
flat in vec4 vMaterial;   // ambient, specular, shininess, rim
flat in vec3 vRimColour;
flat in vec3 vFogColour;
out vec4 fragColor;

void main()
{
    vec3 colour = litMaterial(vBase, vNormal, vWorld, vEye, vLight, vMaterial.x, vMaterial.y, vMaterial.z, vMaterial.w, vRimColour);
    fragColor = vec4(mix(colour, vFogColour, clamp(vFog, 0.0, 1.0)), 1.0);
}
