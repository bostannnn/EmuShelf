// Game Boy Advance · Bright corridor (backdrops.js gbaCorridor2): the camera drifts down a raycast corridor
// of cream and lavender panels with teal windows; the floor reflects the walls through the GBA's alpha blend.

out vec4 fragColor;

const vec3 Vio = vec3(112.0, 101.0, 167.0) / 255.0;
const vec3 Lav = vec3(178.0, 170.0, 230.0) / 255.0;
const vec3 Cream = vec3(248.0, 240.0, 220.0) / 255.0;
const vec3 Teal = vec3(96.0, 176.0, 190.0) / 255.0;

vec3 wall(float u, float v, float side)
{
    float m = jmod(u, 2.0);
    bool seam = m < 0.08 || v < 0.05 || v > 0.95;
    bool window = hash1(floor(u / 2.0) * 3.1 + side) > 0.55 && v > 0.28 && v < 0.64 && m > 0.35 && m < 1.65;
    return seam ? Vio : window ? mix(Teal, rgb(220.0, 250.0, 250.0), 0.3) : v < 0.5 ? Cream : mix(Cream, Lav, 0.5);
}

void main()
{
    vec2 p = pixelTopLeft(gl_FragCoord);
    float f = uRes.x * 0.55, hz = uRes.y * 0.43, camZ = uTime * 1.6;
    float dx = (p.x - uRes.x / 2.0) / f;
    float zw = 2.0 / max(1e-3, abs(dx));
    float top = hz - 1.3 * f / zw, bottom = hz + f / zw, side = dx < 0.0 ? 0.0 : 1.0, u = camZ + zw;
    float z;
    vec3 colour;
    if (p.y < top)
    {
        z = 1.3 * f / (hz - p.y);
        colour = jmod(camZ + z, 3.0) < 0.5 && abs(dx * z) < 1.2 ? rgb(255.0, 250.0, 240.0) : Lav;
    }
    else if (p.y > bottom)
    {
        z = f / (p.y - hz);
        int check = (int(floor(dx * z)) + int(floor(camZ + z))) & 1;
        colour = check == 1 ? Lav : Cream;
        float reflected = 1.0 - (p.y - bottom) / (bottom - top);
        if (reflected > 0.0)
            colour = mix(colour, wall(u, reflected, side), 0.28 * reflected);
    }
    else
    {
        z = zw;
        colour = wall(u, (p.y - top) / (bottom - top), side);
    }
    fragColor = vec4(mix(colour, rgb(206.0, 198.0, 238.0), clamp((z - 3.0) / 24.0, 0.0, 1.0)), 1.0);
}
