// Arcade · Checker floor (backdrops.js arcChecker): a green-and-cream checker floor races toward a pink
// horizon, swaying side to side. CRT scanlines and bloom in the output.

out vec4 fragColor;

void main()
{
    vec2 p = pixelTopLeft(gl_FragCoord);
    float t = uTime;
    vec3 colour = gradient3(floor(p.y) / (uRes.y - 1.0), rgb(40.0, 90.0, 220.0), 0.42, rgb(250.0, 170.0, 200.0), rgb(250.0, 170.0, 200.0));
    vec3 plane = planeAt(p, uRes.y * 0.42, 1.2, uRes.x * 0.6, 0.0, sin(t * 0.4) * 3.0, t * 5.0, false, 0.0);
    if (plane.z > 0.0)
    {
        int check = (int(floor(plane.x / 1.6)) + int(floor(plane.y / 1.6))) & 1;
        vec3 tile = check == 1 ? rgb(60.0, 200.0, 90.0) : rgb(240.0, 240.0, 220.0);
        colour = mix(tile, rgb(250.0, 190.0, 210.0), clamp((plane.z - 10.0) / 40.0, 0.0, 1.0));
    }
    fragColor = vec4(colour, 1.0);
}
