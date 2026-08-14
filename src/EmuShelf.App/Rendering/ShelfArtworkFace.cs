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
}
