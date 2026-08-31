// Presents the shelf through a CRT tube: barrel-warped glass, a scanned beam, a phosphor mask, the
// falloff of a lens that was never flat, and the handful of instabilities that make a tube read as
// powered rather than printed. This replaces the plain resolve blit, so it is also the pass that
// composites the scene over its backdrop and writes the opaque image the window sees.
//
// Every stage is knob-driven and every knob reaches Settings, because none of this can be judged by
// argument — only by sitting in front of it. Setting uIntensity to 0 must reproduce the pre-CRT
// image exactly, so the effect can always be turned off rather than merely turned down.

in vec2 vNdc;
out vec4 fragColor;

uniform sampler2D uScene;
uniform sampler2D uChrome;
// 0 when no snapshot is bound, which is the state on the first frame and whenever the couch UI is
// not the thing being drawn. Doubles as a fade so the chrome can be dialled back independently.
uniform float uChromeAmount;
// The scene target is bucketed and may be allocated larger than the frame actually drawn into it,
// so the sampled rectangle is a sub-region rather than the whole texture. The old resolve passed
// explicit pixel rects to glBlitFramebuffer and never had to care; sampling does.
uniform vec2 uSceneUvScale;
uniform vec2 uOutputSize;

// Tube background, already blended with the accent wash on the CPU. Going full-bleed puts this pass
// underneath the whole couch screen, so the backdrop the Avalonia Borders used to paint has to be
// curved and vignetted with everything else — which means it has to be drawn in here.
uniform vec3 uBackdrop;

uniform float uCurvature;
uniform float uOverscan;
// Overscan applied to the captured couch UI, as a multiple of the same fit. Separate from the
// scene's because the two layers need different answers: the scene has an opaque backdrop whose
// warped corners leave black wedges, while the chrome is transparent out there and contributes
// none. Zooming the chrome equally would simply push the platform rail off the top of the panel.
uniform float uChromeOverscan;
uniform float uScanlineDepth;
uniform float uMaskStrength;
uniform float uMaskPitch;
uniform float uVignette;
uniform float uBloom;
uniform float uVirtualLines;
uniform float uIntensity;

// Seconds since the scene began animating, wrapped on the CPU so this never grows large enough for
// float precision to make the motion stutter.
uniform float uTime;
uniform float uRollSpeed;
uniform float uHumBar;
uniform float uHumSpeed;
uniform float uChromaBleed;
uniform float uJitter;
uniform float uFlicker;

// Which fault the tube is having, how hard, and one stable random value it can be varied by.
//
// Decided on the CPU, in CrtFaultSchedule, rather than hashed out of uTime here. A schedule derived
// from fract(sin(...)) inside a fragment shader cannot be inspected, unit tested or predicted from
// outside — GLSL's sine agrees with a host language's to only a few digits, and the hash multiplies
// that difference by forty thousand, which is enough to move a fault's onset by half its duration.
// The first version did it that way and four of the eight faults were signed off on frames in which
// they were not actually happening.
uniform float uFaultKind;
uniform float uFaultAmount;
uniform float uFaultSeed;

const float kGamma = 2.2;
const float kTau = 6.2831853;

// Folds its argument down before taking a sine of it.
//
// The plain `fract(sin(x * 127.1) * 43758.5)` is fine for small numbers and quietly wrong for the
// ones this shader feeds it. Both callers key on something that counts up — a line index times a
// tick, a fault window index — and a few minutes after the tube is switched on those arguments are
// in the millions. A 32-bit sine of a million-radian angle has almost no mantissa left, so the hash
// stops varying and the horizontal jitter locks solid, on some drivers and not others.
float hash11(float x)
{
    return fract(sin(fract(x * 0.1031) * 43.7585) * 43758.5453123);
}

float hash21(vec2 v)
{
    vec2 folded = fract(v * vec2(0.1031, 0.0973));
    return fract(sin(dot(folded, vec2(43.7585, 71.3211))) * 43758.5453123);
}

// Barrel distortion. Each axis is bent by the square of the *other* axis, which is what makes the
// corners pull in harder than the edges the way real glass does; bending each axis by its own
// coordinate gives a uniform bulge that reads as a fisheye photo instead of a television.
vec2 warp(vec2 uv, float amount)
{
    vec2 centred = (uv * 2.0) - 1.0;
    vec2 squared = centred.yx * centred.yx;
    centred += centred * squared * amount;
    return (centred * 0.5) + 0.5;
}

