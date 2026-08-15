using EmuShelf.Core.Metadata;
using EmuShelf.Infrastructure.Metadata;
using EmuShelf.Integrations.Metadata;

namespace EmuShelf.Infrastructure.Tests.Metadata;

public class LibretroDatCatalogTests
{
    [Fact]
    public void ParseLogiqxXml_KeysBySetName_AndTitlesFromDescription()
    {
        using var reader = new StringReader(
            """
            <?xml version="1.0"?>
            <datafile>
              <game name="mslug">
                <description>Metal Slug - Super Vehicle-001</description>
                <year>1996</year>
                <rom name="201-p1.p1" size="1048576" crc="08d8fed6"/>
              </game>
            </datafile>
            """);

        var index = LibretroDatCatalog.ParseLogiqxXml(reader, GameIdentifierKind.ArcadeSetName);

        var entry = Assert.Single(index.Entries);
        Assert.Equal(
            LibretroDatCatalog.NormalizeKey(GameIdentifierKind.ArcadeSetName, "mslug"),
            entry.Key);
        Assert.Equal("Metal Slug - Super Vehicle-001", entry.Value.Title);
    }

    [Fact]
    public void ParseLogiqxXml_SkipsBiosAndDeviceSets()
    {
        using var reader = new StringReader(
            """
            <datafile>
              <game name="neogeo" isbios="yes"><description>Neo Geo BIOS</description></game>
              <game name="nmk004" isdevice="yes"><description>NMK004 MCU</description></game>
              <game name="sf2"><description>Street Fighter II: The World Warrior</description></game>
            </datafile>
            """);

        var index = LibretroDatCatalog.ParseLogiqxXml(reader, GameIdentifierKind.ArcadeSetName);

        var entry = Assert.Single(index.Entries);
        Assert.Equal("Street Fighter II: The World Warrior", entry.Value.Title);
    }

    [Fact]
    public void ParseLogiqxXml_FallsBackToSetNameWhenDescriptionMissing()
    {
        using var reader = new StringReader("""<datafile><game name="puzzle" /></datafile>""");

        var index = LibretroDatCatalog.ParseLogiqxXml(reader, GameIdentifierKind.ArcadeSetName);

        Assert.Equal("puzzle", Assert.Single(index.Entries).Value.Title);
    }

    [Fact]
    public void ParseLogiqxXml_HandlesTheRealFbneoDatShape_KeyingBySetIdAndSkippingBios()
    {
        // A trimmed but faithful sample of the real "FinalBurn Neo (ClrMame Pro XML, Arcade only)"
        // DAT: the Logiqx DOCTYPE, a <header>, the game `name` attribute as the set short id, the
        // title in <description>, a romof/cloneof link, and a BIOS set whose `isbios` attribute
        // precedes `name` — the exact shape the earlier hand-written tests did not cover.
        using var reader = new StringReader(
            """
            <?xml version="1.0"?>
            <!DOCTYPE datafile PUBLIC "-//FinalBurn Neo//DTD ROM Management Datafile//EN" "http://www.logiqx.com/Dats/datafile.dtd">
            <datafile>
              <header>
                <name>FinalBurn Neo - Arcade Games</name>
                <description>FinalBurn Neo v1.0.0.03 Arcade Games</description>
              </header>
              <game name="mslug" romof="neogeo">
                <description>Metal Slug - Super Vehicle-001</description>
                <year>1996</year>
                <rom name="201-p1.p1" size="2097152" crc="08d8daa5"/>
              </game>
              <game isbios="yes" name="bubsys">
                <description>Bubble System BIOS</description>
                <rom name="boot.bin" size="480" crc="f0774fc2"/>
              </game>
              <game name="sf2ce" cloneof="sf2">
                <description>Street Fighter II' - Champion Edition (World 920313)</description>
              </game>
            </datafile>
            """);

        var index = LibretroDatCatalog.ParseLogiqxXml(reader, GameIdentifierKind.ArcadeSetName);

        Assert.True(index.TryGetValue(
            GameIdentifierKind.ArcadeSetName,
            LibretroDatCatalog.NormalizeKey(GameIdentifierKind.ArcadeSetName, "mslug"),
            out var mslug));
        Assert.Equal("Metal Slug - Super Vehicle-001", mslug.Title);
        Assert.True(index.TryGetValue(
            GameIdentifierKind.ArcadeSetName,
            LibretroDatCatalog.NormalizeKey(GameIdentifierKind.ArcadeSetName, "sf2ce"),
            out var sf2ce));
        Assert.Equal("Street Fighter II' - Champion Edition (World 920313)", sf2ce.Title);
        // The BIOS set is excluded even though it carries a description, and the <header> name is
        // never mistaken for a game.
        Assert.False(index.TryGetValue(
            GameIdentifierKind.ArcadeSetName,
            LibretroDatCatalog.NormalizeKey(GameIdentifierKind.ArcadeSetName, "bubsys"),
            out _));
        Assert.Equal(2, index.Entries.Count);
    }

