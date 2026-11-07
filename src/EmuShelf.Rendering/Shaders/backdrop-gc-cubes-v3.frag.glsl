// GameCube · Cube ripple, background: deep indigo.
out vec4 fragColor;
void main() { fragColor = vec4(mix(rgb(22.0, 18.0, 56.0), rgb(52.0, 44.0, 120.0), floor(pixelTopLeft(gl_FragCoord).y) / (uRes.y - 1.0)), 1.0); }
