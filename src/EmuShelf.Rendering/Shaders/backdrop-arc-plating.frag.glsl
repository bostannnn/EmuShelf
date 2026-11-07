// Arcade · Hull plating (backdrops.js arcPlating): riveted metal plates of a vertical shooter scroll slowly
// downward, a few hatches glowing cyan. CRT scanlines and bloom in the output.

out vec4 fragColor;

void main()
{
    vec2 p = pixelTopLeft(gl_FragCoord);
    float x = floor(p.x), y = floor(p.y), t = uTime;
    const float S = 32.0;
    float Y = y - t * 14.0, u = jmod(x, S), v = jmod(Y, S);
    float h = hash1(floor(x / S) * 7.3 + floor(Y / S) * 3.1);
    vec3 colour = mix(rgb(46.0, 52.0, 66.0), rgb(70.0, 78.0, 96.0), h * 0.6);
    if (u < 1.0 || v < 1.0)
        colour = rgb(96.0, 104.0, 124.0);
    else if (u > S - 2.0 || v > S - 2.0)
        colour = rgb(24.0, 26.0, 36.0);
    else if ((u == 4.0 || u == S - 5.0) && (v == 4.0 || v == S - 5.0))
        colour = rgb(150.0, 156.0, 172.0);
    else if (h > 0.82 && u > 8.0 && u < S - 8.0 && v > 8.0 && v < S - 8.0)
        colour = mix(rgb(20.0, 24.0, 30.0), rgb(80.0, 220.0, 255.0), (0.5 + 0.5 * sin(t * 3.0 + h * 20.0)) * ((int(floor(v)) & 3) < 2 ? 1.0 : 0.4));
    else if (h < 0.15 && u > 6.0 && u < S - 6.0 && ((int(floor(u)) + int(floor(v))) & 7) < 2)
        colour = rgb(34.0, 38.0, 50.0);
    if (abs(x - uRes.x / 2.0) < 34.0 && abs(y - uRes.y * 0.43) < 30.0)
        colour = mix(colour, rgb(30.0, 32.0, 44.0), 0.4);
    fragColor = vec4(colour, 1.0);
}
