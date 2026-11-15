using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Handheld;
using EmuShelf.Rendering.Stage.BigType;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

/// <summary>
/// M47 B9: typing and moving as maths that renders nothing, at fixed moments (tokens.js <c>KeysIn</c>, <c>KeysOut</c>, <c>KeyPress</c>,
/// <c>ResultsIn</c>, <c>FolderBorn</c>, <c>TypedLetterIn</c>, <c>TypedLetterOut</c>, <c>TypedResize</c>, <c>TypedNameOut</c>,
/// <c>Held</c>, <c>Swap</c>, <c>PutDown</c>), and everything in place when held still. The numbers are held to tokens.js.
/// </summary>
public class HandheldBigTypeTypingMotionTests
{
    private static string TokensJs() =>
        File.ReadAllText(Path.Combine(HandheldGroundColourTests.RepositoryRoot(), "docs", "prototypes", "handheld-big-type", "tokens.js"));

    private static double Entry(string tokens, string name, string field)
    {
        var match = Regex.Match(tokens, $@"\b{name}:\s*\{{[^}}]*\b{field}:\s*(?<value>-?[0-9.]+)");
        Assert.True(match.Success, $"tokens.js has no {name}.{field}");
        return double.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
    }

    private static double Number(string tokens, string name)
    {
        var match = Regex.Match(tokens, $@"\b{name}:\s*(?<value>-?[0-9.]+)");
        Assert.True(match.Success, $"tokens.js has no {name}");
        return double.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
    }

    [Fact]
    public void TheNumbers_AreTokensJs()
    {
        var t = TokensJs();
        (string Name, string Field, double Value)[] expected =
        [
            ("KeysIn", "ms", BigTypeTypingMotion.KeysInMilliseconds), ("KeysIn", "rise", BigTypeTypingMotion.KeysInRise),
            ("KeysIn", "rowStagger", BigTypeTypingMotion.KeysInRowStagger), ("KeysIn", "keyStagger", BigTypeTypingMotion.KeysInKeyStagger),
            ("KeysIn", "ringAfterMs", BigTypeTypingMotion.KeysInRingAfter),
            ("KeyFocus", "ms", BigTypeTypingMotion.KeyFocusMilliseconds), ("KeyFocus", "fadeMs", BigTypeTypingMotion.KeyFocusFadeMilliseconds),
            ("KeyPress", "ms", BigTypeTypingMotion.KeyPressMilliseconds), ("KeyPress", "fromScale", BigTypeTypingMotion.KeyPressFromScale),
            ("ResultsIn", "ms", BigTypeTypingMotion.ResultsInMilliseconds), ("ResultsIn", "stagger", BigTypeTypingMotion.ResultsInStagger),
            ("ResultsIn", "from", BigTypeTypingMotion.ResultsInFrom), ("ResultsIn", "fromScale", BigTypeTypingMotion.ResultsInFromScale),
            ("KeysOut", "ms", BigTypeTypingMotion.KeysOutMilliseconds), ("KeysOut", "drop", BigTypeTypingMotion.KeysOutDrop),
            ("KeysOut", "stagger", BigTypeTypingMotion.KeysOutStagger), ("KeysOut", "staggerEvery", BigTypeTypingMotion.KeysOutStaggerEvery),
            ("KeysOut", "holdMs", BigTypeTypingMotion.KeysOutHoldMilliseconds),
            ("FolderBorn", "ms", BigTypeTypingMotion.FolderBornMilliseconds), ("FolderBorn", "fromScale", BigTypeTypingMotion.FolderBornFromScale),
            ("FolderBorn", "fromTilt", BigTypeTypingMotion.FolderBornFromTilt),
            ("TypedLetterIn", "ms", BigTypeTypingMotion.TypedLetterInMilliseconds), ("TypedLetterIn", "fromEm", BigTypeTypingMotion.TypedLetterInFromEm),
            ("TypedLetterIn", "tilt", BigTypeTypingMotion.TypedLetterInTilt), ("TypedLetterIn", "stagger", BigTypeTypingMotion.TypedLetterInStagger),
            ("TypedLetterOut", "ms", BigTypeTypingMotion.TypedLetterOutMilliseconds), ("TypedLetterOut", "dropEm", BigTypeTypingMotion.TypedLetterOutDropEm),
            ("TypedLetterOut", "tilt", BigTypeTypingMotion.TypedLetterOutTilt), ("TypedResize", "ms", BigTypeTypingMotion.TypedResizeMilliseconds),
            ("TypedNameOut", "ms", BigTypeTypingMotion.TypedNameOutMilliseconds), ("TypedNameOut", "stagger", BigTypeTypingMotion.TypedNameOutStagger),
            ("TypedNameOut", "staggerTotal", BigTypeTypingMotion.TypedNameOutStaggerTotal), ("TypedNameOut", "toEm", BigTypeTypingMotion.TypedNameOutToEm),
            ("Held", "wiggleMs", BigTypeMoveMotion.HeldWiggleMilliseconds), ("Held", "degrees", BigTypeMoveMotion.HeldDegrees),
            ("Swap", "ms", BigTypeMoveMotion.SwapMilliseconds),
            ("PutDown", "ms", BigTypeMoveMotion.PutDownMilliseconds), ("PutDown", "from", BigTypeMoveMotion.PutDownFrom),
            ("PutDown", "low", BigTypeMoveMotion.PutDownLow),
        ];
        foreach (var (name, field, value) in expected)
            Assert.True(Math.Abs(Entry(t, name, field) - value) < 1e-6, $"{name}.{field}: tokens.js {Entry(t, name, field)}, code {value}");
        Assert.Equal(Number(t, "HeldScale"), BigTypeMoveMotion.HeldScale, 4);
        Assert.Equal(Number(t, "PlaceholderOpacity"), BigTypeTypingMotion.PlaceholderOpacity, 4);
        Assert.Contains("Swap:      { ms: 360, easing: 'cubic-bezier(.2, 1.3, .35, 1)' }", t, StringComparison.Ordinal);
        foreach (var (name, value) in new (string, double)[]
        {
            ("KeyboardLeft", HandheldTokens.KeyboardLeft), ("KeyboardTop", HandheldTokens.KeyboardTop), ("KeyPitch", HandheldTokens.KeyPitch),
            ("KeyColumns", HandheldTokens.KeyColumns), ("KeySpaceWidth", HandheldTokens.KeySpaceWidth), ("KeyActionWidth", HandheldTokens.KeyActionWidth),
            ("KeyRingOutset", HandheldTokens.KeyRingOutset), ("KeyboardHeadLeft", HandheldTokens.KeyboardHeadLeft),
            ("KeyboardHeadTop", HandheldTokens.KeyboardHeadTop), ("KeyboardHeadMaxWidth", HandheldTokens.KeyboardHeadMaxWidth),
            ("KeyboardNoteLeft", HandheldTokens.KeyboardNoteLeft), ("KeyboardNoteTop", HandheldTokens.KeyboardNoteTop),
            ("ResultsLeft", HandheldTokens.ResultsLeft), ("ResultsTop", HandheldTokens.ResultsTop), ("ResultsPitch", HandheldTokens.ResultsPitch),
            ("ResultsMax", HandheldTokens.ResultsMax), ("RingRadiusKey", HandheldTokens.RingRadiusKey.TopLeft),
            ("KeyFocusScale", HandheldTokens.KeyFocusScale), ("KeyRadius", HandheldTokens.KeyRound.TopLeft),
        })
        {
            Assert.True(Number(t, name) == value, $"{name}: tokens.js {Number(t, name)}, HandheldTokens {value}");
        }
        Assert.Equal(Entry(t, "Key", "size"), HandheldTokens.KeyText);
        Assert.Equal(Entry(t, "KeyboardNote", "size"), HandheldTokens.KeyboardNote);
        Assert.Equal(Entry(t, "KeyboardNote", "lineHeight"), HandheldTokens.KeyboardNoteLineHeight);
        Assert.Equal(Entry(t, "KeyboardNote", "opacity"), HandheldTokens.KeyboardNoteOpacity);
        Assert.Contains("Key: 100, KeyRadius: 24", t, StringComparison.Ordinal);
        Assert.Equal(100, HandheldTokens.KeySide);
    }

