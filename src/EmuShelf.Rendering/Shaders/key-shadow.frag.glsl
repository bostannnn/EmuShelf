// Some GLES drivers require a colour output even when a framebuffer's useful attachment is depth.
// The tiny colour renderbuffer is discarded; gl_FragDepth is produced by fixed-function rasterizing.

out vec4 fragColor;

void main()
{
    fragColor = vec4(1.0);
}
