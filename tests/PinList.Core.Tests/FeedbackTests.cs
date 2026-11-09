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

    [Fact]
    public void The_system_is_escaped_and_trimmed()
    {
        Assert.Equal("https://filipraic.github.io/pin-list/feedback?v=2.8.0&os=Microsoft%20Windows%2010.0.26300",
            Feedback.Url(new Version(2, 8, 0), " Microsoft Windows 10.0.26300 "));
    }
}
