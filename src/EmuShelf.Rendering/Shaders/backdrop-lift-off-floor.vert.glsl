// Lift-off, the floor (ideas-bg.js liftOff): a dark plane under everything (so a gap is a hole), then 27 rows of up to 33 blocks
// running to the horizon. Each block is its top (a paler worn edge round a flat face, the face drawn by the fragment), its front, and
// the side you can see; lit brighter under the spotlight, shadowed under the blocks floating above it, and sunk out of sight while a
// new one rises into a gap. Vertices 0..5 are the plane; then 18 per block (top, front, side; two triangles each).

out vec3 vColour;
out vec3 vInner;
out vec2 vLocal;
flat out float vInset;

const int Columns = 33;

void hide() { gl_Position = vec4(2.0, 2.0, 2.0, 1.0); vColour = vec3(0.0); vInner = vec3(0.0); vLocal = vec2(9.0); vInset = 0.0; }

// A quad's corner k of its two triangles a b c, a c d.
int liftCorner(int k) { return k == 0 || k == 3 ? 0 : k == 1 ? 1 : k == 2 || k == 4 ? 2 : 3; }

void main()
{
    Cam cam = liftCamera();
    float t = uTime;
    vInset = 0.0;
    vLocal = vec2(9.0);
    if (gl_VertexID < 6)
    {
        int c = liftCorner(gl_VertexID);
        vec3 p = vec3(c == 0 || c == 3 ? -40.0 : 40.0, -1.6, c < 2 ? -1.0 : 34.0);
        vColour = liftShade(0.96);
        vInner = vColour;
        gl_Position = camClip(cam, camView(cam, p));
        return;
    }
    int id = gl_VertexID - 6, block = id / 18, local = id - block * 18, face = local / 6, c = liftCorner(local - face * 6);
    float j = float(block / Columns), i = float(block - (block / Columns) * Columns) - 16.0;
    float X = i * LiftSize, Z = 0.2 + j * LiftSize;
    vec2 cell = liftCell(i, j, t);
    if (abs(X) > Z * 0.82 + 2.6 || cell.x == 1.0)
    {
        hide();
        return;
    }
    float h = hash2(i, j), spot = 1.0 / (1.0 + pow(length(vec2(X, Z) - LiftSpot) / 2.8, 4.0));
    float y0 = (h - 0.5) * 0.2 + 0.05 * sin(t * 0.5 + i * 0.7 + j * 0.45), light = 0.2 + 1.02 * spot + (h - 0.5) * 0.08;
    if (cell.x == 2.0)
    {
        y0 -= 1.3 * (1.0 - sstep(0.0, 1.0, cell.y));
        light *= 0.5 + 0.5 * cell.y;
    }
    // Shadows of the blocks floating above, fading as they climb.
    for (int s = 0; s < LiftFlyCells; s++)
    {
        vec2 f = liftFlyCell(s), fc = liftCell(f.x, f.y, t);
        if (fc.x != 1.0)
            continue;
        vec2 d = vec2(X, Z) - vec2(f.x * LiftSize, 0.2 + f.y * LiftSize);
        float q = dot(d, d);
        if (q < 4.0)
            light *= 1.0 - 0.5 * (1.0 - fc.y) * sstep(1.9, 0.4, sqrt(q));
    }
    float x0 = X - LiftHalf, x1 = X + LiftHalf, z0 = Z - LiftHalf, z1 = Z + LiftHalf, yb = y0 - 2.0 * LiftHalf;
    float depth = camView(cam, vec3(x1, yb, z0)).z;
    vec3 p;
    if (face == 0)
    {
        p = vec3(c == 0 || c == 3 ? x0 : x1, y0, c < 2 ? z0 : z1);
        vColour = liftRamp(light * 1.22 + 0.05, depth);
        vInner = liftRamp(light, depth);
        vLocal = p.xz - vec2(X, Z);
        vInset = depth < 19.0 ? 1.0 : 0.0;
    }
    else if (face == 1)
    {
        p = vec3(c == 0 || c == 3 ? x0 : x1, c < 2 ? y0 : yb, z0);
        vColour = liftRamp(light * (c < 2 ? 0.5 : 0.3), depth);
        vInner = vColour;
    }
    else if (X > cam.pos.x)
    {
        p = vec3(x0, c < 2 ? y0 : yb, c == 0 || c == 3 ? z0 : z1);
        vColour = liftRamp(light * 0.2, depth);
        vInner = vColour;
    }
    else
    {
        p = vec3(x1, c < 2 ? y0 : yb, c == 0 || c == 3 ? z0 : z1);
        vColour = liftRamp(light * 0.4, depth);
        vInner = vColour;
    }
    gl_Position = camClip(cam, camView(cam, p));
}