    [Fact]
    public void ArcadeProfile_UsesTheLogiqxXmlDatNotTheClrMameProTextTwin()
    {
        var arcade = KnownMetadataProfiles.All.Single(profile => profile.SystemId == "arcade");

        Assert.Equal(GameIdentifierKind.ArcadeSetName, arcade.CatalogKeyKind);
        Assert.Equal(DatFormat.LogiqxXml, arcade.CatalogFormat);
        // The XML twin ("… (ClrMame Pro XML, Arcade only).dat") keys by set id; the text
        // "FBNeo - Arcade Games.dat" does not contain "ClrMame" and would make XmlReader throw.
        Assert.Contains("ClrMame", arcade.CatalogUri.AbsoluteUri);
    }

    [Fact]
    public void Parser_IndexesTopLevelSerialAndIgnoresNestedRomFields()
    {
        using var reader = new StringReader(
            """
            clrmamepro (
                name "Sony - PlayStation 2"
            )
            game (
                name "007 - Agent Under Fire (USA)"
                region "USA"
                serial "SLUS-20265"
                rom (
                    name "wrong.iso"
                    serial "WRONG-00000"
                )
            )
            """);

        var index = LibretroDatCatalog.Parse(reader, GameIdentifierKind.Serial);

        var entry = Assert.Single(index.Entries).Value;
        Assert.Equal("007 - Agent Under Fire (USA)", entry.Title);
        Assert.Equal("USA", entry.Region);
        Assert.True(index.Entries.ContainsKey("SLUS-20265"));
    }

    [Fact]
    public void Parser_PrefersRetailEntryWhenSerialIsSharedWithBeta()
    {
        using var reader = new StringReader(
            """
            game (
                name "Example Game (USA) (Beta)"
                serial "SLUS_123.45"
            )
            game (
                name "Example Game (USA)"
                serial "SLUS-12345"
            )
            """);

        var index = LibretroDatCatalog.Parse(reader, GameIdentifierKind.Serial);

        Assert.Equal("Example Game (USA)", index.Entries["SLUS-12345"].Title);
    }

    [Fact]
    public void Parser_IndexesNestedRomSha1ForCartridgeCatalogs()
    {
        using var reader = new StringReader(
            """
            game (
                name "Ristar (USA, Europe)"
                region "USA, Europe"
                rom (
                    name "Ristar (USA, Europe).md"
                    sha1 471EE01E97220D35105CC5E9FB2F03765623CD05
                )
            )
            """);

        var index = LibretroDatCatalog.Parse(reader, GameIdentifierKind.Sha1);

        var entry = Assert.Single(index.Entries).Value;
        Assert.Equal("Ristar (USA, Europe)", entry.Title);
        Assert.Equal("USA, Europe", entry.Region);
        Assert.True(index.Entries.ContainsKey("471EE01E97220D35105CC5E9FB2F03765623CD05"));
    }