// Smooth RGB triad rather than a hard three-pixel repeat. The hard version is sharper on a display
// whose pixels line up with the pitch and a shimmering mess on every other one, and EmuShelf has no
// control over either the window size or the scale factor it is handed.
vec3 apertureMask(float x, float pitch, float strength)
{
    const vec3 phase = vec3(0.0, 2.0943951, 4.1887902);
    vec3 triad = 0.5 + (0.5 * cos(phase + ((x / max(pitch, 1.0)) * kTau)));
    return mix(vec3(1.0), triad, clamp(strength, 0.0, 1.0));
}

// Beam profile across one scan line. A cosine would light the gaps as much as the lines; the
// exponent pulls the energy into the centre of the trace and leaves black between traces.
float scanline(float uvY, float lines, float depth)
{
    float position = fract(uvY * lines);
    float distanceFromCentre = abs(position - 0.5) * 2.0;
    float beam = pow(1.0 - (distanceFromCentre * distanceFromCentre), 1.6);
    return mix(1.0, beam, clamp(depth, 0.0, 1.0));
}

// The scene target holds premultiplied alpha: shells write opaque, contact shadows blend with
// (One, OneMinusSrcAlpha), and everything the shelf did not cover is left at zero. So the composite
// over the backdrop is an add, not a mix — a mix would double-darken the shadows.
//
// The backdrop stays sRGB-encoded here because the scene texture it is composited against is: the
// PBR shader encodes on the way out, since it targets a plain RGBA8 surface. Compositing in that
// space is also what the resolve blit and Avalonia's own compositor did before, so the shelf keeps
// the backdrop it had rather than gaining a subtly different one.
vec3 sceneOver(vec4 uvs, vec3 backdrop)
{
    vec4 scene = texture(uScene, uvs.xy * uSceneUvScale);
    vec3 image = scene.rgb + (backdrop * (1.0 - scene.a));

    // The couch chrome — platform rail, focused title — arrives as a snapshot of the Avalonia visual
    // tree rather than being composited over this pass's output. That is the whole point: anything
    // laid on top afterwards stays flat on a curved picture, so the rail has to be part of the image
    // the tube is displaying before a single UV is bent.
    //
    // .bgra because Avalonia hands over premultiplied Bgra8888 and GL_BGRA is not portable to the
    // ES 3.0 dialect ANGLE gives us on Windows. Swizzling on read costs nothing and needs no second
    // upload path. Premultiplied is also why this is an add rather than a mix.
    if (uChromeAmount > 0.0)
    {
        // V is flipped because the two sides disagree about where a texture starts: Avalonia hands
        // over rows top-first, and glTexImage2D reads them bottom-first. Without this the rail
        // arrives at the bottom of the tube with its labels mirrored.
        vec4 chrome = texture(uChrome, vec2(uvs.z, 1.0 - uvs.w)).bgra;
        chrome *= uChromeAmount;
        image = chrome.rgb + (image * (1.0 - chrome.a));
    }

    return image;
}

// Linear-light sample of the finished image at one point on the tube.
vec3 tubeSample(vec4 uvs, vec3 backdrop)
{
    return pow(sceneOver(uvs, backdrop), vec3(kGamma));
}

// Chroma separation. A composite signal carries colour on a subcarrier that arrives fractionally
// later than luma, so the three channels land at slightly different places along the line — which
// is why the artefact is horizontal only, and why it belongs on the *sampling*, not on the finished
// pixel. The wobble along y keeps it from reading as a static misregistration.
vec3 chromaSample(vec4 uvs, vec3 backdrop, float bleed)
{
    if (bleed <= 0.0)
    {
        return tubeSample(uvs, backdrop);
    }

    float drift = bleed * (1.0 + (0.5 * sin((uTime * 2.3) + (uvs.y * 27.0))));
    vec4 offset = vec4(drift, 0.0, drift, 0.0);
    return vec3(
        tubeSample(uvs + offset, backdrop).r,
        tubeSample(uvs, backdrop).g,
        tubeSample(uvs - offset, backdrop).b);
}

