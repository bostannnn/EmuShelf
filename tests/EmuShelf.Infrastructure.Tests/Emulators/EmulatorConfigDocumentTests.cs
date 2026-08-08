using EmuShelf.Integrations.Emulators;

namespace EmuShelf.Infrastructure.Tests.Emulators;

public sealed class EmulatorConfigDocumentTests
{
    [Fact]
    public void SetValue_ReplacesExisting_PreservingKeyTextAndSpacing()
    {
        var document = new EmulatorConfigDocument("[Hotkeys]\nFastForward = Keyboard/Tab\n");

        Assert.True(document.SetValue("Hotkeys", "FastForward", "SDL-0/B"));

        Assert.Equal("[Hotkeys]\nFastForward = SDL-0/B\n", document.ToText());
    }

    [Fact]
    public void SetValue_ThatMatchesCurrent_MakesNoChange()
    {
        var document = new EmulatorConfigDocument("[Hotkeys]\nFastForward = SDL-0/B\n");

        Assert.False(document.SetValue("Hotkeys", "FastForward", "SDL-0/B"));
        Assert.False(document.Changed);
    }

    [Fact]
    public void SetValue_InsertsMissingKey_AtEndOfSectionBeforeNextSection()
    {
        var document = new EmulatorConfigDocument("[Hotkeys]\nExisting = 1\n[Other]\nX = 2\n");

        document.SetValue("Hotkeys", "PowerOff", "SDL-0/Back & SDL-0/Start");

        Assert.Equal(
            "[Hotkeys]\nExisting = 1\nPowerOff = SDL-0/Back & SDL-0/Start\n[Other]\nX = 2\n",
            document.ToText());
    }

    [Fact]
    public void SetValue_CreatesMissingSection_WhenAllowed()
    {
        var document = new EmulatorConfigDocument("[Main]\nSettingsVersion = 3\n");

        document.SetValue("Hotkeys", "PowerOff", "v");

        Assert.True(document.HasSection("Hotkeys"));
        Assert.Equal("v", document.GetValue("Hotkeys", "PowerOff"));
    }

    [Fact]
    public void SetValue_DoesNotCreateSection_ForFlatFilesWhenTheKeyIsAbsent()
    {
        var document = new EmulatorConfigDocument("input_a = \"1\"\n");

        document.SetValue(null, "input_new", "\"9\"");

        Assert.Equal("\"9\"", document.GetValue(null, "input_new"));
    }

    [Fact]
    public void PreservesCarriageReturns_AndComments_AndUnknownKeys()
    {
        var original = "; a comment\r\n[Hotkeys]\r\n# another\r\nUnknown = keepme\r\nExit = old\r\n";
        var document = new EmulatorConfigDocument(original);

        document.SetValue("Hotkeys", "Exit", "new");

        Assert.Equal("; a comment\r\n[Hotkeys]\r\n# another\r\nUnknown = keepme\r\nExit = new\r\n", document.ToText());
    }

    [Fact]
    public void PreservesAbsenceOfTrailingNewline()
    {
        var document = new EmulatorConfigDocument("[A]\nk = 1");

        document.SetValue("A", "k", "2");

        Assert.Equal("[A]\nk = 2", document.ToText());
    }

    [Fact]
    public void KeysWithValue_And_RemoveKey_OperateWithinSection()
    {
        var document = new EmulatorConfigDocument(
            "[Hotkeys]\nA = chord\nB = chord\nC = other\n[Pad]\nA = chord\n");

        Assert.Equal(["A", "B"], document.KeysWithValue("Hotkeys", "chord"));

        Assert.True(document.RemoveKey("Hotkeys", "A"));
        Assert.Null(document.GetValue("Hotkeys", "A"));
        // The identically named key in another section is untouched.
        Assert.Equal("chord", document.GetValue("Pad", "A"));
    }

    [Fact]
    public void FlatFile_ReplacesValue_PreservingQuotes()
    {
        var document = new EmulatorConfigDocument("input_save_state_btn = \"5\"\ninput_load_state_btn = \"7\"\n");

        document.SetValue(null, "input_save_state_btn", "\"3\"");

        Assert.Equal("input_save_state_btn = \"3\"\ninput_load_state_btn = \"7\"\n", document.ToText());
    }

    [Fact]
    public void KeyMatching_IsCaseInsensitive_ButKeepsOriginalKeyText()
    {
        var document = new EmulatorConfigDocument("[Hotkeys]\nPowerOff = old\n");

        document.SetValue("Hotkeys", "poweroff", "new");

        Assert.Equal("[Hotkeys]\nPowerOff = new\n", document.ToText());
    }
}
