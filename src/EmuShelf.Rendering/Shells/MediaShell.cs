using System.Numerics;

namespace EmuShelf.Rendering.Shells;

/// <summary>The physical media EmuShelf can render in its shared 3D shelf scene.</summary>
/// <remarks>
/// One entry per authored geometry family, not per console. Profiles can apply different measured
/// dimensions and material variants to one temporary geometry family — for example the shorter
/// PS3 Blu-ray case versus a DVD-height PS2 case. The console-to-medium table lives in the app
/// layer, at <c>EmuShelf.App.Rendering.MediaShellMap</c>.
/// </remarks>
public enum MediaShell
{
    /// <summary>A thin cover-art card used when a system has no authored physical medium yet.</summary>
    CoverCard,

    /// <summary>The SNES/Super Famicom ROM cartridge.</summary>
    SnesCartridge,

    /// <summary>The Game Boy Advance ROM cartridge.</summary>
    GbaCartridge,

    /// <summary>The Game Boy/Game Boy Color ROM cartridge.</summary>
    GbcCartridge,

    /// <summary>The CD jewel case PS1 and Dreamcast games shipped in.</summary>
    JewelCase,

    /// <summary>The NES/Famicom ROM cartridge.</summary>
    NesCartridge,

    /// <summary>The Mega Drive/Genesis ROM cartridge.</summary>
    MegaDriveCartridge,

    /// <summary>The Nintendo DS game card.</summary>
    DsCard,

    /// <summary>The Nintendo 3DS game card.</summary>
    /// <remarks>
    /// Its own geometry rather than a profile over <see cref="DsCard"/>, even though the two cards
    /// share a footprint to within a millimetre. What tells them apart is moulded rather than
    /// measured: a 3DS card carries the tab on its upper right edge that stops it entering a DS.
    /// A profile can express a millimetre of height; it cannot grow a tab.
    /// </remarks>
    Nintendo3dsCard,

    /// <summary>Temporary keep-case geometry shared by PS2, GameCube, Wii and PSP profiles.</summary>
    DiscKeepCase,

    /// <summary>The shorter Blu-ray keep case a PS3 game shipped in.</summary>
    /// <remarks>
    /// Separate geometry rather than a profile over <see cref="DiscKeepCase"/>, because the two
    /// cases are different objects: 135 x 171.5 x 13mm against a DVD case's 135 x 190 x 14mm. Sharing
    /// the DVD mesh is what forced PS3 to render 19mm too tall for a year — the scene scales each
    /// axis independently, so its truthful height came out as a 13.7% stretch rather than a shorter
    /// case, and the honest option with one mesh was to keep the wrong height. With the real
    /// geometry both are simply right. See DECISIONS 2026-08-15.
    /// </remarks>
    BluRayCase,

    /// <summary>
    /// An arcade cabinet, cut off below its control panel so it stands as a bartop machine.
    /// </summary>
    /// <remarks>
    /// The odd one out of the shells: arcade games have no physical medium a player ever held, so
    /// the machine itself is the medium, and its artwork slot is the screen rather than a label.
    /// </remarks>
    ArcadeCabinet,
    /// The optical disc a keep case holds, drawn only while a disc-based game is launching.
    /// </summary>
    /// <remarks>
    /// Generated rather than sourced. A disc is an annulus and a shader finish, so authoring it as
    /// geometry would buy nothing and would bring a licence with it; one profile's measured
    /// diameter then covers both a 120mm DVD and a GameCube's 80mm mini-disc from the same mesh.
    /// </remarks>
    GameDisc,
}

[Flags]
public enum PhysicalArtworkSlots
{
    None = 0,
    Front = 1 << 0,
    Back = 1 << 1,
    Spine = 1 << 2,
    CartridgeSupport = 1 << 3,

    /// <summary>The printed face of the disc inside the case, not the case itself.</summary>
    DiscLabel = 1 << 4,
}