    // ---- The keyboard ----

    [Fact]
    public void TheKeys_RiseByRowAndAlongIt_AndTheRingShowsAfter260()
    {
        // Row 2, key 3: 2 × 50 + 3 × 12 = 136 ms late; 140 px below and clear until then, in place 440 ms later.
        Assert.Equal((140f, 0f), BigTypeTypingMotion.KeyIn(2, 3, 136f));
        var (rise, opacity) = BigTypeTypingMotion.KeyIn(2, 3, 136f + 220f);
        Assert.Equal(140f * (1f - BigTypeMotion.Land(0.5f)), rise, 3);
        Assert.Equal(Math.Clamp(BigTypeMotion.Land(0.5f), 0f, 1f), opacity, 3);
        Assert.Equal((0f, 1f), BigTypeTypingMotion.KeyIn(2, 3, 136f + 440f));
        Assert.Equal(3 * 50f + 9 * 12f + 440f, BigTypeTypingMotion.KeysInTotal(3, 9));
        Assert.False(BigTypeTypingMotion.RingShows(259f));
        Assert.True(BigTypeTypingMotion.RingShows(260f));
        // A key pressed pulses from 0.95.
        Assert.Equal(0.95f, BigTypeTypingMotion.KeyPulse(0f), 4);
        Assert.Equal(1f, BigTypeTypingMotion.KeyPulse(300f));
    }

    [Fact]
    public void ClosingTheKeysDrop_EightMsApartCountedEveryTen_AndTheLayerUnderComesBackAt290()
    {
        // Key 13 is 3 steps late (13 mod 10); it has gone 170 ms after that.
        Assert.Equal((0f, 1f), BigTypeTypingMotion.KeyOut(13, 24f));
        var (drop, opacity) = BigTypeTypingMotion.KeyOut(13, 24f + 85f);
        Assert.Equal(100f * BigTypeMotion.Leave(0.5f), drop, 3);
        Assert.Equal(1f - BigTypeMotion.Leave(0.5f), opacity, 3);
        Assert.Equal((100f, 0f), BigTypeTypingMotion.KeyOut(13, 24f + 170f));
        Assert.Equal(290f, BigTypeTypingMotion.KeysOutHold());
    }

