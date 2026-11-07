// Dreamcast · Sunny checker (backdrops.js dcChecker): an orange-and-white checker floor turns slowly under a
// bright blue sky, with a soft gloss at the horizon. Crisp VGA, no filter.

out vec4 fragColor;

void main()
{
    vec2 p = pixelTopLeft(gl_FragCoord);
    float y = floor(p.y), t = uTime;
    float hz = uRes.y * 0.38;
    vec3 colour = gradient3(y / (uRes.y - 1.0), rgb(30.0, 110.0, 220.0), 0.38, rgb(170.0, 220.0, 250.0), rgb(170.0, 220.0, 250.0));
    vec3 plane = planeAt(p, hz, 1.5, uRes.x * 0.6, t * 0.04, 0.0, t, false, 0.0);
    if (plane.z > 0.0)
    {
        int check = (int(floor(plane.x / 1.5)) + int(floor(plane.y / 1.5))) & 1;
        vec3 tile = check == 1 ? rgb(240.0, 124.0, 62.0) : rgb(250.0, 250.0, 248.0);
        colour = mix(tile, rgb(236.0, 240.0, 244.0), clamp((plane.z - 10.0) / 40.0, 0.0, 1.0));
    }
    if (y >= floor(hz) + 1.0)
        colour = mix(colour, rgb(200.0, 230.0, 255.0), 0.18 * exp(-(y - hz) / 14.0));
    fragColor = vec4(colour, 1.0);
}