void main()
{
    vec2 uv = (vNdc * 0.5) + 0.5;

    // Everything after the composite is light being attenuated by a beam profile and a mask, which
    // only behaves if it happens in linear light — multiplying an encoded value darkens the
    // midtones far more than the phosphor physically would.
    vec3 backdrop = max(uBackdrop, vec3(0.0));

    // The couch stage ramp: lit at the top, settling darker toward the bottom, matching the grid
    // layout's gradient backdrop. Scaling the RESOLVED backdrop (library colour + accent wash)
    // keeps it recolouring with the theme and focused platform exactly as the flat fill did; a
    // zero backdrop (tube presentations that composite over their own paint) stays zero.
    backdrop *= mix(0.62, 1.35, uv.y);   // GL uv.y runs bottom-up: 0.62 at the floor, lit at the top

    // The untouched image is only needed to lerp against, so at full intensity it is two texture
    // fetches per pixel that are multiplied by zero. This pass runs at native resolution over the
    // whole window, and full intensity is the shipped default, so skipping it is the single cheapest
    // saving available here.
    bool blendsAgainstFlat = uIntensity < 0.999;
    vec3 untouched = blendsAgainstFlat ? tubeSample(vec4(uv, uv), backdrop) : vec3(0.0);

    // Occasional faults.
    //
    // Everything else here is a finish the tube wears all the time. These are the eight things a set
    // that has been on for twenty years does now and then and then stops doing. Scheduled rather
    // than continuous, because that distinction is the whole effect — a fault visible at any moment
    // is a broken television, and one that arrives now and then and clears is a working one with
    // some miles on it.
    float fault = uFaultAmount;
    float tearing = (abs(uFaultKind - 1.0) < 0.5) ? fault : 0.0;
    float rollKick = (abs(uFaultKind - 2.0) < 0.5) ? fault : 0.0;
    float degauss = (abs(uFaultKind - 3.0) < 0.5) ? fault : 0.0;
    float dropout = (abs(uFaultKind - 4.0) < 0.5) ? fault : 0.0;
    float waving = (abs(uFaultKind - 5.0) < 0.5) ? fault : 0.0;
    float rainbow = (abs(uFaultKind - 6.0) < 0.5) ? fault : 0.0;
    float bandTear = (abs(uFaultKind - 7.0) < 0.5) ? fault : 0.0;
    float surge = (abs(uFaultKind - 8.0) < 0.5) ? fault : 0.0;

    vec2 tubeUv = warp(uv, uCurvature);

    // Overscan, exactly as a real set did it: zoom until the curved edges fall off the panel rather
    // than leaving black wedges inside it. The warp stretches a corner by (1 + curvature), so
    // dividing by that same factor lands the corners precisely on the edge — which is why this
    // tracks uCurvature instead of being an independent number somebody has to re-tune every time
    // the curve changes. uOverscan is the margin on top of that fit: 1.0 is a mathematically exact
    // fit, slightly more hides the filtering and jitter at the very edge, and 0 gives the border back.
    vec2 chromeUv = ((tubeUv - 0.5) / (1.0 + (uCurvature * uChromeOverscan))) + 0.5;
    tubeUv = ((tubeUv - 0.5) / (1.0 + (uCurvature * uOverscan))) + 0.5;

    // Vertical hold kicking and re-settling, with the retrace bar it drags behind it. The bar is
    // what makes a slip read as a slip: a picture that merely slides is a scroll, which looks like a
    // bug rather than like a television losing lock.
    float retrace = 0.0;
    float rollOffset = rollKick * 0.35;
    if (rollOffset > 0.0)
    {
        tubeUv.y = fract(tubeUv.y + rollOffset);
        chromeUv.y = fract(chromeUv.y + rollOffset);
        retrace = smoothstep(0.035, 0.0, min(tubeUv.y, 1.0 - tubeUv.y));
    }

    // Horizontal instability, quantised to whole scan lines and to a rate well under the frame
    // rate. Per-pixel noise would be video snow; a tube with a marginal horizontal lock shifts an
    // entire line at once, and only every few frames. A tearing fault is the same mechanism with
    // the amplitude of a lock that has actually let go rather than one that is merely marginal.
    float jitter = uJitter + (tearing * 2.5);
    if (jitter > 0.0)
    {
        float line = floor(tubeUv.y * uVirtualLines);
        float tick = floor(uTime * 24.0);
        // Flagging: the top of the picture bends worst, because it is drawn before the gain control
        // has settled after the vertical interval. Only worth applying while a fault is running —
        // the steady-state jitter is far too small for the shape to be visible.
        float flagging = 1.0 + (tearing * 1.4 * smoothstep(0.55, 1.0, tubeUv.y));
        float shift = (hash11(line + (tick * 17.0)) - 0.5) * jitter * flagging * 0.01;
        tubeUv.x += shift;
        chromeUv.x += shift;
    }

    // Interference bending the raster. Unlike the jitter above this is smooth and correlated down
    // the picture rather than random per line, which is the whole difference between a lock that is
    // slipping and something beating against the deflection — one sizzles, the other undulates.
    if (waving > 0.0)
    {
        float bend = waving * 0.030
            * sin((tubeUv.y * 18.0) - (uTime * 5.0))
            * (0.6 + (0.4 * sin((tubeUv.y * 5.0) + (uTime * 1.7))));
        tubeUv.x += bend;
        chromeUv.x += bend;
    }

    // A tape's head switch: the last few lines of a field are read by a head that has already begun
    // to leave the track, so a band near the bottom is displaced hard while everything above it is
    // untouched. The band walks a little between faults so it never looks like a fixed defect.
    if (bandTear > 0.0)
    {
        // Anywhere in the lower two thirds — a head switch lands near the bottom of a field, but
        // pinning it to one line makes it a fixed defect rather than a fault. Wide enough to cross
        // whatever the picture is showing: at a twentieth of the screen the band fell in the empty
        // space above the shelf and could not be seen at all.
        float centre = 0.10 + (0.5 * uFaultSeed);
        float inBand = smoothstep(0.07, 0.0, abs(tubeUv.y - centre));
        float lean = smoothstep(centre + 0.07, centre - 0.07, tubeUv.y);
        float shove = bandTear * inBand * (0.07 + (0.05 * lean));
        tubeUv.x += shove;
        chromeUv.x += shove;
    }

    // Wrap whatever the faults displaced back into the picture.
    //
    // The overscan margin is under two percent, and a band tear shoves the line by twelve — so
    // without this the displaced strip ran off the side, met the out-of-glass branch below, and came
    // back as a hard black wedge: a hole punched in the picture rather than a torn line. A real tear
    // shows whatever the line ran into, and wrapping is both what that looks like and free.
    //
    // Guarded so the steady state is untouched. Ordinary jitter is a third of a pixel and never
    // reaches an edge, and this pass has to stay byte-identical when nothing is going wrong.
    if ((tearing + waving + bandTear) > 0.0)
    {
        tubeUv.x = fract(tubeUv.x);
        chromeUv.x = fract(chromeUv.x);
    }

    // Outside the glass. Only reachable when uOverscan is dialled below a full fit, since overscan
    // pushes this region off the panel entirely. Kept as a hard edge rather than a soft one: this is
    // the boundary of the tube, and feathering it makes the screen look like a blurred texture
    // rather than an object.
    if (uCurvature > 0.0
        && (tubeUv.x < 0.0 || tubeUv.x > 1.0 || tubeUv.y < 0.0 || tubeUv.y > 1.0))
    {
        vec3 surround = mix(sceneOver(vec4(uv, uv), backdrop), vec3(0.0), uIntensity);
        fragColor = vec4(surround, 1.0);
        return;
    }

    // A degauss fault rings the convergence: the mask is briefly magnetised, the three beams stop
    // landing on top of each other, and the error swings back and forth as it decays. Riding it on
    // the chroma separation reuses the machinery that already splits the channels apart.
    float bleed = uChromaBleed
        + (degauss * 16.0 * sin((tubeUv.y * 21.0) + (uTime * 43.0)))
        + (dropout * 5.0);

    vec4 uvs = vec4(tubeUv, chromeUv);
    vec3 colour = chromaSample(uvs, backdrop, bleed / uOutputSize.x);

    // Halation: the glow a bright phosphor throws into its neighbours, sampled as a cheap cross. It
    // runs before the scanlines so the glow spills across the gaps between traces, which is where it
    // is actually visible — applied afterwards it would just be a blurrier scanline.
    if (uBloom > 0.0)
    {
        vec2 texel = 2.0 / uOutputSize;
        vec4 across = vec4(texel.x, 0.0, texel.x, 0.0);
        vec4 down = vec4(0.0, texel.y, 0.0, texel.y);
        vec3 glow = tubeSample(uvs + across, backdrop)
            + tubeSample(uvs - across, backdrop)
            + tubeSample(uvs + down, backdrop)
            + tubeSample(uvs - down, backdrop);
        colour += glow * 0.25 * uBloom;
    }

    // The beam drifts rather than sitting still. A perfectly stationary scanline grid reads as a
    // texture laid over the picture; a slow roll reads as a raster being painted.
    colour *= scanline(tubeUv.y + (uTime * uRollSpeed), uVirtualLines, uScanlineDepth);
    colour *= apertureMask(gl_FragCoord.x, uMaskPitch, uMaskStrength);

    // Hum bar: the wide, soft brightness band that crawls up a tube whose power supply is beating
    // against the field rate. Deliberately slow and deliberately gentle — this is the one effect
    // that says "switched on" from across a room, and the one that becomes unbearable if overdone.
    if (uHumBar > 0.0)
    {
        float phase = fract(tubeUv.y - (uTime * uHumSpeed));
        float band = 0.5 + (0.5 * cos(phase * kTau));
        colour *= 1.0 + (uHumBar * (band - 0.5));
    }

    // Mains flutter on the beam current. Two incommensurate rates so it never settles into a
    // pulse the eye can predict and start reading as a blink.
    if (uFlicker > 0.0)
    {
        float flutter = (sin(uTime * 47.0) * 0.6) + (sin(uTime * 71.3) * 0.4);
        colour *= 1.0 + (uFlicker * 0.05 * flutter);
    }

    // A degauss fault rings the beam current along with the convergence, and the retrace bar of a
    // vertical slip is simply the blanking interval arriving somewhere it is normally never seen.
    if (degauss > 0.0)
    {
        colour *= 1.0 + (degauss * 0.55 * sin((tubeUv.y * 27.0) + (uTime * 31.0)));
    }

    colour *= 1.0 - (0.92 * retrace);

    // Cross-colour. A composite decoder cannot tell fine luma detail from the colour subcarrier, so
    // detail near the subcarrier frequency comes back as colour that was never in the picture — the
    // rainbow that crawls over a striped shirt or a dithered sky. Keyed on luminance so it lands on
    // the lit parts of the picture and leaves the black alone, and swept along x at roughly the
    // subcarrier's own rate so it bands rather than tints.
    if (rainbow > 0.0)
    {
        float lit = dot(colour, vec3(0.299, 0.587, 0.114));
        float phase = (tubeUv.x * uOutputSize.x * 0.85)
            + (tubeUv.y * 34.0)
            + (uTime * 7.0);
        vec3 swing = vec3(cos(phase), cos(phase + 2.0943951), cos(phase + 4.1887902));
        colour += rainbow * 1.05 * lit * swing;
    }

    // Beam current surging and settling: the whole picture blooms, washes toward white and comes
    // back. This is the one fault that reads from the far side of a room, so it is also the one
    // that has to be brief.
    if (surge > 0.0)
    {
        colour = mix(colour, vec3(dot(colour, vec3(0.333))), surge * 0.5);
        colour *= 1.0 + (surge * 1.5);
    }

    // Snow, while the signal is away. Gated on brightness because noise rides the signal: it is loud
    // in the greys and nearly invisible in the blacks, which is the opposite of what an added grain
    // layer does. Only ever present during a dropout — a tube fed a good signal is not noisy, and a
    // permanent grain layer is the fastest way to make one look like a video filter.
    if (dropout > 0.0)
    {
        float grain = hash21(floor(gl_FragCoord.xy / 2.0) + vec2(floor(uTime * 60.0) * 1.7, 0.0));
        float gate = (0.6 * sqrt(clamp(max(colour.r, max(colour.g, colour.b)), 0.0, 1.0))) + 0.4;
        colour *= 1.0 - (dropout * 0.95 * gate * (0.5 - grain));

        // Colour goes first. The burst that tells the decoder what phase to expect is the weakest
        // thing on the line, so a signal on its way out loses its colour a moment before it loses
        // its picture — which is why a dropout flashes monochrome rather than simply going noisy.
        colour = mix(colour, vec3(dot(colour, vec3(0.299, 0.587, 0.114))), dropout * 0.8);
    }

    // Scanlines and mask are both multiplies below one, so a tube with either turned up is dimmer
    // than the image that went into it. Real sets answer that with beam current; this answers it by
    // giving back roughly the average the two profiles removed, which keeps the shelf's exposure
    // where the lighting rig put it instead of making CRT mode a brightness slider.
    float scanLoss = 1.0 - (clamp(uScanlineDepth, 0.0, 1.0) * 0.38);
    float maskLoss = 1.0 - (clamp(uMaskStrength, 0.0, 1.0) * 0.5);
    colour /= max(scanLoss * maskLoss, 0.15);

    if (uVignette > 0.0)
    {
        vec2 v = tubeUv * (1.0 - tubeUv.yx);
        float falloff = pow(clamp(v.x * v.y * 16.0, 0.0, 1.0), uVignette);
        colour *= falloff;
    }

    // One lerp against the untouched image is what makes uIntensity a real master control: at 0
    // this pass is an exact, if expensive, copy of the resolve it replaced.
    if (blendsAgainstFlat)
    {
        colour = mix(untouched, colour, clamp(uIntensity, 0.0, 1.0));
    }

    fragColor = vec4(pow(max(colour, vec3(0.0)), vec3(1.0 / kGamma)), 1.0);
}