    [Fact]
    public void SearchsHits_ComeDownFromAbove_45MsApart()
    {
        Assert.Equal((-60f, 0.6f, 0f), BigTypeTypingMotion.ResultIn(2, 90f));
        Assert.Equal((0f, 1f, 1f), BigTypeTypingMotion.ResultIn(2, 90f + 420f));
        var (y, scale, _) = BigTypeTypingMotion.ResultIn(0, 210f);
        Assert.Equal(-60f * (1f - BigTypeMotion.Land(0.5f)), y, 3);
        Assert.Equal(0.6f + (0.4f * BigTypeMotion.Land(0.5f)), scale, 3);
        Assert.Equal(4 * 45f + 420f, BigTypeTypingMotion.ResultsInTotal(5));
    }

    [Fact]
    public void AFolderNamedOnTheKeyboard_IsBornFromNothing_Turned25Degrees()
    {
        Assert.Equal((0f, -25f, 0f), BigTypeTypingMotion.FolderBorn(0f));
        var (scale, degrees, _) = BigTypeTypingMotion.FolderBorn(300f);
        Assert.Equal(BigTypeMotion.Land(0.5f), scale, 3);
        Assert.Equal(-25f * (1f - BigTypeMotion.Land(0.5f)), degrees, 3);
        Assert.Equal((1f, 0f, 1f), BigTypeTypingMotion.FolderBorn(600f));
    }

    // ---- The typed word ----

    [Fact]
    public void ANewLetter_DropsInFromAbove_Tilted_30MsAfterTheOneBefore()
    {
        // The third of three new letters starts 60 ms late.
        Assert.Equal(new BigTypeLetterPose(0f, -0.55f, -8f, 0f), BigTypeTypingMotion.TypedIn(2, 60f));
        var half = BigTypeTypingMotion.TypedIn(2, 60f + 230f);
        Assert.Equal(-0.55f * (1f - BigTypeMotion.Land(0.5f)), half.Y, 4);
        Assert.Equal(-8f * (1f - BigTypeMotion.Land(0.5f)), half.Degrees, 4);
        Assert.Equal(BigTypeLetterPose.Rest, BigTypeTypingMotion.TypedIn(2, 60f + 460f));
        Assert.Equal(2 * 30f + 460f, BigTypeTypingMotion.TypedInMilliseconds(3));
    }

    [Fact]
    public void ADeletedLetter_FallsOffTheEnd_Turning14Degrees()
    {
        var half = BigTypeTypingMotion.TypedOut(130f)!.Value;
        Assert.Equal(0.6f * BigTypeMotion.Leave(0.5f), half.Y, 4);
        Assert.Equal(14f * BigTypeMotion.Leave(0.5f), half.Degrees, 4);
        Assert.Equal(1f - BigTypeMotion.Leave(0.5f), half.Opacity, 4);
        Assert.Null(BigTypeTypingMotion.TypedOut(260f));
    }

    [Fact]
    public void TheWord_EasesToItsNewSize_InCssEaseOut_OverTwoHundredMs()
    {
        Assert.Equal(300f, BigTypeTypingMotion.TypedSize(300f, 200f, 0f));
        Assert.Equal(300f - (100f * BigTypeTypingMotion.EaseOut(0.5f)), BigTypeTypingMotion.TypedSize(300f, 200f, 100f), 3);
        Assert.Equal(200f, BigTypeTypingMotion.TypedSize(300f, 200f, 200f));
        // Nothing to ease from (it was not shown): the new size at once.
        Assert.Equal(200f, BigTypeTypingMotion.TypedSize(0f, 200f, 0f));
    }

    [Fact]
    public void TheNameBeforeTyping_LeavesUpward_TenMsALetter_UpTo110()
    {
        Assert.Equal(new BigTypeLetterPose(0f, 0f, 0f, 1f), BigTypeTypingMotion.NameOut(3, 30f));
        Assert.Null(BigTypeTypingMotion.NameOut(3, 30f + 170f));
        // Letter 20 waits only 110 ms, as the mock's min(i * 10, 110).
        Assert.Null(BigTypeTypingMotion.NameOut(20, 110f + 170f));
        Assert.NotNull(BigTypeTypingMotion.NameOut(20, 110f + 169f));
        Assert.Equal(-0.6f * BigTypeMotion.Leave(0.5f), BigTypeTypingMotion.NameOut(0, 85f)!.Value.Y, 4);
        Assert.Equal(110f + 170f, BigTypeTypingMotion.NameOutMilliseconds(30));
    }

    // ---- Pick up and put down ----

    [Fact]
    public void TheHeldTile_Rocks_From35DegreesLeftToRight_420MsEachWay()
    {
        Assert.Equal(-3.5f, BigTypeMoveMotion.Wiggle(0f), 4);
        Assert.Equal(0f, BigTypeMoveMotion.Wiggle(210f), 4);
        Assert.Equal(3.5f, BigTypeMoveMotion.Wiggle(420f), 4);
        Assert.Equal(0f, BigTypeMoveMotion.Wiggle(630f), 4);
        Assert.Equal(-3.5f, BigTypeMoveMotion.Wiggle(840f), 4);
        Assert.Equal(-3.5f + (7f * BigTypeMotion.EaseInOut(0.25f)), BigTypeMoveMotion.Wiggle(105f), 4);
    }

