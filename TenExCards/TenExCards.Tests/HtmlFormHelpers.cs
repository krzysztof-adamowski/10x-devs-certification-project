using System.Text.RegularExpressions;

namespace TenExCards.Tests;

/// <summary>
/// Minimal scraping for the statically-rendered Identity forms: extracts the hidden
/// <c>__RequestVerificationToken</c> / <c>_handler</c> inputs an <c>EditForm</c> with
/// <c>FormName</c> emits, so a test can round-trip a POST the way a browser would.
/// </summary>
internal static partial class HtmlFormHelpers
{
    [GeneratedRegex(@"<input\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex InputTagRegex();

    [GeneratedRegex("name=\"([^\"]*)\"", RegexOptions.IgnoreCase)]
    private static partial Regex NameAttributeRegex();

    [GeneratedRegex("value=\"([^\"]*)\"", RegexOptions.IgnoreCase)]
    private static partial Regex ValueAttributeRegex();

    public static Dictionary<string, string> ExtractHiddenFields(string html, params string[] fieldNames)
    {
        var result = new Dictionary<string, string>();

        foreach (Match inputMatch in InputTagRegex().Matches(html))
        {
            var tag = inputMatch.Value;
            var nameMatch = NameAttributeRegex().Match(tag);
            if (!nameMatch.Success || !fieldNames.Contains(nameMatch.Groups[1].Value))
            {
                continue;
            }

            var valueMatch = ValueAttributeRegex().Match(tag);
            result[nameMatch.Groups[1].Value] =
                valueMatch.Success ? System.Net.WebUtility.HtmlDecode(valueMatch.Groups[1].Value) : "";
        }

        return result;
    }

    public static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient client, string url, Dictionary<string, string> fields)
    {
        using var content = new FormUrlEncodedContent(fields);
        return await client.PostAsync(url, content);
    }
}
