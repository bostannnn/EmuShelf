// A backdrop's frame blended with its previous one (backdrops.js gOut): an LCD's slow pixels (ghost: the old
// frame lingers by that fraction) or phosphor / motion trails (persist: the old frame decays, the brighter wins).

uniform sampler2D uScene;
uniform sampler2D uPrevious;
uniform float uHasPrevious;
uniform float uGhost;
uniform float uPersist;

out vec4 fragColor;

void main()
{
    ivec2 at = ivec2(gl_FragCoord.xy);
    vec3 current = texelFetch(uScene, at, 0).rgb;
    vec3 previous = texelFetch(uPrevious, at, 0).rgb;
    if (uHasPrevious > 0.5)
    {
        if (uGhost > 0.0)
            current += (previous - current) * uGhost;
        else
            current = max(current, previous * uPersist);
    }
    fragColor = vec4(current, 1.0);
}
