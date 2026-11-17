namespace PinList;

/// <summary>A number with its word, singular or plural: "1 item", "3 items", "2 categories".</summary>
public static class CountText
{
    /// <param name="plural">The plural when it isn't the word with an s ("categories").</param>
    public static string Of(int n, string word, string? plural = null) =>
        n == 1 ? $"1 {word}" : $"{n} {plural ?? word + "s"}";
}