    [Fact]
    public void ATradeOfPlaces_SlidesTheTileToItsNewSlot_In360Ms()
    {
        Assert.Equal((-212f, 0f), BigTypeMoveMotion.Swap(-212f, 0f, 0f));
        var (x, _) = BigTypeMoveMotion.Swap(-212f, 0f, 180f);
        Assert.Equal(-212f * (1f - BigTypeMoveMotion.SwapEase(0.5f)), x, 3);
        Assert.Equal((0f, 0f), BigTypeMoveMotion.Swap(-212f, 0f, 360f));
        Assert.True(BigTypeMoveMotion.Swapping(359f));
        Assert.False(BigTypeMoveMotion.Swapping(360f));
    }

    [Fact]
    public void PutDown_TheTileSquashes_112_Then092_Then1()
    {
        Assert.Equal(1.12f, BigTypeMoveMotion.PutDown(0f), 4);
        // Halfway along CSS ease-out is the low point.
        var lowAt = 0f;
        for (var ms = 0f; ms <= 380f; ms += 0.25f)
        {
            if (BigTypeTypingMotion.EaseOut(ms / 380f) >= 0.5f)
            {
                lowAt = ms;
                break;
            }
        }
        Assert.Equal(0.92f, BigTypeMoveMotion.PutDown(lowAt), 2);
        Assert.Equal(1f, BigTypeMoveMotion.PutDown(380f));
    }

    [Fact]
    public void HeldStill_EverythingIsWhereItRests()
    {
        Assert.Equal((0f, 1f), BigTypeTypingMotion.KeyIn(3, 9, 0f, holdStill: true));
        Assert.True(BigTypeTypingMotion.RingShows(0f, holdStill: true));
        Assert.Equal((100f, 0f), BigTypeTypingMotion.KeyOut(0, 0f, holdStill: true));
        Assert.Equal(0f, BigTypeTypingMotion.KeysOutHold(holdStill: true));
        Assert.Equal(1f, BigTypeTypingMotion.KeyPulse(0f, holdStill: true));
        Assert.Equal((0f, 1f, 1f), BigTypeTypingMotion.ResultIn(4, 0f, holdStill: true));
        Assert.Equal((1f, 0f, 1f), BigTypeTypingMotion.FolderBorn(0f, holdStill: true));
        Assert.Equal(BigTypeLetterPose.Rest, BigTypeTypingMotion.TypedIn(5, 0f, holdStill: true));
        Assert.Equal(0f, BigTypeTypingMotion.TypedInMilliseconds(5, holdStill: true));
        Assert.Null(BigTypeTypingMotion.TypedOut(0f, holdStill: true));
        Assert.Equal(200f, BigTypeTypingMotion.TypedSize(300f, 200f, 0f, holdStill: true));
        Assert.Null(BigTypeTypingMotion.NameOut(0, 60f, holdStill: true));
        Assert.Equal(0f, BigTypeMoveMotion.Wiggle(105f, holdStill: true));
        Assert.Equal((0f, 0f), BigTypeMoveMotion.Swap(-212f, 0f, 0f, holdStill: true));
        Assert.Equal(1f, BigTypeMoveMotion.PutDown(0f, holdStill: true));
    }
}

public partial class HandheldShellViewModelTests
{
    /// <summary>
    /// M47 B9 through the shell, under Big Type: the keys are the mock's in its places, the typing rules are its (at the app's limits),
    /// Search's hits and the pad between them and the keys, and pick up and put down (trading places, across pages, walking the steps
    /// back, Y into a folder). Switching the look is process-wide, so these never run beside another test, and put today's look back.
    /// </summary>
    [Collection(nameof(HandheldTokenLookCollection))]
    public class BigTypeTypingAndMoving
    {
        private static async Task<T> BigType<T>(Func<Task<T>> run)
        {
            HandheldTokens.Use(HandheldLook.BigType);
            try
            {
                return await run();
            }
            finally
            {
                HandheldTokens.Use(HandheldLook.Classic);
            }
        }

        private static Task BigType(Func<Task> run) => BigType(async () =>
        {
            await run();
            return 0;
        });

        private static void Type(HandheldShellViewModel shell, string text)
        {
            foreach (var ch in text)
            {
                var key = ch == ' '
                    ? shell.Naming!.KeyRows.SelectMany(row => row.Keys).Single(candidate => candidate.Kind == HandheldKeyKind.Space)
                    : shell.Naming!.KeyRows.SelectMany(row => row.Keys).First(candidate => candidate.Value == char.ToUpperInvariant(ch).ToString());
                shell.PressNamingKey(key);
            }
        }