/// <summary>
/// Where a game's scraped artwork is pasted onto a shell, as a rectangle on one of its faces.
/// </summary>
/// <remarks>
/// Deliberately expressed against the model's own bounds rather than its UV atlas. The production
/// SNES body uses its UVs for PBR maps but receives a body-attached decal, while the GBA's label is
/// packed rotated into a shared atlas. Object-space placement keeps both independent from
/// atlas layout and stays correct if a model is re-exported with different UVs.
/// </remarks>
/// <param name="Face">Which face of the shell the artwork sits on.</param>
/// <param name="MinU">Left edge, as a fraction of the face's half-width (-1 is the far edge).</param>
/// <param name="MaxU">Right edge, in the same units.</param>
/// <param name="MinV">Bottom edge, as a fraction of the shell's half-height.</param>
/// <param name="MaxV">Top edge, in the same units.</param>
/// <param name="CornerRadius">Rounded-corner radius as a fraction of the panel's shorter edge.</param>
/// <param name="CutCorner">Diagonal bite taken out of the panel's bottom-left corner, as a fraction
/// of its shorter edge. A DS label is cut there so it clears the card's thumb notch, and squaring
/// that corner is one of the details that stops a card reading as a DS card.</param>
/// <param name="ArtFit">How artwork whose shape does not match this panel is fitted to it. Per
/// panel rather than per shell because one shell's panels are not one shape: a keep case's front
/// really is the same proportions as the box scan, while its spine is a 14mm strip beside a 135mm
/// sleeve, and stretching a scan onto both cannot be right for both.</param>
/// <param name="MaxSurfaceDepth">Overrides <see cref="MediaShellDefinition.PanelDepthFraction"/>
/// for this one panel, in canonical object units — the shell is one unit tall, so 0.0053 is a
/// millimetre on a 190mm case. Null takes the shell's figure, which is the right default: expressed
/// against the shell's own thickness it keeps a label off the interior of any cartridge without
/// being retuned. An override is for the panel that needs a bound far tighter than "not the far
/// side of the shell" — a keep case's sleeve has to stop at the fillet, roughly a millimetre in,
/// where the shell's own fraction would allow five.</param>
/// <param name="TopWrap">Fraction of the printed sheet's height that folds over the shell's top
/// face rather than lying on this panel's own face. A Mega Drive label is one 75 x 68mm sticker
/// whose top 7.7mm wraps over the cartridge's top edge — that strip is the title you read on a
/// shelved cartridge, and it is why <see cref="MaxV"/> for such a label is the shell's own top edge
/// rather than a margin below it. The renderer derives the folded strip's own panel from this, so
/// the two stay one continuous print. Zero leaves the panel flat, which is every other shell.</param>
/// <param name="Material">Name of the one material this panel prints on, or null for the whole
/// shell. It does two things at once, which is why it is a single knob. Only meshes wearing that
/// material take the print, so nothing around the panel can be caught by it; and the rectangle's
/// -1..1 is measured against that material's own geometry rather than the shell's bounding box, so
/// the numbers are read off the part being printed. An arcade cabinet needs both: its screen is a
/// hand's width inside a machine that is mostly cabinet, and a rectangle over the whole front —
/// however tightly drawn — is a rectangle over the bezel, the marquee and the control panel too.
/// A named material that the shell does not carry is an error rather than a silent no-op.</param>
public readonly record struct ArtPanel(
    ArtFace Face,
    float MinU,
    float MaxU,
    float MinV,
    float MaxV,
    float CornerRadius = 0f,
    float CutCorner = 0f,
    ArtFit ArtFit = ArtFit.Stretch,
    float? MaxSurfaceDepth = null,
    float TopWrap = 0f,
    string? Material = null)
{
    /// <summary>A panel covering the whole of a face, inset by <paramref name="inset"/>.</summary>
    public static ArtPanel Full(
        ArtFace face,
        float inset = 0f,
        ArtFit fit = ArtFit.Stretch,
        float? maxSurfaceDepth = null) =>
        new(
            face, -1f + inset, 1f - inset, -1f + inset, 1f - inset,
            ArtFit: fit, MaxSurfaceDepth: maxSurfaceDepth);
}

/// <summary>How artwork whose shape does not match an <see cref="ArtPanel"/>'s is fitted to it.</summary>
/// <remarks>
/// This matters because a keep case's sleeve really is the same shape as the box scan the scraper
/// returns, while a cartridge label is landscape and the scan is portrait. Stretching the latter
/// onto the former is the difference between a cartridge and a squashed poster.
/// </remarks>
public enum ArtFit
{
    /// <summary>Fill the panel exactly, distorting if the shapes disagree.</summary>
    Stretch,

    /// <summary>Fill the panel, cropping whatever overflows.</summary>
    Cover,

    /// <summary>Fit inside the panel, leaving the tint visible around it.</summary>
    Contain,
}

/// <summary>Which side of a shell an <see cref="ArtPanel"/> lies on, in canonical space.</summary>
public enum ArtFace
{
    /// <summary>+Z, the face that greets the player.</summary>
    Front,

    /// <summary>-Z.</summary>
    Back,

    /// <summary>-X, the spine of a keep case.</summary>
    Spine,

    /// <summary>+Y, the edge a cartridge label folds over. Its second axis runs front to back.</summary>
    Top,
}

