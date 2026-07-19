using System.Security.Cryptography;

namespace EmuShelf.Integrations.Importing;

/// <summary>Recognizes the fixed Nintendo cartridge-header logo without bundling the logo bytes.</summary>
internal static class NintendoLogoValidator
{
    public const int LogoBytes = 156;

    // SHA-256 of the canonical 156-byte GBA/DS header-logo region. Keeping only the digest avoids
    // redistributing the logo while still rejecting a fabricated header that merely carries a
    // self-consistent checksum.
    private static readonly byte[] CanonicalLogoSha256 = Convert.FromHexString(
        "08A0153CFD6B0EA54B938F7D209933FA849DA0D56F5A34C481060C9FF2FAD818");

    public static bool IsCanonical(ReadOnlySpan<byte> logo) =>
        logo.Length == LogoBytes &&
        SHA256.HashData(logo).AsSpan().SequenceEqual(CanonicalLogoSha256);
}
