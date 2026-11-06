using Trackarr.Services.UI;

namespace Trackarr.Tests;

public class StatusMessageDecodeTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[]")]
    public void FormatStatusMessages_EmptyOrNoGroups_ReturnsDash(string? json)
    {
        var result = StatusMessageDecode.FormatStatusMessages(json);
        Assert.Equal("—", result);
    }

    [Fact]
    public void FormatStatusMessages_ValidGroups_RendersTitleAndMessages()
    {
        var result = StatusMessageDecode.FormatStatusMessages(
            """[{"title":"Import failed","messages":["No files found","Permission denied"]}]""");

        Assert.Contains("<ul", result);
        Assert.Contains("Import failed", result);
        Assert.Contains("No files found", result);
        Assert.Contains("Permission denied", result);
    }

    [Fact]
    public void FormatStatusMessages_EncodesHtmlInTitlesAndMessages()
    {
        // The title/message content binds now, so the encoding path must keep it injection-safe.
        var result = StatusMessageDecode.FormatStatusMessages(
            """[{"title":"<b>x</b>","messages":["<script>alert(1)</script>"]}]""");

        Assert.DoesNotContain("<script>", result);
        Assert.DoesNotContain("<b>x</b>", result);
        Assert.Contains("&lt;script&gt;", result);
    }

    [Fact]
    public void FormatStatusMessages_MalformedJson_IsHtmlEncoded()
    {
        // Not valid JSON, so the decoder falls back to encoding the raw input.
        // This guards against HTML/script injection through a status message blob.
        var result = StatusMessageDecode.FormatStatusMessages("<script>alert('xss')</script>");

        Assert.DoesNotContain("<script>", result);
        Assert.Contains("&lt;script&gt;", result);
    }
}
