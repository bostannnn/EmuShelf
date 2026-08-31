using Avalonia.Media;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// Couch-menu row glyphs (24x24 Material-style fills), parsed once. Kept as a named set so the
/// system menu and the game-actions sheet share one visual vocabulary.
/// </summary>
public static class GamepadOptionIcons
{
    public static readonly Geometry Launch = Geometry.Parse("M8 5v14l11-7z");
    public static readonly Geometry Achievements = Geometry.Parse(
        "M19 5h-2V3H7v2H5a2 2 0 0 0-2 2v1a5 5 0 0 0 4.34 4.95A5 5 0 0 0 11 15.9V18H8v3h8v-3h-3v-2.1a5 5 0 0 0 3.66-2.95A5 5 0 0 0 21 8V7a2 2 0 0 0-2-2zM5 8V7h2v3.82A3 3 0 0 1 5 8zm14 0a3 3 0 0 1-2 2.82V7h2z");
    public static readonly Geometry Edit = Geometry.Parse(
        "M3 17.25V21h3.75L17.81 9.94l-3.75-3.75L3 17.25zM20.71 7.04a1 1 0 0 0 0-1.41l-2.34-2.34a1 1 0 0 0-1.41 0l-1.83 1.83 3.75 3.75 1.83-1.83z");
    public static readonly Geometry Cover = Geometry.Parse(
        "M21 19V5a2 2 0 0 0-2-2H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2zM8.5 13.5l2.5 3 3.5-4.5 4.5 6H5l3.5-4.5z");
    public static readonly Geometry Scrape = Geometry.Parse(
        "M17.65 6.35A7.96 7.96 0 0 0 12 4a8 8 0 1 0 7.73 10h-2.08A6 6 0 1 1 12 6a5.9 5.9 0 0 1 4.22 1.78L13 11h7V4l-2.35 2.35z");
    public static readonly Geometry Remove = Geometry.Parse(
        "M6 19a2 2 0 0 0 2 2h8a2 2 0 0 0 2-2V7H6v12zM19 4h-3.5l-1-1h-5l-1 1H5v2h14V4z");
    public static readonly Geometry Search = Geometry.Parse(
        "M15.5 14h-.79l-.28-.27a6.5 6.5 0 1 0-.7.7l.27.28v.79l5 4.99L20.49 19l-4.99-5zm-6 0A4.5 4.5 0 1 1 14 9.5 4.49 4.49 0 0 1 9.5 14z");
    public static readonly Geometry Add = Geometry.Parse("M19 13h-6v6h-2v-6H5v-2h6V5h2v6h6z");
    public static readonly Geometry Settings = Geometry.Parse(
        "M19.14 12.94a7.5 7.5 0 0 0 0-1.88l2.03-1.58a.5.5 0 0 0 .12-.63l-1.92-3.32a.5.5 0 0 0-.6-.22l-2.39.96a7.3 7.3 0 0 0-1.63-.94l-.36-2.54a.5.5 0 0 0-.5-.42h-3.84a.5.5 0 0 0-.5.42l-.36 2.54c-.58.24-1.13.56-1.63.94l-2.39-.96a.5.5 0 0 0-.6.22L2.65 8.85a.5.5 0 0 0 .12.63l2.03 1.58a7.5 7.5 0 0 0 0 1.88l-2.03 1.58a.5.5 0 0 0-.12.63l1.92 3.32c.13.22.39.31.6.22l2.39-.96c.5.38 1.05.7 1.63.94l.36 2.54c.04.24.25.42.5.42h3.84a.5.5 0 0 0 .5-.42l.36-2.54a7.3 7.3 0 0 0 1.63-.94l2.39.96c.21.09.47 0 .6-.22l1.92-3.32a.5.5 0 0 0-.12-.63l-2.03-1.58zM12 15.5A3.5 3.5 0 1 1 15.5 12 3.5 3.5 0 0 1 12 15.5z");
    public static readonly Geometry Desktop = Geometry.Parse(
        "M21 3H3a2 2 0 0 0-2 2v10a2 2 0 0 0 2 2h7v2H8v2h8v-2h-2v-2h7a2 2 0 0 0 2-2V5a2 2 0 0 0-2-2zm0 12H3V5h18v10z");
    public static readonly Geometry Quit = Geometry.Parse(
        "M13 3h-2v10h2V3zm4.83 2.17-1.42 1.42A6.94 6.94 0 0 1 19 12a7 7 0 1 1-14 0c0-2.06.9-3.92 2.59-5.41L6.17 5.17A9 9 0 1 0 21 12a8.96 8.96 0 0 0-3.17-6.83z");
}
