using Avalonia.Headless.XUnit;
using System.Globalization;
using Microsoft.Win32;
using PinList;
using Xunit;

namespace PinList.Tests;

public class ConverterTests
{
    [AvaloniaFact]
    public void First_line_marks_hidden_lines()
    {
        object? Convert(string? text) => Converters.FirstLine.Convert(text, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal("one", Convert("one"));
        Assert.Equal("one  …", Convert("one  \r\ntwo"));
        Assert.Equal("", Convert(null));
    }
}

public class WindowsSettingsTests : StoreTestBase
{
    [AvaloniaTheory]
    [InlineData(100, 1.0)]
    [InlineData(150, 1.5)]
    [InlineData(225, 2.25)]
    [InlineData(99, 1.0)]
    [InlineData(226, 1.0)]
    public void Text_size_is_read_as_a_factor(int percent, double expected) =>
        Assert.Equal(expected, WindowsSettings.ParseTextScale(percent));

    [AvaloniaFact]
    public void Missing_or_odd_text_size_means_normal() =>
        Assert.Equal(1.0, WindowsSettings.ParseTextScale(null));

    [AvaloniaTheory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(null, true)]
    [InlineData("1", true)]
    public void App_mode_dark_unless_set_to_light(object? value, bool dark) =>
        Assert.Equal(dark, WindowsSettings.ParseAppsUseDark(value));

    [AvaloniaFact]
    public void Real_settings_can_be_read()
    {
        Assert.InRange(WindowsSettings.TextScale(), 1.0, 2.25);
        _ = WindowsSettings.AppsUseDark(); // either answer is fine; it must not throw
    }

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
    public void A_moved_exe_gets_its_new_path()
    {
        using (var k = Registry.CurrentUser.CreateSubKey(Key)) k.SetValue("PinList", "\"C:\\old\\PinList.exe\"");
        Autostart.RefreshPath();
        Assert.Equal($"\"{Environment.ProcessPath}\"", Value());
    }

    [AvaloniaFact]
    public void Refreshing_leaves_it_off_when_it_is_off()
    {
        Autostart.RefreshPath();
        Assert.Null(Value());
    }

    [AvaloniaFact]
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

    [AvaloniaFact]
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
        Assert.True(shown.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [AvaloniaFact]
    public void Signal_without_a_listener_reports_false()
    {
        using var only = new SingleInstance(UniqueName());
        Assert.False(only.SignalFirst());
    }

    [AvaloniaFact]
    public void After_closing_the_name_is_free_again()
    {
        var name = UniqueName();
        new SingleInstance(name).Dispose();
        using var again = new SingleInstance(name);
        Assert.True(again.IsFirst);
    }

    [AvaloniaFact]
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
