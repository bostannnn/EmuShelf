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

const float kGamma = 2.2;
const float kTau = 6.2831853;

float hash11(float x)
{
    return fract(sin(x * 127.1) * 43758.5453123);
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

    // The untouched image is only needed to lerp against, so at full intensity it is two texture
    // fetches per pixel that are multiplied by zero. This pass runs at native resolution over the
    // whole window, and full intensity is the shipped default, so skipping it is the single cheapest
    // saving available here.
    bool blendsAgainstFlat = uIntensity < 0.999;
    vec3 untouched = blendsAgainstFlat ? tubeSample(vec4(uv, uv), backdrop) : vec3(0.0);

    vec2 tubeUv = warp(uv, uCurvature);

    // Overscan, exactly as a real set did it: zoom until the curved edges fall off the panel rather
    // than leaving black wedges inside it. The warp stretches a corner by (1 + curvature), so
    // dividing by that same factor lands the corners precisely on the edge — which is why this
    // tracks uCurvature instead of being an independent number somebody has to re-tune every time
    // the curve changes. uOverscan is the margin on top of that fit: 1.0 is a mathematically exact
    // fit, slightly more hides the filtering and jitter at the very edge, and 0 gives the border back.
    vec2 chromeUv = ((tubeUv - 0.5) / (1.0 + (uCurvature * uChromeOverscan))) + 0.5;
    tubeUv = ((tubeUv - 0.5) / (1.0 + (uCurvature * uOverscan))) + 0.5;

    // Horizontal instability, quantised to whole scan lines and to a rate well under the frame
    // rate. Per-pixel noise would be video snow; a tube with a marginal horizontal lock shifts an
    // entire line at once, and only every few frames.
    if (uJitter > 0.0)
    {
        float line = floor(tubeUv.y * uVirtualLines);
        float tick = floor(uTime * 24.0);
        float shift = (hash11(line + (tick * 17.0)) - 0.5) * uJitter * 0.01;
        tubeUv.x += shift;
        chromeUv.x += shift;
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

    vec4 uvs = vec4(tubeUv, chromeUv);
    vec3 colour = chromaSample(uvs, backdrop, uChromaBleed / uOutputSize.x);

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