        [Fact]
        public Task TheKeys_AreTheMocks_InItsPlaces_AndEveryCharacterTodaysKeyboardsTypeIsOnAPage() => BigType(async () =>
        {
            var library = new FakeLibrary().WithPlatform("ps2", 3);
            var store = new InMemoryHandheldHomeLayoutStore();
            store.Save(HandheldHomeLayout.FromSlots([HandheldSlot.PlatformFolder("ps2")]));
            var shell = await CreateAsync(library, store);
            FocusHome(shell, 7);
            Press(shell, GamepadAction.Confirm);
            var naming = shell.Naming!;

            // The mock's buildKeys, doubled: rows at 33 + [0, 28, 84] + i × 56, 212 + r × 56; Space, Delete and Done at 380.
            Assert.Equal(["QWERTYUIOP", "ASDFGHJKL", "ZXCVBNM#+="[..7]], naming.KeyRows.Take(3)
                .Select(row => string.Concat(row.Keys.Where(key => key.Kind == HandheldKeyKind.Character).Select(key => key.Label))));
            double[] indent = [0, 56, 168];
            for (var r = 0; r < 3; r++)
            {
                var keys = naming.KeyRows[r].Keys.Where(key => key.Kind == HandheldKeyKind.Character).ToList();
                for (var i = 0; i < keys.Count; i++)
                    Assert.Equal((66 + indent[r] + (i * 112), 424 + (r * 112), 100.0), (keys[i].X, keys[i].Y, keys[i].Width));
            }
            Assert.Equal([("Space", 66.0, 548.0), ("Delete", 626.0, 268.0), ("Done", 906.0, 268.0)],
                naming.KeyRows[3].Keys.Select(key => (key.Label, key.X, key.Width)));
            Assert.All(naming.KeyRows[3].Keys, key => Assert.Equal(760, key.Y));
            // The app's other characters: the key at the end of the third row (or RB) turns the page; no Shift for a name.
            var page = naming.KeyRows[2].Keys[^1];
            Assert.Equal((HandheldKeyKind.Symbols, "123", 1018.0, 156.0), (page.Kind, page.Label, page.X, page.Width));
            Assert.DoesNotContain(naming.KeyRows.SelectMany(row => row.Keys), key => key.Kind == HandheldKeyKind.Shift);
            var everything = string.Concat(HandheldBigTypeKeys.Pages.SelectMany(rows => rows));
            foreach (var ch in "1234567890!@#$%^&*()-_=+[]{}\\|;:'\",.<>/?")
                Assert.Contains(ch, everything);
            Press(shell, GamepadAction.NextPlatform);
            Assert.Equal("1234567890", string.Concat(naming.KeyRows[0].Keys.Select(key => key.Label)));
            Assert.True(naming.KeyRows[2].Keys[^1].IsFocused);
            Press(shell, GamepadAction.NextPlatform, 2);
            Assert.Equal("QWERTYUIOP", string.Concat(naming.KeyRows[0].Keys.Select(key => key.Label)));

            // Up and down go to the nearest key across (the mock's nearest): from Z (centre 284) up to S (centre 284), then D's row.
            naming.FocusKey(naming.KeyRows[2].Keys.First(key => key.Label == "Z"));
            Press(shell, GamepadAction.NavigateUp);
            Assert.Equal("S", naming.FocusedKey.Label);
            // From M (centre 956) up to K (centre 956), straight above it.
            naming.FocusKey(naming.KeyRows[2].Keys.First(key => key.Label == "M"));
            Press(shell, GamepadAction.NavigateUp);
            Assert.Equal("K", naming.FocusedKey.Label);
        });

        [Fact]
        public Task Naming_TypesByTheMocksRule_AtTheAppsLimit_AndBumpsWhereTheMockDoes() => BigType(async () =>
        {
            var library = new FakeLibrary().WithPlatform("ps2", 3);
            var store = new InMemoryHandheldHomeLayoutStore();
            store.Save(HandheldHomeLayout.FromSlots([HandheldSlot.PlatformFolder("ps2")]));
            var shell = await CreateAsync(library, store);
            FocusHome(shell, 7);
            var bumps = 0;
            shell.Bumped += _ => bumps++;
            Press(shell, GamepadAction.Confirm);
            var naming = shell.Naming!;

            // The Stage: the label rolls to "New folder", the placeholder at its opacity, the line counts letters; no second line.
            Assert.Equal(("New folder", "Name", "0 / 24 letters", ""), (shell.Stage.Label, shell.Stage.Name, shell.Stage.Line, shell.Stage.Facts));
            Assert.True(shell.Stage.IsTyped);
            Assert.True(shell.Stage.IsPlaceholder);
            Assert.Equal(HandheldTokens.PlaceholderOpacity, shell.Stage.NameOpacity);
            Assert.Equal(["A Type", "B Cancel", "Y Space", "X Done"], shell.Hints.Select(hint => $"{hint.Button} {hint.Label}"));
            Assert.Equal("It lands on the slot you picked.", shell.Typing!.Note);
            Assert.Equal("Name the folder", shell.Typing.Heading);

            // No space first; a capital first and after a space, lower case otherwise; never two spaces.
            Press(shell, GamepadAction.Actions);
            Assert.Equal(("", 1), (naming.Text, bumps));
            Type(shell, "racing");
            Assert.Equal("Racing", naming.Text);
            Type(shell, " ");
            Press(shell, GamepadAction.Actions);
            Assert.Equal(2, bumps);
            Type(shell, "games");
            Assert.Equal("Racing Games", naming.Text);
            Assert.Equal(("Racing Games", "12 / 24 letters", false), (shell.Stage.Name, shell.Stage.Line, shell.Stage.IsPlaceholder));
            Assert.Equal(["A Type", "B Delete", "Y Space", "X Done"], shell.Hints.Select(hint => $"{hint.Button} {hint.Label}"));
            // The app's limit (24, not the mock's 12): typing into a full name bumps.
            Type(shell, " and more stuff");
            Assert.Equal(24, naming.Text.Length);
            var before = bumps;
            Type(shell, "x");
            Assert.Equal(before + 1, bumps);

            // B deletes while there is text, then cancels; Done on an empty name bumps and makes nothing.
            while (naming.Text.Length > 0)
                Press(shell, GamepadAction.Cancel);
            Press(shell, GamepadAction.Menu);
            Assert.Same(naming, shell.Naming);
            Press(shell, GamepadAction.Cancel);
            Assert.Null(shell.Naming);
            Assert.False(shell.Stage.IsTyped);
            Assert.Empty(shell.Layout.Folders);
        });

