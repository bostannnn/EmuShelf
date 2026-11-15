// M47 B9: typing and moving in the Big Type look (decision 2026-11-15 "M47 B9" in DECISIONS.md).

using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using EmuShelf.Rendering.Stage.BigType;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// The tokens B9 added for the Big Type look: the keyboard's places (tokens.js <c>launcher</c>: its rows, its heading and note, Search's
/// results), its keys (<c>shape.Key</c>, <c>KeyRadius</c>, <c>KeyFocusScale</c>, <c>RingRadiusKey</c>, <c>type.Key</c>,
/// <c>type.KeyboardNote</c>), the placeholder's opacity and the held tile's size. Today's look never draws them (it keeps its own
/// keyboards and move mode), so each holds one value in either look. The motion's numbers are in <see cref="BigTypeTypingMotion"/> and
/// <see cref="BigTypeMoveMotion"/>, shared with the native screens.
/// </summary>
public static partial class HandheldTokens
{
    // ===== The keyboard's places (tokens.js launcher) =====

    /// <summary>The widest row's first key and the bottom row's left edge (<c>launcher.KeyboardLeft</c>).</summary>
    public static double KeyboardLeft { get; } = 66;

    /// <summary>The first row of letters' top (<c>launcher.KeyboardTop</c>).</summary>
    public static double KeyboardTop { get; } = 424;

    /// <summary>Key to key, across and down (<c>launcher.KeyPitch</c>).</summary>
    public static double KeyPitch { get; } = 112;

    /// <summary>The widest row, in keys: every row is centred on it (<c>launcher.KeyColumns</c>).</summary>
    public static int KeyColumns { get; } = 10;

    /// <summary>A key's side (<c>shape.Key</c>).</summary>
    public static double KeySide { get; } = 100;

    /// <summary>The gap between two keys: the pitch less a key.</summary>
    public static double KeyGap => KeyPitch - KeySide;

    /// <summary>The widest row's width: <see cref="KeyColumns"/> keys and the gaps between them.</summary>
    public static double KeyRowWidth => (KeyColumns * KeyPitch) - KeyGap;

    /// <summary>Space's width under the letters (<c>launcher.KeySpaceWidth</c>).</summary>
    public static double KeySpaceWidth { get; } = 548;

    /// <summary>Delete's and Done's width (<c>launcher.KeyActionWidth</c>).</summary>
    public static double KeyActionWidth { get; } = 268;

    /// <summary>The ring round a key (<c>launcher.KeyRingOutset</c>).</summary>
    public static double KeyRingOutset { get; } = 14;

    public static Thickness KeyRingMargin => new(-KeyRingOutset);

    /// <summary>A key's corners (<c>shape.KeyRadius</c>).</summary>
    public static CornerRadius KeyRound { get; } = new(24);

    /// <summary>The ring's corners round a key (<c>shape.RingRadiusKey</c>).</summary>
    public static CornerRadius RingRadiusKey { get; } = new(34);

    /// <summary>The focused key's size (<c>shape.KeyFocusScale</c>).</summary>
    public static double KeyFocusScale { get; } = 1.08;

    public static ITransform KeyRestTransform { get; } = TransformOperations.Parse("scale(1)");
    public static ITransform KeyFocusTransform { get; } = TransformOperations.Parse(FormattableString.Invariant($"scale({KeyFocusScale})"));

    /// <summary>A key's label (<c>type.Key</c>, bold).</summary>
    public static double KeyText { get; } = 42;

    /// <summary>The keyboard's heading ("Name the folder", "Search"), at <see cref="SheetTitle"/> (<c>launcher.KeyboardHead…</c>).</summary>
    public static double KeyboardHeadLeft { get; } = 66;

    public static double KeyboardHeadTop { get; } = 44;

    public static double KeyboardHeadMaxWidth { get; } = 1120;

    public static Thickness KeyboardHeadMargin => new(KeyboardHeadLeft, KeyboardHeadTop, 0, 0);

    /// <summary>The note under the heading ("It lands on the slot you picked."; <c>launcher.KeyboardNote…</c>, <c>type.KeyboardNote</c>).</summary>
    public static double KeyboardNoteLeft { get; } = 70;

    public static double KeyboardNoteTop { get; } = 224;

    public static Thickness KeyboardNoteMargin => new(KeyboardNoteLeft, KeyboardNoteTop, 0, 0);

    public static double KeyboardNote { get; } = 36;

    public static double KeyboardNoteLineHeight { get; } = 1.3;

    public static double KeyboardNoteOpacity { get; } = 0.75;

    /// <summary>The note's line box (<c>type.KeyboardNote.lineHeight</c> of its size).</summary>
    public static double KeyboardNoteLine => KeyboardNote * KeyboardNoteLineHeight;

    /// <summary>Not the mock's: the chosen colour's check on a colour key (today's chip draws it at 40).</summary>
    public static double KeyGlyph { get; } = 40;

    /// <summary>Not the mock's: the invisible field a hardware keyboard types into, as tall as the heading's line (1.15 of its size).</summary>
    public static double KeyboardFieldHeight => SheetTitle * 1.15;

    /// <summary>Search's results: up to <see cref="ResultsMax"/> of home's tiles in a row (<c>launcher.Results…</c>).</summary>
    public static double ResultsLeft { get; } = 66;

    public static double ResultsTop { get; } = 200;

    public static double ResultsPitch { get; } = 200;

    public static int ResultsMax { get; } = 5;

    /// <summary>The typed word's placeholder on the Stage (<c>colour.PlaceholderOpacity</c>).</summary>
    public static double PlaceholderOpacity { get; } = BigTypeTypingMotion.PlaceholderOpacity;

    // ===== Pick up and put down (tokens.js shape) =====

    /// <summary>The held tile's size (<c>shape.HeldScale</c>).</summary>
    public static double HeldScale { get; } = BigTypeMoveMotion.HeldScale;

    public static ITransform HeldTransform { get; } = TransformOperations.Parse(FormattableString.Invariant($"scale({HeldScale})"));

    // ===== The keyboard's motion, for the style transitions =====

    /// <summary>A key taking the focus: its colours (<c>KeyFocus.fadeMs</c>) and its size (<c>KeyFocus.ms</c>, Land).</summary>
    public static TimeSpan KeyFocusFade { get; } = TimeSpan.FromMilliseconds(BigTypeTypingMotion.KeyFocusFadeMilliseconds);

    public static TimeSpan KeyFocusMove { get; } = TimeSpan.FromMilliseconds(BigTypeTypingMotion.KeyFocusMilliseconds);
}
