// Lift-off, the sky (ideas-bg.js liftOff): from the colour's mid shade at the top to the haze at the horizon, row by row.

out vec4 fragColor;

void main()
{
    float y = pixelIndex(pixelTopLeft(gl_FragCoord).y);
    float horizon = floor(122.0 - 310.0 * tan(0.29) + 0.5);
    float g = clamp(y / horizon, 0.0, 1.0);
    fragColor = vec4(mix(liftShade(0.5), liftShade(0.12), g * g) / 255.0, 1.0);
}