        [Fact]
        public Task Done_MakesTheFolderOnTheSlotPicked_AndSaysWhereItIsBorn() => BigType(async () =>
        {
            var library = new FakeLibrary().WithPlatform("ps2", 3);
            var store = new InMemoryHandheldHomeLayoutStore();
            store.Save(HandheldHomeLayout.FromSlots([HandheldSlot.PlatformFolder("ps2")]));
            var shell = await CreateAsync(library, store);
            FocusHome(shell, 7);
            var born = -1;
            shell.FolderBorn += index => born = index;
            Press(shell, GamepadAction.Confirm);
            Type(shell, "racing");
            Press(shell, GamepadAction.Menu);
            Assert.Null(shell.Naming);
            Assert.Equal(7, born);
            var folder = Assert.Single(shell.Layout.Folders);
            Assert.Equal("Racing", folder.Name);
            Assert.Equal(HandheldSlot.UserFolder(folder.Id), shell.Layout.SlotAt(7));
        });

        [Fact]
        public Task Search_TypesByTheMocksRule_ItsHitsSitAboveTheKeys_AndThePadGoesBetweenThem() => BigType(async () =>
        {
            var library = new FakeLibrary().WithPlatform("ps2", 8).WithPlatform("gc", 2);
            var shell = await CreateAsync(library);
            shell.SearchDebounce = TimeSpan.Zero;
            RunMenuRow(shell, "Search");
            var search = Assert.IsType<HandheldSearchPanel>(shell.Panel);
            var keyboard = search.Keyboard!;
            Assert.True(keyboard.AutoCapital);
            Assert.DoesNotContain(keyboard.KeyRows.SelectMany(row => row.Keys), key => key.Kind == HandheldKeyKind.Shift);
            Assert.Equal(("Search", "Search", "Type part of a name"), (shell.Stage.Label, shell.Stage.Name, shell.Stage.Line));
            Assert.Equal("Search", shell.Typing!.Heading);

            // Up from the top row with no hits bumps.
            var bumps = 0;
            shell.Bumped += _ => bumps++;
            Press(shell, GamepadAction.NavigateUp);
            Assert.Equal(1, bumps);

            // Typing "game" (a capital first): ten match, five hits above the keys; the line counts them all.
            foreach (var ch in "GAME")
                shell.TapPanelKey(keyboard.KeyRows.SelectMany(row => row.Keys).First(key => key.Value == ch.ToString()));
            await search.Pending;
            Assert.Equal("Game", keyboard.Text);
            Assert.Equal(5, search.Hits.Count);
            Assert.Equal("10 games", shell.Stage.Line);
            Assert.Equal(Enumerable.Range(0, 5).Select(i => 66.0 + (i * 200)), search.Hits.Select(hit => hit.X));

            // Up from Q (centre 116) to the nearest hit (the first, centre 154): the game on the Stage, A Play, B Back.
            keyboard.FocusAt(0, 0);
            Press(shell, GamepadAction.NavigateUp);
            Assert.True(search.IsOnHits);
            Assert.Equal(0, search.HitIndex);
            Assert.Equal(search.Hits[0].Title, shell.Stage.Name);
            Assert.False(shell.Stage.IsTyped);
            Assert.Equal(["A Play", "B Back"], shell.Hints.Select(hint => $"{hint.Button} {hint.Label}"));
            Press(shell, GamepadAction.NavigateRight, 4);
            Assert.Equal(4, search.HitIndex);
            Press(shell, GamepadAction.NavigateRight);
            Assert.Equal(2, bumps);
            // Down goes to the nearest key in the top row (hit 4's centre 954 → I, centre 900, before O at 1012).
            Press(shell, GamepadAction.NavigateDown);
            Assert.False(search.IsOnHits);
            Assert.Equal("I", keyboard.FocusedKey.Label);
            Assert.True(shell.Stage.IsTyped);
            Assert.Equal("Game", shell.Stage.Name);

            // A query nothing matches: "Nothing found"; Done then leaves Search (the mock's closeKb).
            keyboard.Text = "Zzz";
            await search.Pending;
            Assert.Equal("Nothing found", shell.Stage.Line);
            Assert.Empty(search.Hits);
            Press(shell, GamepadAction.Menu);
            Assert.Null(shell.Panel);
        });

