using System.Globalization;
using System.Windows;
using Microsoft.Win32;
using PinList;
using Xunit;

namespace PinList.Tests;

public class ConverterTests
{
    static readonly CultureInfo C = CultureInfo.InvariantCulture;

    [Fact]
    public void Bool_to_visibility_and_back_is_not_supported()
    {
        var c = new BoolToVisibilityConverter();
        Assert.Equal(Visibility.Visible, c.Convert(true, typeof(Visibility), null!, C));
        Assert.Equal(Visibility.Collapsed, c.Convert(false, typeof(Visibility), null!, C));
        Assert.Equal(Visibility.Collapsed, c.Convert(true, typeof(Visibility), "invert", C));
        Assert.Equal(Visibility.Visible, c.Convert("not a bool", typeof(Visibility), "invert", C));
        Assert.Throws<NotSupportedException>(() => c.ConvertBack(Visibility.Visible, typeof(bool), null!, C));
    }

    [Fact]
    public void First_line_marks_hidden_lines()
    {
        var c = new FirstLineConverter();
        Assert.Equal("one", c.Convert("one", typeof(string), null!, C));
        Assert.Equal("one  …", c.Convert("one \r\ntwo", typeof(string), null!, C));
        Assert.Equal("", c.Convert(null!, typeof(string), null!, C));
        Assert.Throws<NotSupportedException>(() => c.ConvertBack("", typeof(string), null!, C));
    }

    [Fact]
    public void Empty_text_shows_the_placeholder()
    {
        var c = new EmptyToVisibleConverter();
        Assert.Equal(Visibility.Visible, c.Convert("", typeof(Visibility), null!, C));
        Assert.Equal(Visibility.Visible, c.Convert(null!, typeof(Visibility), null!, C));
        Assert.Equal(Visibility.Collapsed, c.Convert("x", typeof(Visibility), null!, C));
        Assert.Throws<NotSupportedException>(() => c.ConvertBack(Visibility.Visible, typeof(string), null!, C));
    }
}

public class WindowsSettingsTests : StoreTestBase
{
    [Theory]
    [InlineData(100, 1.0)]
    [InlineData(150, 1.5)]
    [InlineData(225, 2.25)]
    [InlineData(99, 1.0)]
    [InlineData(226, 1.0)]
    public void Text_size_is_read_as_a_factor(int percent, double expected) =>
        Assert.Equal(expected, WindowsSettings.ParseTextScale(percent));

    [Fact]
    public void Missing_or_odd_text_size_means_normal() =>
        Assert.Equal(1.0, WindowsSettings.ParseTextScale(null));

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(null, true)]
    [InlineData("1", true)]
    public void App_mode_dark_unless_set_to_light(object? value, bool dark) =>
        Assert.Equal(dark, WindowsSettings.ParseAppsUseDark(value));

    [Fact]
    public void Real_settings_can_be_read()
    {
        Assert.InRange(WindowsSettings.TextScale(), 1.0, 2.25);
        _ = WindowsSettings.AppsUseDark(); // either answer is fine; it must not throw
    }

    [Fact]
    public void Unreadable_values_are_null_and_logged()
    {
        Assert.Null(WindowsSettings.Read(@"NOT_A_ROOT\Key", "Value"));
        Assert.Contains("NOT_A_ROOT", File.ReadAllText(Path.Combine(Dir, "log.txt")));
        Assert.Null(WindowsSettings.Read(@"HKEY_CURRENT_USER\Software\PinListTests\Missing", "Value"));
    }
}

/// <summary>Uses its own registry key (HKCU\Software\PinListTests\Run), never the real "Start with Windows" list.</summary>
public class AutostartTests : StoreTestBase
{
    const string Key = @"Software\PinListTests\AutostartTest";
    readonly string _realKey = Autostart.RunKey;

    public AutostartTests()
    {
        Autostart.RunKey = Key;
        Registry.CurrentUser.DeleteSubKeyTree(Key, false);
    }

    public override void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(Key, false);
        Autostart.RunKey = _realKey;
        base.Dispose();
    }

    string? Value()
    {
        using var k = Registry.CurrentUser.OpenSubKey(Key);
        return k?.GetValue("PinList") as string;
    }

    [Fact]
    public void Turning_on_and_off()
    {
        Assert.False(Autostart.IsEnabled());
        Autostart.Set(true);
        Assert.True(Autostart.IsEnabled());
        Assert.Equal($"\"{Environment.ProcessPath}\"", Value());

        Autostart.Set(false);
        Assert.False(Autostart.IsEnabled());
        Autostart.Set(false); // already off: fine
    }

    [Fact]
    public void A_moved_exe_gets_its_new_path()
    {
        using (var k = Registry.CurrentUser.CreateSubKey(Key)) k.SetValue("PinList", "\"C:\\old\\PinList.exe\"");
        Autostart.RefreshPath();
        Assert.Equal($"\"{Environment.ProcessPath}\"", Value());
    }

    [Fact]
    public void Refreshing_leaves_it_off_when_it_is_off()
    {
        Autostart.RefreshPath();
        Assert.Null(Value());
    }

    [Fact]
    public void Registry_errors_are_logged_not_thrown()
    {
        Autostart.RunKey = new string('x', 300); // longer than a registry key name may be
        Autostart.Set(true);
        Autostart.RefreshPath();
        Assert.Contains("Autostart", File.ReadAllText(Path.Combine(Dir, "log.txt")));
    }
}

public class SingleInstanceTests
{
    static string UniqueName() => "PinListTest." + Guid.NewGuid().ToString("N");

    [Fact]
    public void Second_instance_is_not_first_and_signals_the_first()
    {
        var name = UniqueName();
        using var first = new SingleInstance(name);
        Assert.True(first.IsFirst);

        using var shown = new ManualResetEventSlim();
        first.ListenForShow(shown.Set);

        using var second = new SingleInstance(name);
        Assert.False(second.IsFirst);
        Assert.True(second.SignalFirst());
        Assert.True(shown.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Signal_without_a_listener_reports_false()
    {
        using var only = new SingleInstance(UniqueName());
        Assert.False(only.SignalFirst());
    }

    [Fact]
    public void After_closing_the_name_is_free_again()
    {
        var name = UniqueName();
        new SingleInstance(name).Dispose();
        using var again = new SingleInstance(name);
        Assert.True(again.IsFirst);
    }

    [Fact]
    public void Closing_from_another_thread_still_frees_the_name()
    {
        var name = UniqueName();
        var instance = new SingleInstance(name);
        var t = new Thread(instance.Dispose); // not the thread that owns the mutex
        t.Start();
        t.Join();
        using var again = new SingleInstance(name);
        Assert.True(again.IsFirst);
    }
}