/// <summary>Everything the renderer needs to turn one .glb into a framed, art-bearing hero.</summary>
/// <param name="Shell">Which medium this describes.</param>
/// <param name="ResourceName">Embedded resource name of the .glb inside this assembly.</param>
/// <param name="Orientation">Rotates the authored model into canonical space (Y up, +Z front).
/// The three models were authored lying in three different directions, so each carries its own.</param>
/// <param name="MaxTextureSize">Longest edge the model's own maps are downsampled to at load.</param>
/// <param name="CoverPanel">Where the game's cover art is projected.</param>
/// <param name="ExtraPanels">Further panels — a case's back and spine — painted with the accent
/// tint until the scraper supplies real art for them.</param>
/// <param name="PanelRoughness">Roughness the printed panels take, overriding the shell's own map.
/// A paper sleeve behind a keep case's clear overlay is far glossier than a cartridge's printed
/// label, and that difference is most of what distinguishes the two materials on screen.</param>
/// <param name="FlattenPanelNormal">True where printed art should hide the moulding under it.</param>
/// <param name="PanelDepthFraction">How far behind a panel's plane a surface may lie and still be
/// printed, as a fraction of the shell's extent along that panel's normal. A panel is a decal on
/// the one surface that faces the player, not a projection cast through the whole shell: without
/// this, every front-facing fragment inside the rectangle is printed, including surfaces deep
/// inside the body. On the GBA that meant the label ran across the exposed board behind the
/// cartridge's pin opening, which is visible as soon as the hero is pitched toward the player.
/// The default clears the deepest authored label recess — the GBA's, at 0.30 — by a wide margin
/// and still excludes every shell's interior, which starts at 0.50.</param>
/// <param name="BodyRoughnessScale">Per-shell correction for the source model's body roughness.</param>
/// <param name="BodyAlbedoScale">Per-shell correction for the source model's body base colour,
/// applied in linear space before the printed panels are laid over it. Sibling of
/// <paramref name="BodyRoughnessScale"/>, and needed for the same reason: a downloaded asset's
/// material was tuned against its author's viewer, not against EmuShelf's studio.</param>
/// <param name="DielectricReflectance">Normal-incidence reflectance for the shell's dielectric
/// material. Most plastics are close to 0.04; a small correction can stop a scanned model from
/// reading like glossy toy plastic without changing metallic parts.</param>
/// <param name="AmbientIntensity">Per-shell strength of the surrounding image-based studio fill.</param>
/// <param name="ShadowFillOcclusion">How much geometry-cast key visibility also suppresses ambient
/// fill. Zero is a fully filled product shot; one lets a key shadow remove all ambient light.</param>
/// <param name="CavityStrength">Strength of the authored normal map's small-scale occlusion cue.</param>
/// <param name="NormalStrength">Scale applied to tangent-space normal-map X/Y before normalization.</param>
/// <param name="ClearcoatFactor">Strength of a clear lacquer over the whole shell, for a case that
/// is literally that: a jewel case is clear polystyrene over a printed insert, which is a second
/// sharper specular lobe and not a lower roughness — the coat has its own highlight while the card
/// under it stays matte. A material that carries <c>KHR_materials_clearcoat</c> of its own wins.
/// Zero on every shell with no lacquer, which costs one comparison.</param>
/// <param name="ClearcoatRoughness">Roughness of that coat. Moulded polystyrene is not optically
/// flat, so this is not zero.</param>
/// <param name="TrimBelowHeightFraction">Fraction of the authored model's height, from its own
/// bottom, cut away at load. Zero for every real medium — you do not saw the bottom off a
/// cartridge. The arcade cabinet is not a medium but a machine, and at its real 1.8m it would make
/// every cartridge beside it a speck; cut under the control panel it is a bartop machine that still
/// carries everything that says "arcade".</param>
/// <param name="Iridescence">Strength of the diffraction rainbow a pressed disc's track spiral
/// throws across its highlights. Zero on every moulded shell, which is what keeps the term off the
/// plastic: it is the one thing that makes a grey annulus read as an optical disc rather than a
/// washer, and it belongs to the medium rather than to the light.</param>
/// <param name="TakesScrapedArtwork">Whether this shell's panels may sample the game's scraped
/// artwork at all. False keeps them on the platform tint however much art the game has: a box scan
/// is a picture of the packaging, and the one medium it is never a picture of is the disc inside
/// it. Declared on the shell rather than enforced by the caller because there are two draw paths
/// into these panels, and the rule has to hold on both.</param>
/// <param name="CoverArtIndex">Which of the game's uploaded faces this shell's cover panel draws.
/// Zero for every packaging shell, which is the game's box front. A disc is the exception: it is a
/// second shell belonging to the same game, so its label cannot share slot 0 with the case's sleeve
/// and takes a face of its own.</param>
/// <param name="RequiresArtwork">Whether this shell's panels are skipped entirely when the game has
/// no artwork for them, instead of falling back to the platform tint. A case's unscraped back wants
/// the tint — it is a coloured stand-in for a picture, on a surface that is printed either way. A
/// disc's is not: its face is a mirror, and a flat opaque circle laid over the middle of it reads as
/// a sticker stuck on rather than as a label, which is exactly how it was reported. Better to be a
/// disc until there is something real to print on it.</param>
/// <param name="PanelTintLift">How far a panel with no scraped artwork lifts its platform tint
/// toward a light printed base, from zero for the raw accent to one for plain white. A case's unscraped
/// back is a coloured stand-in for a picture and wants the accent as it is; a disc's label is ink on
/// a silver substrate, and the full-strength accent there is a saturated chip that reads as a
/// sticker rather than as printing.</param>
public sealed record MediaShellDefinition(
    MediaShell Shell,
    string ResourceName,
    Matrix4x4 Orientation,
    int MaxTextureSize,
    ArtPanel CoverPanel,
    IReadOnlyList<ArtPanel> ExtraPanels,
    float PanelRoughness,
    bool FlattenPanelNormal,
    float PanelDepthFraction = 0.40f,
    float BodyRoughnessScale = 1f,
    float BodyAlbedoScale = 1f,
    float DielectricReflectance = 0.04f,
    float AmbientIntensity = 0.86f,
    float ShadowFillOcclusion = 0.30f,
    float CavityStrength = 0.12f,
    float NormalStrength = 1f,
    float ClearcoatFactor = 0f,
    float ClearcoatRoughness = 0.04f,
    float TrimBelowHeightFraction = 0f,
    float Iridescence = 0f,
    bool TakesScrapedArtwork = true,
    int CoverArtIndex = 0,
    bool RequiresArtwork = false,
    float PanelTintLift = 0f);

