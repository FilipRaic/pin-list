using Avalonia.Input;
using Xunit;

namespace PinList.Tests;

/// <summary>Shortcuts are shown and read with each system's own keys.</summary>
public class OsTextTests
{
    static T OnMac<T>(bool mac, System.Func<T> read)
    {
        bool was = OsText.IsMac;
        OsText.IsMac = mac;
        try { return read(); }
        finally { OsText.IsMac = was; }
    }

    [Fact]
    public void Windows_shows_Ctrl_Enter_and_Del()
    {
        Assert.Equal("Ctrl+Z", OnMac(false, () => OsText.Undo));
        Assert.Equal("Ctrl+C", OnMac(false, () => OsText.CopyGesture));
        Assert.Equal("Ctrl+F", OnMac(false, () => OsText.Find));
        Assert.Equal("Enter", OnMac(false, () => OsText.Enter));
        Assert.Equal("Del", OnMac(false, () => OsText.Delete));
        Assert.Equal("Alt+Up", OnMac(false, () => OsText.Alt("Up")));
        Assert.Equal("Exit", OnMac(false, () => OsText.Exit));
        Assert.Contains("Alt+Up or Down", OnMac(false, () => OsText.ItemHelp));
    }

    [Fact]
    public void A_Mac_shows_its_own_key_symbols()
    {
        Assert.Equal("⌘Z", OnMac(true, () => OsText.Undo));
        Assert.Equal("⌘C", OnMac(true, () => OsText.CopyGesture));
        Assert.Equal("↩", OnMac(true, () => OsText.Enter));
        Assert.Equal("⌫", OnMac(true, () => OsText.Delete));
        Assert.Equal("⌥↓", OnMac(true, () => OsText.Alt("Down")));
        Assert.Equal("Quit PinList", OnMac(true, () => OsText.Exit));
        Assert.Equal("Open at login", OnMac(true, () => OsText.Autostart));
        Assert.Contains("Return edits", OnMac(true, () => OsText.ItemHelp));
        Assert.Contains("Option+Up or Down", OnMac(true, () => OsText.ItemHelp));
        Assert.Equal("Return adds the item · Shift+Return starts a new line", OnMac(true, () => OsText.AddItemTip));
    }

    [Theory]
    [InlineData("Up", "⌥↑")]
    [InlineData("Down", "⌥↓")]
    [InlineData("Left", "⌥←")]
    [InlineData("Right", "⌥→")]
    [InlineData("Home", "⌥Home")]
    public void Option_shortcuts_use_arrow_symbols_on_a_Mac(string key, string shown) =>
        Assert.Equal(shown, OnMac(true, () => OsText.Alt(key)));

    [Fact]
    public void Cmd_and_the_Mac_delete_key_only_count_on_a_Mac()
    {
        Assert.True(OnMac(false, () => OsText.IsCommand(KeyModifiers.Control)));
        Assert.False(OnMac(false, () => OsText.IsCommand(KeyModifiers.Meta)));
        Assert.True(OnMac(true, () => OsText.IsCommand(KeyModifiers.Meta)));
        Assert.True(OnMac(true, () => OsText.IsCommand(KeyModifiers.Control)));
        Assert.False(OnMac(true, () => OsText.IsCommand(KeyModifiers.Meta | KeyModifiers.Shift)));

        Assert.True(OnMac(false, () => OsText.IsDeleteKey(Key.Delete)));
        Assert.False(OnMac(false, () => OsText.IsDeleteKey(Key.Back)));
        Assert.True(OnMac(true, () => OsText.IsDeleteKey(Key.Back)));
        Assert.True(OnMac(true, () => OsText.IsDeleteKey(Key.Delete)));
    }
}