    [Fact]
    public void Parser_IndexesDreamcastTrackHashAndNestedProductNumberFallback()
    {
        using var reader = new StringReader(
            """
            game (
                name "Tony Hawk's Pro Skater (USA)"
                region "USA"
                rom ( name "Tony Hawk's Pro Skater (USA) (Track 5).bin" sha1 E64CC5A24AA2868D23B597332B0D94647A927A15 serial "T-40205N" )
            )
            """);

        var index = LibretroDatCatalog.Parse(
            reader,
            [GameIdentifierKind.Sha1, GameIdentifierKind.Serial],
            readRomSerials: true);

        Assert.True(index.TryGetValue(
            GameIdentifierKind.Sha1,
            "E64CC5A24AA2868D23B597332B0D94647A927A15",
            out var hashEntry));
        Assert.Equal("Tony Hawk's Pro Skater (USA)", hashEntry.Title);
        Assert.True(index.TryGetValue(GameIdentifierKind.Serial, "T40205N", out var serialEntry));
        Assert.Equal("Tony Hawk's Pro Skater (USA)", serialEntry.Title);
    }

    // clrmamepro writes a whole `rom ( … )` record on one line, so it sits at the same nesting
    // depth as the game's own fields. A serial there belongs to the ROM, and a profile that did
    // not opt in must not key on it — depth alone cannot tell the two apart.
    [Fact]
    public void Parser_WithoutRomSerialOptIn_IgnoresSerialsInsideRomRecords()
    {
        const string dat = """
            game (
                name "Example (USA)"
                region "USA"
                rom ( name "Example (USA) (Track 3).bin" sha1 AABBCCDD serial "T-40205N" )
            )
            """;

        var optedOut = LibretroDatCatalog.Parse(
            new StringReader(dat),
            [GameIdentifierKind.Serial],
            readRomSerials: false);
        var optedIn = LibretroDatCatalog.Parse(
            new StringReader(dat),
            [GameIdentifierKind.Serial],
            readRomSerials: true);

        Assert.False(optedOut.TryGetValue(GameIdentifierKind.Serial, "T40205N", out _));
        Assert.True(optedIn.TryGetValue(GameIdentifierKind.Serial, "T40205N", out _));
    }

    // A region-free 3DS cartridge (a late Pokémon title) has one product code for every regional
    // dump, so a single serial keys several DAT entries whose only difference is the region and the
    // localized name. The No-Intro Korean name has no language suffix, so it is the shortest and
    // historically won the collapse — labelling a European dump "Korea". The filename's region tag
    // must break the tie toward the region the user actually owns.
    [Fact]
    public void Parser_RegionFreeSerial_PrefersTheRegionTheFilenameAdvertises()
    {
        const string dat = """
            game (
                name "Pocket Monsters Ultra Moon (Korea)"
                region "Korea"
                serial "CTR-P-A2BA"
            )
            game (
                name "Pokemon Ultra Moon (Europe) (En,Ja,Fr,De,Es,It,Zh,Ko)"
                region "Europe"
                serial "CTR-P-A2BA"
            )
            game (
                name "Pokemon Ultra Moon (USA) (En,Ja,Fr,De,Es,It,Zh,Ko)"
                region "USA"
                serial "CTR-P-A2BA"
            )
            """;

        var index = LibretroDatCatalog.Parse(new StringReader(dat), GameIdentifierKind.Serial);
        const string key = "CTRPA2BA";

        // A European dump resolves to the European entry, not the shorter Korean one.
        Assert.True(index.TryGetValue(
            GameIdentifierKind.Serial,
            key,
            "Pokemon Ultra Moon (Europe) (En,Ja,Fr,De,Es,It,Zh,Ko)",
            out var europe));
        Assert.Equal("Pokemon Ultra Moon (Europe) (En,Ja,Fr,De,Es,It,Zh,Ko)", europe.Title);
        Assert.Equal("Europe", europe.Region);

        // The same shared serial resolves to the US entry for a US dump.
        Assert.True(index.TryGetValue(
            GameIdentifierKind.Serial,
            key,
            "Pokemon Ultra Moon (USA) (En,Ja,Fr,De,Es,It,Zh,Ko)",
            out var usa));
        Assert.Equal("USA", usa.Region);

        // With no hint the historical region-agnostic pick (the shortest title) is unchanged.
        Assert.True(index.TryGetValue(GameIdentifierKind.Serial, key, out var noHint));
        Assert.Equal("Pocket Monsters Ultra Moon (Korea)", noHint.Title);

        // A region the DAT does not carry (Japan is absent here) falls back to that same pick
        // rather than inventing a match.
        Assert.True(index.TryGetValue(
            GameIdentifierKind.Serial,
            key,
            "Pokemon Ultra Moon (Japan) (En,Ja,Fr,De,Es,It,Zh,Ko)",
            out var japan));
        Assert.Equal("Pocket Monsters Ultra Moon (Korea)", japan.Title);
    }

