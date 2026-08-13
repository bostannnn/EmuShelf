// Bakes the studio the shell is lit by, one cube face per draw.
//
// This is a *procedural* environment rather than a shipped HDR panorama, for three reasons: an
// equirectangular HDR that survives being reflected in glossy plastic is a multi-megabyte asset,
// EmuShelf is a portable app that should not carry one, and generating it lets the studio pick up
// the focused system's accent colour so the hero belongs to the shelf it sits on.

in vec2 vNdc;
out vec4 fragColor;

// Linear-space accent of the focused system, and how far the room is allowed to take that tint.
uniform vec3 uAccent;
uniform float uAccentMix;
uniform float uIntensity;

// A rectangular softbox at unit distance along `dir`. Rectangles, not points: the giveaway that a
// surface is moulded plastic rather than painted card is a long soft-edged bar of light sliding
// across it, and that shape has to exist in the environment for the reflection to find it.
float softbox(vec3 d, vec3 dir, vec2 halfSize, float softness)
{
    vec3 n = normalize(dir);
    float denom = dot(d, n);
    if (denom <= 1e-4)
    {
        return 0.0;
    }

    vec3 p = (d / denom) - n;
    vec3 reference = abs(n.y) > 0.95 ? vec3(0.0, 0.0, 1.0) : vec3(0.0, 1.0, 0.0);
    vec3 right = normalize(cross(n, reference));
    vec3 up = cross(right, n);

    vec2 q = abs(vec2(dot(p, right), dot(p, up)));
    vec2 edge = vec2(1.0) - smoothstep(halfSize - softness, halfSize + softness, q);
    return edge.x * edge.y;
}

void main()
{
    vec3 d = normalize(faceDirection(vNdc));

    // Room: a dark floor rising to a soft grey ceiling. Kept deliberately dim — the softboxes below
    // carry the image, and a bright room would drown their reflections in flat ambient.
    float height = (d.y * 0.5) + 0.5;
    vec3 floorTone = vec3(0.006, 0.007, 0.009);
    vec3 horizonTone = vec3(0.020, 0.022, 0.028);
    vec3 ceilingTone = vec3(0.052, 0.058, 0.072);

    vec3 room = mix(floorTone, horizonTone, smoothstep(0.0, 0.5, height));
    room = mix(room, ceilingTone, smoothstep(0.45, 1.0, height));

    // Tint the room, not the lights: the accent should colour the ambient the plastic sits in
    // without turning the white softboxes into coloured ones.
    vec3 colour = mix(room, room * uAccent, uAccentMix);

    // The lights sit mostly IN FRONT of the subject rather than above it. A flat vertical face
    // reflects the hemisphere in front of it, so an overhead-only rig — the intuitive setup — puts
    // every highlight where the shell can never show it, and the case comes out looking matte.
    // This is a photographer's beauty-dish-beside-the-camera arrangement instead.

    // Key: broad, front, up and to the left. The soft wash that rides across the sleeve on a turn.
    colour += vec3(1.0, 0.985, 0.955) * 7.0
        * softbox(d, vec3(-0.55, 0.42, 1.0), vec2(0.50, 0.30), 0.16);

    // Glint: narrow and bright, front-right, with a hard edge. This is the crisp bar that reads
    // unmistakably as polished plastic when the shell swings the other way.
    colour += vec3(1.0, 0.97, 0.93) * 44.0
        * softbox(d, vec3(0.85, 0.30, 0.72), vec2(0.28, 0.09), 0.05);

    // Overhead: shapes the diffuse and lights the top edge, without being the main reflection.
    colour += vec3(0.92, 0.95, 1.0) * 1.6
        * softbox(d, vec3(-0.15, 1.0, 0.25), vec2(0.75, 0.40), 0.35);

    // Rim: behind and above, separating the shell's shoulder from the shelf backdrop.
    colour += vec3(1.0, 0.96, 0.92) * 5.0
        * softbox(d, vec3(0.35, 0.55, -1.0), vec2(0.45, 0.12), 0.09);

    // Bounce: a faint slab below the front, standing in for the surface the case sits on.
    colour += vec3(0.55, 0.60, 0.72) * 0.6
        * softbox(d, vec3(0.0, -0.92, 0.45), vec2(0.85, 0.50), 0.40);

    fragColor = vec4(colour * uIntensity, 1.0);
}
