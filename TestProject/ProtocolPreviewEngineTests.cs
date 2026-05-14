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
        Assert.That(previewResult.ParsedKeys, Is.EquivalentTo(new[] { "Data" }));
    }

    [Test]
    public void TryBuildResponsePreview_WhenLuaReturnsTable_ExposesTableKeys()
    {
        ProtocolCommandConfig command = new()
        {
            ResponseFormat = ProtocolPayloadFormat.Ascii,
            SampleResponseText = "OK,T1,25.6",
            ParseRulesText = "return { Status = string.sub(data, 1, 2), Value = string.sub(data, 7) }"
        };

        bool success = ProtocolPreviewEngine.TryBuildResponsePreview(
            command,
            out ProtocolResponsePreviewResult? previewResult,
            out string message);

        Assert.That(success, Is.True, message);
        Assert.That(previewResult, Is.Not.Null);
        Assert.That(previewResult!.ParsedKeys, Is.EquivalentTo(new[] { "Status", "Value" }));
    }

    [Test]
    public void TryRefreshParsedResultKeys_WhenParseSucceeds_StoresKeysOnCommand()
    {
        ProtocolConfigProfile profile = new()
        {
            ResponseFormat = ProtocolPayloadFormat.Ascii,
            SampleResponseText = "OK,T1,25.6",
            ParseRulesText = "return { Status = string.sub(data, 1, 2), Value = string.sub(data, 7) }"
        };

        bool success = ProtocolPreviewEngine.TryRefreshParsedResultKeys(profile, out string message);

        Assert.That(success, Is.True, message);
        Assert.That(profile.CurrentCommand.ParsedResultKeys, Is.EquivalentTo(new[] { "Status", "Value" }));
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