        [Fact]
        public Task Search_AOnAHit_ClosesSearch_AndPlaysTheGame() => BigType(async () =>
        {
            var library = new FakeLibrary().WithPlatform("ps2", 3);
            foreach (var entry in library.Games["ps2"])
                library.Details[entry.Id] = Details();
            library.PendingLaunch = new TaskCompletionSource<bool>();
            var shell = await CreateAsync(library);
            shell.SearchDebounce = TimeSpan.Zero;
            RunMenuRow(shell, "Search");
            var search = Assert.IsType<HandheldSearchPanel>(shell.Panel);
            search.Keyboard!.Text = "Game";
            await search.Pending;
            Press(shell, GamepadAction.NavigateUp);
            var game = search.Hits[0].Entry;
            Press(shell, GamepadAction.Confirm);
            for (var i = 0; i < 200 && shell.TitlePage is null; i++)
                await Task.Delay(5);
            Assert.Null(shell.Panel);
            Assert.Equal(game.Id, shell.TitlePage!.Entry.Id);
            Assert.False(shell.ShowsTitlePage);
        });

        [Fact]
        public Task EditTitle_KeepsItsOwnCase_WithShift_AndItsLength() => BigType(async () =>
        {
            var keyboard = new HandheldTextKeyboardViewModel(() => _text, text => _text = text, () => "Title");
            _text = string.Empty;
            Assert.Contains(keyboard.KeyRows[2].Keys, key => key.Kind == HandheldKeyKind.Shift && key.X == 66 && key.Width == 156);
            Assert.Equal("q", keyboard.KeyRows[0].Keys[0].Label);
            keyboard.Press(keyboard.KeyRows[0].Keys[0]);
            keyboard.Dispatch(GamepadAction.Actions);
            keyboard.Dispatch(GamepadAction.Actions);
            Assert.Equal("q  ", _text);
            keyboard.Dispatch(GamepadAction.PreviousPlatform);
            Assert.Equal("Q", keyboard.KeyRows[0].Keys[0].Label);
            await Task.CompletedTask;
        });

        private static string _text = string.Empty;

        // ---- Pick up and put down ----

        private static async Task<(FakeLibrary Library, InMemoryHandheldHomeLayoutStore Store, HandheldShellViewModel Shell)> HomeAsync()
        {
            var library = new FakeLibrary().WithPlatform("ps2", 3).WithPlatform("gc", 2).WithPlatform("wii", 2);
            var store = new InMemoryHandheldHomeLayoutStore();
            var pin = library.Games["ps2"][0].Id;
            var slots = new List<HandheldSlot> { HandheldSlot.PlatformFolder("ps2"), HandheldSlot.PlatformFolder("gc"), HandheldSlot.PlatformFolder("wii") };
            slots.Add(HandheldSlot.Game(pin));
            store.Save(HandheldHomeLayout.FromSlots(slots));
            var shell = await CreateAsync(library, store);
            return (library, store, shell);
        }

        private static List<HandheldSlot> Slots(HandheldShellViewModel shell) => shell.Layout.AllSlots().ToList();

        [Fact]
        public Task Moving_TradesPlaces_EachStepRemembered_AndBWalksThemBack() => BigType(async () =>
        {
            var (_, store, shell) = await HomeAsync();
            var before = Slots(shell);
            var saves = store.Saves;
            var traded = new List<IReadOnlyList<(int From, int To)>>();
            shell.TilesTraded += pairs => traded.Add(pairs);
            var putDown = -1;
            shell.TilePutDown += index => putDown = index;

            FocusHome(shell, 0);
            shell.StartMove();
            Assert.True(shell.IsMoving);
            // The mock's setFocus while held: the held tile's own Stage, "Moving" over it, what a step does under it.
            Assert.Equal(("Moving", "PS2", "It trades places with what it passes"), (shell.Stage.Label, shell.Stage.Name, shell.Stage.Line));
            Assert.Equal(["A Drop", "B Put back"], shell.Hints.Select(hint => $"{hint.Button} {hint.Label}"));

            Press(shell, GamepadAction.NavigateRight);
            Press(shell, GamepadAction.NavigateDown);
            Assert.Equal([[(0, 1), (1, 0)], [(1, 6), (6, 1)]], traded.Select(pairs => pairs.ToArray()));
            Assert.Equal(6, shell.HomeIndexOfHeld);
            Assert.True(shell.CurrentPage!.Slots[6].IsCarried);
            Assert.Equal("PS2 System", shell.CurrentPage.Slots[6].Name);
            Assert.Equal("GC System", shell.CurrentPage.Slots[0].Name);
            // Up again (to slot 1), then up from the top row bumps.
            Press(shell, GamepadAction.NavigateUp);
            var bumps = 0;
            shell.Bumped += _ => bumps++;
            Press(shell, GamepadAction.NavigateUp);
            Assert.Equal(1, bumps);
            Assert.Equal(1, shell.HomeIndexOfHeld);
            Press(shell, GamepadAction.NavigateDown);

            // B: every step walked back; home is exactly as it was, nothing saved, the focus where it started.
            Press(shell, GamepadAction.Cancel);
            Assert.False(shell.IsMoving);
            Assert.Equal(before, Slots(shell));
            Assert.Equal(saves, store.Saves);
            Assert.Equal(0, shell.HomePosition.Slot);
            Assert.Equal(0, putDown);
            // The last trade says where each tile goes back: GC from 0 to 1, PS2 from 6 to 0, the empty slot from 1 to 6.
            Assert.Equal([(0, 1), (1, 6), (6, 0)], traded[^1].OrderBy(pair => pair.From).ToArray());
        });

