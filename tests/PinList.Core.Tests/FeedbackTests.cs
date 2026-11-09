using PinList;
using Xunit;

namespace PinList.Tests;

/// <summary>The address of the feedback form on the website.</summary>
public class FeedbackTests
{
    [Fact]
    public void The_address_has_the_version_and_the_system()
    {
        Assert.Equal("https://filipraic.github.io/pin-list/feedback?v=2.8.0&os=Android",
            Feedback.Url(new Version(2, 8, 0, 0), "Android"));
    }

    [Theory]
    [InlineData(26300, "26H2", 9711, "Windows 11 26H2 (build 26300.9711)")] // Windows 11 says it's 10.0
    [InlineData(22000, null, null, "Windows 11 (build 22000)")]
    [InlineData(19045, "22H2", 4170, "Windows 10 22H2 (build 19045.4170)")]
    [InlineData(19045, " ", null, "Windows 10 (build 19045)")]
    public void Windows_11_is_told_apart_by_its_build(int build, string? release, int? update, string expected)
    {
        Assert.Equal(expected, Feedback.WindowsName(new Version(10, 0, build, 0), release, update));
    }

    [Fact]
    public void Older_windows_keep_their_version_number()
    {
        Assert.Equal("Windows 6.3 (build 9600)", Feedback.WindowsName(new Version(6, 3, 9600), null, null));
    }

    [Fact]
    public void Mac_and_android_names()
    {
        Assert.Equal("macOS 15.1", Feedback.MacName(new Version(15, 1, 0)));
        Assert.Equal("macOS 15.1.2", Feedback.MacName(new Version(15, 1, 2)));
        Assert.Equal("Android 15 (Xiaomi 23078PND5G)", Feedback.AndroidName("15", " Xiaomi 23078PND5G "));
        Assert.Equal("Android 14", Feedback.AndroidName(" 14 ", ""));
        Assert.Equal("Android", Feedback.AndroidName(null, null));
    }

    [Fact]
    public void The_system_is_escaped_and_trimmed()
    {
        Assert.Equal("https://filipraic.github.io/pin-list/feedback?v=2.8.0&os=Microsoft%20Windows%2010.0.26300",
            Feedback.Url(new Version(2, 8, 0), " Microsoft Windows 10.0.26300 "));
    }
}
