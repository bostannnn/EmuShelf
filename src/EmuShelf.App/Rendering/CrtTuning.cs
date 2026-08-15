using System.Globalization;
using EmuShelf.Rendering;

namespace EmuShelf.App.Rendering;

/// <summary>
/// Reads the shelf's CRT presentation out of the environment.
/// </summary>
/// <remarks>
/// TEMPORARY, and the reason it exists is worth stating: these knobs belong in Settings, but every
/// one of them is judged by eye at couch distance, and a rebuild between each adjustment makes that
/// judgement take an evening instead of ten minutes. This lets the look be settled first and the
/// Settings UI be built against numbers somebody has actually looked at. Delete it once those
/// controls exist and the values live in <c>LibraryViewSettings</c>.
/// </remarks>
internal static class CrtTuning
{
    public static CrtPresentation FromEnvironment()
    {
        if (Environment.GetEnvironmentVariable("EMUSHELF_CRT") is "0" or "off" or "OFF")
        {
            return CrtPresentation.Off;
        }

        var baseline = CrtPresentation.Default;
        return baseline with
        {
            Intensity = Read("EMUSHELF_CRT_INTENSITY", baseline.Intensity),
            Curvature = Read("EMUSHELF_CRT_CURVATURE", baseline.Curvature),
            Overscan = Read("EMUSHELF_CRT_OVERSCAN", baseline.Overscan),
            ChromeOverscan = Read("EMUSHELF_CRT_CHROME_OVERSCAN", baseline.ChromeOverscan),
            ScanlineDepth = Read("EMUSHELF_CRT_SCANLINES", baseline.ScanlineDepth),
            MaskStrength = Read("EMUSHELF_CRT_MASK", baseline.MaskStrength),
            MaskPitch = Read("EMUSHELF_CRT_MASK_PITCH", baseline.MaskPitch),
            VirtualLines = Read("EMUSHELF_CRT_LINES", baseline.VirtualLines),
            Bloom = Read("EMUSHELF_CRT_BLOOM", baseline.Bloom),
            Vignette = Read("EMUSHELF_CRT_VIGNETTE", baseline.Vignette),
            RollSpeed = Read("EMUSHELF_CRT_ROLL", baseline.RollSpeed),
            HumBar = Read("EMUSHELF_CRT_HUM", baseline.HumBar),
            HumSpeed = Read("EMUSHELF_CRT_HUM_SPEED", baseline.HumSpeed),
            ChromaBleed = Read("EMUSHELF_CRT_CHROMA", baseline.ChromaBleed),
            Jitter = Read("EMUSHELF_CRT_JITTER", baseline.Jitter),
            Flicker = Read("EMUSHELF_CRT_FLICKER", baseline.Flicker),
        };
    }

    private static float Read(string name, float fallback) =>
        float.TryParse(
            Environment.GetEnvironmentVariable(name),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : fallback;
}