    // Every disc of a Dreamcast title carries the same product number in IP.BIN, so one serial keys
    // the whole set. The entries differ only by a "(Disc N)" suffix of identical length, so region
    // and PreferenceScore both tie and Disc 1 used to win for discs 2 and 3 as well — naming and
    // covering all three after the first disc.
    [Fact]
    public void Parser_SharedDiscSerial_PrefersTheDiscTheFilenameNames()
    {
        const string dat = """
            game (
                name "Shenmue (Europe) (En,Fr,De,Es) (Disc 1)"
                region "Europe"
                serial "MK-5105950"
            )
            game (
                name "Shenmue (Europe) (En,Fr,De,Es) (Disc 2)"
                region "Europe"
                serial "MK-5105950"
            )
            game (
                name "Shenmue (Europe) (En,Fr,De,Es) (Disc 3)"
                region "Europe"
                serial "MK-5105950"
            )
            """;

        var index = LibretroDatCatalog.Parse(new StringReader(dat), GameIdentifierKind.Serial);
        const string key = "MK5105950";

        foreach (var disc in new[] { 1, 2, 3 })
        {
            Assert.True(index.TryGetValue(
                GameIdentifierKind.Serial,
                key,
                $"Shenmue (Europe) (EnFrDeEs) (Disc {disc})",
                out var entry));
            Assert.Equal($"Shenmue (Europe) (En,Fr,De,Es) (Disc {disc})", entry.Title);
        }

        // Every candidate names a disc, so a filename that names none cannot decide between them and
        // the historical pick still applies.
        Assert.True(index.TryGetValue(GameIdentifierKind.Serial, key, "Shenmue (Europe)", out var noDisc));
        Assert.Equal("Shenmue (Europe) (En,Fr,De,Es) (Disc 1)", noDisc.Title);
    }

    // Naming no disc is itself an answer when the key holds an entry that names none either: that
    // entry wins, rather than whichever disc happens to have the shortest title.
    [Fact]
    public void Parser_SharedDiscSerial_PrefersTheUnnumberedEntryWhenTheFilenameNamesNoDisc()
    {
        const string dat = """
            game (
                name "Example (USA) (Disc 1)"
                region "USA"
                serial "SLUS-00001"
            )
            game (
                name "Example (USA) (Disc 2)"
                region "USA"
                serial "SLUS-00001"
            )
            game (
                name "Example (USA) (Single Disc Edition)"
                region "USA"
                serial "SLUS-00001"
            )
            """;

        var index = LibretroDatCatalog.Parse(new StringReader(dat), GameIdentifierKind.Serial);
        var key = LibretroDatCatalog.NormalizeKey(GameIdentifierKind.Serial, "SLUS-00001");

        Assert.True(index.TryGetValue(GameIdentifierKind.Serial, key, out var hintless));
        Assert.Equal("Example (USA) (Single Disc Edition)", hintless.Title);

        Assert.True(index.TryGetValue(GameIdentifierKind.Serial, key, "Example (USA) (Disc 2)", out var disc2));
        Assert.Equal("Example (USA) (Disc 2)", disc2.Title);
    }

