using PinList;
using Xunit;

namespace PinList.Tests;

/// <summary>A number with its word, as the questions and messages say it.</summary>
public class CountTextTests
{
    [Theory]
    [InlineData(0, "0 items")]
    [InlineData(1, "1 item")]
    [InlineData(2, "2 items")]
    public void A_regular_word_gets_an_s(int n, string expected) => Assert.Equal(expected, CountText.Of(n, "item"));

    [Theory]
    [InlineData(1, "1 category")]
    [InlineData(3, "3 categories")]
    public void An_irregular_word_brings_its_plural(int n, string expected) =>
        Assert.Equal(expected, CountText.Of(n, "category", "categories"));
}