        [Fact]
        public Task Moving_AcrossAPage_AndAPutsItDownThere() => BigType(async () =>
        {
            var (_, store, shell) = await HomeAsync();
            FocusHome(shell, 4);
            shell.StartMove();
            var saves = store.Saves;
            // Right from the last column: page 2's first slot.
            Press(shell, GamepadAction.NavigateRight);
            Assert.Equal((1, 0), (shell.HomePosition.Page, shell.HomePosition.Slot));
            Assert.Equal(20, shell.HomeIndexOfHeld);
            var held = shell.Layout.SlotAt(4);
            Press(shell, GamepadAction.Confirm);
            Assert.False(shell.IsMoving);
            Assert.Equal(saves + 1, store.Saves);
            // It traded places with page 2's first slot (empty): it is there now, and its old slot holds what was there.
            Assert.Equal(held with { IsNew = false }, shell.Layout.SlotAt(20) with { IsNew = false });
            Assert.True(shell.Layout.SlotAt(4).IsEmpty || shell.Layout.SlotAt(4) != held);
        });

        [Fact]
        public Task Moving_APin_YPutsItInAFolder_OrANewOne() => BigType(async () =>
        {
            var (library, _, shell) = await HomeAsync();
            var pin = library.Games["ps2"][0].Id;
            FocusHome(shell, 3);
            shell.StartMove();
            Assert.Equal(["A Drop", "B Put back", "Y Put in…"], shell.Hints.Select(hint => $"{hint.Button} {hint.Label}"));
            Press(shell, GamepadAction.NavigateLeft);
            Press(shell, GamepadAction.Actions);
            var sheet = Assert.IsType<HandheldSheetViewModel>(shell.Sheet);
            Assert.Equal(["New folder"], sheet.Items.Select(item => item.Label));
            Assert.Equal("Put in", shell.Stage.Label);
            // B goes back to holding it.
            Press(shell, GamepadAction.Cancel);
            Assert.True(shell.IsMoving);
            Press(shell, GamepadAction.Actions);
            RunSheetRow(shell, "New folder");
            Assert.False(shell.IsMoving);
            var naming = Assert.IsType<HandheldNamingViewModel>(shell.Naming);
            Assert.Equal("It lands on the slot you picked.", naming.Note);
            Type(shell, "mine");
            Press(shell, GamepadAction.Menu);
            var folder = Assert.Single(shell.Layout.Folders);
            Assert.Equal(("Mine", true), (folder.Name, folder.Contains(pin)));
            // The folder is where the game was held (it traded places with the Wii folder first); the pin is gone from home.
            Assert.Equal(HandheldSlot.UserFolder(folder.Id), shell.Layout.SlotAt(2));
            Assert.Equal(HandheldSlot.PlatformFolder("wii"), shell.Layout.SlotAt(3));
            Assert.DoesNotContain(HandheldSlot.Game(pin), shell.Layout.AllSlots());

            // Another pin, into that folder of yours: its slot is left empty.
            shell.PinToHome(library.Games["gc"][0].Id);
            var at = shell.Layout.IndexOf(HandheldSlot.Game(library.Games["gc"][0].Id));
            FocusHome(shell, at);
            shell.StartMove();
            Press(shell, GamepadAction.Actions);
            RunSheetRow(shell, "Mine");
            Assert.False(shell.IsMoving);
            Assert.True(shell.Layout.SlotAt(at).IsEmpty);
            Assert.True(shell.Layout.FindFolder(folder.Id)!.Contains(library.Games["gc"][0].Id));
        });

        [Fact]
        public Task Moving_AFolder_YDoesNothing() => BigType(async () =>
        {
            var (_, _, shell) = await HomeAsync();
            FocusHome(shell, 0);
            shell.StartMove();
            var bumps = 0;
            shell.Bumped += _ => bumps++;
            Press(shell, GamepadAction.Actions);
            Assert.Null(shell.Sheet);
            Assert.Equal(1, bumps);
            Press(shell, GamepadAction.Cancel);
        });

        [Fact]
        public async Task TodaysLook_KeepsItsOwnMoveMode()
        {
            var (_, _, shell) = await HomeAsync();
            FocusHome(shell, 0);
            shell.StartMove();
            Press(shell, GamepadAction.NavigateRight);
            // Today's: the tile is lifted out and carried over the next; nothing trades places until it is dropped.
            Assert.Equal(-1, shell.HomeIndexOfHeld);
            Assert.Equal(["A Drop", "B Put back"], shell.Hints.Select(hint => $"{hint.Button} {hint.Label}"));
            Press(shell, GamepadAction.Cancel);
            Assert.Null(shell.Typing);
        }
    }
}
