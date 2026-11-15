// Lift-off, the spotlight's beam (ideas-bg.js liftOff): a soft column of light, wider at the floor, leaning to the spot, added.

out vec4 fragColor;

void main()
{
    vec2 p = pixelTopLeft(gl_FragCoord);
    float y = p.y - 0.5;
    vec3 spot = liftProject(liftCamera(), vec3(LiftSpot.x, 0.0, LiftSpot.y));
    float fr = (y + 50.0) / (spot.y + 50.0), centre = spot.x + 70.0 * (1.0 - fr), width = 24.0 + 62.0 * fr;
    float strength = 0.3 * (0.45 + 0.55 * fr) * sstep(spot.y + 40.0, spot.y - 30.0, y);
    float u = (p.x - centre) / width, u2 = u * u;
    float g = y < spot.y + 40.0 ? strength / (1.0 + u2 * u2) : 0.0;
    fragColor = vec4(liftTint(0.55) * g / 255.0, 0.0);
}
