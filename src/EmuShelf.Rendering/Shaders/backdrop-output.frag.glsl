// A backdrop's low-resolution frame shown on the Stage with its console's finish, in the motion lab's order
// (backdrops.js gOut / pPresent): composite blur, bit depth with ordered dither, the LCD or CRT grid, the
// interlaced field, then the bloom added on top. Premultiplied for the Stage's ONE / ONE_MINUS_SRC_ALPHA blend.

uniform sampler2D uSource;
uniform sampler2D uBloomNear;
uniform sampler2D uBloomFar;
uniform float uOpacity;
uniform float uSmooth;
uniform float uLevels;
uniform float uDither;
uniform float uGrid;
uniform float uComposite;
uniform float uInterlace;
uniform float uBloom;
// The share of the frame's width cut off, half each side, so a 16:9 frame fills a narrower screen without
// squashing (the swapped Stage on the Thor's 1240×1080 panel, M45 H5). Zero shows all of it.
uniform float uCrop;
// Big Type's wipe (M47 B6): a circle in the surface's pixels (x, y from the bottom-left, the radius) that the backdrop shows
// only inside, with a one-pixel soft edge, while w is 1. All zero (unset) shows all of it, as before.
uniform vec4 uClip;

#ifdef BACKDROP_BIGTYPE
// Big Type (M47 B13; this program is compiled with BACKDROP_BIGTYPE defined, today's look without it, from the text it always had).
// uZoom: the share of the frame shown across each axis, about its middle (a crisp frame shown at the whole scale above the Stage's);
// 1, 1 shows all of it. uRowScale: the frame's rows per row of its console's own size, so a sharp frame's interlace keeps its
// console's line pitch. uPull and uGround: the recolour (backdrop-review.html's tint, mix-blend-mode: color at the pull's opacity,
// as BackdropRecolour does); a pull of 0 leaves the colour alone.
uniform vec2 uZoom;
uniform float uRowScale;
uniform float uPull;
uniform vec3 uGround;
// The far glow's share of the bloom (0.9 for every backdrop but those that say otherwise).
uniform float uBloomWide;

// W3C Compositing and Blending Level 1: Lum, ClipColor, SetLum, and the "color" blend B(Cb, Cs) = SetLum(Cs, Lum(Cb)).
float blendLum(vec3 c) { return 0.3 * c.r + 0.59 * c.g + 0.11 * c.b; }

vec3 blendClipColor(vec3 c)
{
    float l = blendLum(c);
    float n = min(c.r, min(c.g, c.b));
    float x = max(c.r, max(c.g, c.b));
    if (n < 0.0)
        c = vec3(l) + (c - vec3(l)) * l / (l - n);
    if (x > 1.0)
        c = vec3(l) + (c - vec3(l)) * (1.0 - l) / (x - l);
    return c;
}

vec3 blendColor(vec3 backdrop, vec3 source) { return blendClipColor(source + vec3(blendLum(backdrop) - blendLum(source))); }
#endif

in vec2 vNdc;
out vec4 fragColor;

const float Bayer[16] = float[16](0.0, 8.0, 2.0, 10.0, 12.0, 4.0, 14.0, 6.0, 3.0, 11.0, 1.0, 9.0, 15.0, 7.0, 13.0, 5.0);

void main()
{
    float opacity = uOpacity;
    if (uClip.w > 0.5)
    {
        opacity *= clamp(uClip.z - distance(gl_FragCoord.xy, uClip.xy) + 0.5, 0.0, 1.0);
        if (opacity == 0.0)
        {
            fragColor = vec4(0.0);
            return;
        }
    }
    vec2 uv = vNdc * 0.5 + 0.5;
    uv.x = (uCrop * 0.5) + (uv.x * (1.0 - uCrop));
#ifdef BACKDROP_BIGTYPE
    uv = vec2(0.5) + (uv - vec2(0.5)) * uZoom;
#endif
    vec2 p = uv * uRes;
    ivec2 last = ivec2(uRes) - 1;
    ivec2 cell = clamp(ivec2(floor(p)), ivec2(0), last);
    vec3 colour;
    if (uComposite > 0.5)
    {
        vec3 left = texelFetch(uSource, ivec2(max(cell.x - 1, 0), cell.y), 0).rgb;
        vec3 middle = texelFetch(uSource, cell, 0).rgb;
        vec3 right = texelFetch(uSource, ivec2(min(cell.x + 1, last.x), cell.y), 0).rgb;
        colour = (left + 2.0 * middle + right) / 4.0;
    }
    else if (uSmooth > 0.5)
    {
        colour = texture(uSource, uv).rgb;
    }
    else
    {
        colour = texelFetch(uSource, cell, 0).rgb;
    }

    // Rows count from the top, as they did in the prototype.
    int row = last.y - cell.y;
    if (uLevels > 0.0)
    {
        float offset = uDither > 0.0 ? (Bayer[(row & 3) * 4 + (cell.x & 3)] / 16.0 - 0.5) / uLevels * uDither : 0.0;
        colour = round(clamp(colour + offset, 0.0, 1.0) * uLevels) / uLevels;
    }

    vec2 within = fract(p);
    if (uGrid > 0.5 && uGrid < 1.5)
    {
        // LCD: the right and bottom third of each pixel is the gap between cells.
        if (within.x > 2.0 / 3.0 || within.y < 1.0 / 3.0)
            colour *= vec3(0.86, 0.87, 0.88);
    }
    else if (uGrid > 1.5)
    {
        // CRT: the lower half of each row is the dark gap between scanlines.
        colour *= mix(1.0, 0.45, smoothstep(0.42, 0.58, 1.0 - within.y));
    }

#ifdef BACKDROP_BIGTYPE
    if (uInterlace > 0.0 && ((int(floor(float(row) / uRowScale)) + int(uFrame)) & 1) == 1)
#else
    if (uInterlace > 0.0 && ((row + int(uFrame)) & 1) == 1)
#endif
        colour *= 1.0 - uInterlace;

#ifdef BACKDROP_BIGTYPE
    if (uBloom > 0.0)
        colour += texture(uBloomNear, uv).rgb * uBloom + texture(uBloomFar, uv).rgb * uBloom * uBloomWide;
#else
    if (uBloom > 0.0)
        colour += texture(uBloomNear, uv).rgb * uBloom + texture(uBloomFar, uv).rgb * uBloom * 0.9;
#endif

#ifdef BACKDROP_BIGTYPE
    vec3 shown = min(colour, vec3(1.0));
    if (uPull > 0.0)
        shown = mix(shown, blendColor(shown, uGround), uPull);
    fragColor = vec4(shown * opacity, opacity);
#else
    fragColor = vec4(min(colour, vec3(1.0)) * opacity, opacity);
#endif
}