/// <summary>
/// A medium's real-world presentation contract for the shared shelf scene.
/// </summary>
/// <remarks>
/// Geometry is still loaded in canonical one-unit-tall space. The scene renderer scales that
/// canonical asset to these millimetre dimensions against one 190mm reference, so unlike the old
/// one-hero camera a GBA cartridge cannot grow to the same screen height as a keep case. The
/// optional correction is deliberately small and defaults to one; it is not a second arbitrary
/// per-platform cover-size system.
///
/// The dimensions here stay the measured object throughout. What reaches the scene is those
/// dimensions through <see cref="SizeCompression"/>, which is the one place any medium's presented
/// size may differ from its real one.
/// </remarks>
/// <param name="InsertionAnimationId">Which launch choreography this medium takes. Read by the app
/// layer's <c>PhysicalShelfLaunchStyle</c>: a cartridge is turned and pushed into a slot, while a
/// keep case gives up its disc and is set down. One name per motion rather than a test against the
/// shell, because the two do not line up — a PS1 jewel case and a PSP UMD share a stand-in cover
/// card today and want different motions once their own shells exist.</param>
/// <param name="DiscDiameterMillimetres">Diameter of the optical disc inside this medium, or zero
/// where there is none. Measured rather than assumed: a GameCube ships an 80mm mini-disc where
/// every other case here holds a 120mm one, and that difference is plainly visible once the disc
/// is out of the case.</param>
public sealed record PhysicalMediaProfile(
    MediaShell Shell,
    Vector3 DimensionsMillimetres,
    PhysicalArtworkSlots ArtworkSlots,
    string MaterialVariant,
    string InsertionAnimationId,
    float PresentationScale = 1f,
    float FloorClearanceInShelfUnits = 0f,
    float DiscDiameterMillimetres = 0f)
{
    public const float ReferenceHeightMillimetres = 190f;

    /// <summary>
    /// How much of a medium's real size difference from the 190mm reference survives into the
    /// scene, as the exponent of a power law: one is literal metric scale, zero would stand every
    /// medium at the reference height.
    /// </summary>
    /// <remarks>
    /// The single rule that decides relative size on the shelf, and it exists because literal metric
    /// scale — which is what shipped — spans 14.6 to 1 from a 32.9mm Game Pak to the 480mm arcade
    /// cabinet. One camera has to hold all of them at once on the all-games view, and it frames the
    /// largest, so a mixed row with one arcade game in it drew a Game Pak at 4.3% of the frame's
    /// height against the cabinet's 64%: a smear a few pixels tall with no cover art legible on it.
    /// Truthful, and unusable.
    ///
    /// A power law rather than a blend toward the reference because it is scale-free: two media
    /// keep a fixed ratio to each other however they are compressed — 2:1 in life is 2^k on the
    /// shelf — so the ordering, and the *feeling* of ordering, is preserved everywhere rather than
    /// only near the anchor. It is also one constant with an obvious pair of limits, which is what
    /// makes it a rule rather than a table of fudge factors: nothing here is per-platform.
    ///
    /// 0.35 sets the widest ratio in the library — arcade cabinet against Game Pak — at 2.6:1,
    /// where the real objects are 14.6:1: a 480mm cabinet is drawn at 263mm and a 32.9mm Game Pak
    /// at 103mm, both against a keep case that is exactly its own 190mm because it is the anchor.
    /// Every medium keeps its place in the order (a keep case still stands over a cartridge, a NES
    /// cartridge still over a SNES one), and on the all-games view the row now spans 17% to 45% of
    /// the frame's height where it spanned 4.3% to 64%. Lower it to flatten the shelf further;
    /// 1 restores exact metric scale and every proportion test still passes, because this is applied
    /// uniformly to all three axes — it changes how big a medium is, never what shape it is.
    ///
    /// The disc a case gives up takes the same factor as the case, which is what keeps a disc that
    /// fits inside its box still fitting inside it.
    /// </remarks>
    public const float SizeCompression = 0.35f;

    /// <summary>
    /// Optional per-profile correction applied after an asset has entered the shell's canonical
    /// Y-up/+Z-front space. It stays separate from controller rotation and defaults to identity.
    /// </summary>
    public Matrix4x4 CanonicalOrientation { get; init; } = Matrix4x4.Identity;

    /// <summary>The medium's real height as this profile presents it, before compression.</summary>
    public float PresentedHeightMillimetres => DimensionsMillimetres.Y * PresentationScale;

    /// <summary>
    /// The one conversion from this profile's millimetres to the shelf's units, compression
    /// included.
    /// </summary>
    /// <remarks>
    /// Every dimension goes through this single factor rather than each being compressed on its own,
    /// and that is the whole reason compressing sizes is safe here. The scene matches a shell's
    /// three canonical extents to the three numbers below independently, so anything that touches
    /// them unevenly does not read as a size change — it silently distorts the model, which is the
    /// failure mode two of these profiles have already shipped. One shared factor cannot.
    /// </remarks>
    private float ShelfUnitsPerMillimetre =>
        MathF.Pow(
            MathF.Max(PresentedHeightMillimetres, 0.01f) / ReferenceHeightMillimetres,
            SizeCompression - 1f)
        * PresentationScale / ReferenceHeightMillimetres;

    public float WidthInShelfUnits => DimensionsMillimetres.X * ShelfUnitsPerMillimetre;

    public float HeightInShelfUnits => DimensionsMillimetres.Y * ShelfUnitsPerMillimetre;

    public float DepthInShelfUnits => DimensionsMillimetres.Z * ShelfUnitsPerMillimetre;

    /// <summary>
    /// The widest this medium can become while turning about its up axis — its turning circle,
    /// which is what a shelf row has to reserve for it rather than its face width.
    /// </summary>
    /// <remarks>
    /// A rotated rectangle spans <c>width·|cos θ| + depth·|sin θ|</c>, whose maximum over all
    /// angles is the diagonal. For every medium made of packaging this is its width to within a
    /// percent — a keep case is 14mm deep against 135mm wide — which is why the row could reserve
    /// face width for a year without anyone noticing.
    ///
    /// An arcade cabinet is deeper than it is wide, so face width reserves barely half of what it
    /// occupies: at rest, turned the 0.18 radians every unfocused medium holds, neighbouring
    /// cabinets already overlapped, and the focused one swept straight through both of them as it
    /// turned to launch. Reserving the turning circle costs a keep case four thousandths of a unit
    /// and is the whole fix for the cabinet.
    /// </remarks>
    public float TurningWidthInShelfUnits =>
        MathF.Sqrt((WidthInShelfUnits * WidthInShelfUnits) + (DepthInShelfUnits * DepthInShelfUnits));
    /// <summary>Whether this medium has a disc the launch choreography can lift out of it.</summary>
    public bool HasDisc => DiscDiameterMillimetres > 0f;

    public float DiscDiameterInShelfUnits => DiscDiameterMillimetres * ShelfUnitsPerMillimetre;
}
