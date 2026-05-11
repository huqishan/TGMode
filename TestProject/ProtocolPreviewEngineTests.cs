using Module.Communication.Models;
using System.Text.Json;

namespace TestProject;

public class ProtocolPreviewEngineTests
{
    [Test]
    public void TryBuildResponsePreview_WhenLuaReturnsSingleValue_WrapsIntoDataKey()
    {
        ProtocolCommandConfig command = new()
        {
            ResponseFormat = ProtocolPayloadFormat.Ascii,
            SampleResponseText = "OK,25.6",
            ParseRulesText = "return string.sub(data, 1, 2)"
        };

        bool success = ProtocolPreviewEngine.TryBuildResponsePreview(
            command,
            out ProtocolResponsePreviewResult? previewResult,
            out string message);

        Assert.That(success, Is.True, message);
        Assert.That(previewResult, Is.Not.Null);

        using JsonDocument document = JsonDocument.Parse(previewResult!.ParsedJson);
        Assert.That(document.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Object));
        Assert.That(document.RootElement.GetProperty("Data").GetString(), Is.EqualTo("OK"));
    }

    [Test]
    public void TryBuildResponsePreview_WhenLuaReturnsNil_ReturnsEmptyParsedJson()
    {
        ProtocolCommandConfig command = new()
        {
            ResponseFormat = ProtocolPayloadFormat.Ascii,
            SampleResponseText = "OK,25.6",
            ParseRulesText = "return nil"
        };

        bool success = ProtocolPreviewEngine.TryBuildResponsePreview(
            command,
            out ProtocolResponsePreviewResult? previewResult,
            out string message);

        Assert.That(success, Is.True, message);
        Assert.That(previewResult, Is.Not.Null);
        Assert.That(previewResult!.ParsedJson, Is.EqualTo(string.Empty));
    }
}