    // A serial shared by an original and its revision: the "(Rev 1)" penalty in PreferenceScore
    // always picked the original, so a Rev 1 dump was named — and grouped — as the original.
    [Fact]
    public void Parser_SharedRevisionSerial_PrefersTheRevisionTheFilenameNames()
    {
        const string dat = """
            game (
                name "Metal Gear Solid (USA) (Disc 1)"
                region "USA"
                serial "SLUS-00594"
            )
            game (
                name "Metal Gear Solid (USA) (Rev 1) (Disc 1)"
                region "USA"
                serial "SLUS-00594"
            )
            """;

        var index = LibretroDatCatalog.Parse(new StringReader(dat), GameIdentifierKind.Serial);
        var key = LibretroDatCatalog.NormalizeKey(GameIdentifierKind.Serial, "SLUS-00594");

        Assert.True(index.TryGetValue(
            GameIdentifierKind.Serial,
            key,
            "Metal Gear Solid (USA) (Rev 1) (Disc 1)",
            out var revised));
        Assert.Equal("Metal Gear Solid (USA) (Rev 1) (Disc 1)", revised.Title);

        // An unrevised dump keeps the original entry: an absent tag matches an absent tag.
        Assert.True(index.TryGetValue(
            GameIdentifierKind.Serial,
            key,
            "Metal Gear Solid (USA) (Disc 1)",
            out var original));
        Assert.Equal("Metal Gear Solid (USA) (Disc 1)", original.Title);
    }

    // The language list a No-Intro filename carries ("(En,Ja,Fr,…)") must never be mistaken for a
    // region: a "Ko" language code does not select a "Korea" entry.
    [Fact]
    public void Parser_RegionFreeSerial_DoesNotTreatLanguageCodesAsRegions()
    {
        const string dat = """
            game (
                name "Pocket Monsters Ultra Moon (Korea)"
                region "Korea"
                serial "CTR-P-A2BA"
            )
            game (
                name "Pokemon Ultra Moon (USA) (En,Ja,Fr,De,Es,It,Zh,Ko)"
                region "USA"
                serial "CTR-P-A2BA"
            )
            """;

        var index = LibretroDatCatalog.Parse(new StringReader(dat), GameIdentifierKind.Serial);

        // The USA filename lists Korean ("Ko") among its languages; the entry must still be USA.
        Assert.True(index.TryGetValue(
            GameIdentifierKind.Serial,
            "CTRPA2BA",
            "Pokemon Ultra Moon (USA) (En,Ja,Fr,De,Es,It,Zh,Ko)",
            out var usa));
        Assert.Equal("USA", usa.Region);
    }

    // The game's own serial is still read when a rom record also carries one, whether or not the
    // profile opted in, because `??=` keeps the first and the game-level field comes first.
    [Fact]
    public void Parser_PrefersTheGameLevelSerialOverARomRecordSerial()
    {
        const string dat = """
            game (
                name "Example (USA)"
                serial "T-11111N"
                rom ( name "Example (USA) (Track 3).bin" sha1 AABBCCDD serial "T-99999N" )
            )
            """;

        foreach (var readRomSerials in new[] { false, true })
        {
            var index = LibretroDatCatalog.Parse(
                new StringReader(dat),
                [GameIdentifierKind.Serial],
                readRomSerials);

            Assert.True(index.TryGetValue(GameIdentifierKind.Serial, "T11111N", out var entry));
            Assert.Equal("Example (USA)", entry.Title);
            Assert.False(index.TryGetValue(GameIdentifierKind.Serial, "T99999N", out _));
        }
    }
}
