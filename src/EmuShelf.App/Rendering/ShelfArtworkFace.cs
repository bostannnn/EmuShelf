namespace EmuShelf.App.Rendering;

/// <summary>
/// Which face of a physical medium a piece of artwork belongs on.
/// </summary>
/// <remarks>
/// The values are the panel indices the renderer and the fragment shader use, so this enum is the
/// one place the app layer's idea of a face and the shader's panel array have to agree. It matches
/// the order shells declare their panels in: cover panel first, then the extras.
/// </remarks>
public enum ShelfArtworkFace
{
    Front = 0,
    Back = 1,
    Spine = 2,

    /// <summary>
    /// The printed face of the disc a case holds — ScreenScraper's support texture.
    /// </summary>
    /// <remarks>
    /// Deliberately its own face rather than reusing Front. The disc and the case are two shells
    /// belonging to one game and both are on screen at once during a launch, so the case's sleeve
    /// and the disc's label have to be uploaded and bound independently. For a cartridge system the
    /// same scraped file is the cartridge's own label and arrives on Front, which is why this is a
    /// routing question and not a second scrape.
    /// </remarks>
    DiscLabel = 3,
}
