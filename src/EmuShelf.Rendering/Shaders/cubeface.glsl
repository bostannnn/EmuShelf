// Shared by the three cubemap-baking passes: turns the face being rendered plus a position in that
// face's viewport into the world direction it represents, using GL's cubemap axis convention.

uniform int uFace;

vec3 faceDirection(vec2 ndc)
{
    float s = ndc.x;
    float t = ndc.y;

    if (uFace == 0) { return vec3(1.0, -t, -s); }
    if (uFace == 1) { return vec3(-1.0, -t, s); }
    if (uFace == 2) { return vec3(s, 1.0, t); }
    if (uFace == 3) { return vec3(s, -1.0, -t); }
    if (uFace == 4) { return vec3(s, -t, 1.0); }
    return vec3(-s, -t, -1.0);
}
