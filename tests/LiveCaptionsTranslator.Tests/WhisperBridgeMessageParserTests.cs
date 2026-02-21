using LiveCaptionsTranslator.captionSources;

namespace LiveCaptionsTranslator.Tests;

public class WhisperBridgeMessageParserTests
{
    [Fact]
    public void Parse_CanonicalProtocol_MapsFieldsCorrectly()
    {
        long fallbackSequence = 0;
        string payload = """
            {"text":"こんにちは","isFinal":true,"sequence":12,"source":"bridge","timestamp":"2026-02-21T09:00:00Z","utteranceId":"utt-12"}
            """;

        var updates = WhisperBridgeMessageParser.Parse(payload, ref fallbackSequence, CaptionSourceKinds.WhisperBridge);

        var update = Assert.Single(updates);
        Assert.Equal("こんにちは", update.Text);
        Assert.True(update.IsFinal);
        Assert.Equal(12, update.Sequence);
        Assert.Equal("bridge", update.Source);
        Assert.Equal("utt-12", update.UtteranceId);
        Assert.Equal(DateTimeOffset.Parse("2026-02-21T09:00:00Z"), update.Timestamp);
        Assert.Equal(12, fallbackSequence);
    }

    [Fact]
    public void Parse_AliasSchemaAndPlainText_UsesFallbacks()
    {
        long fallbackSequence = 40;
        string aliasPayload = """
            {"caption":"テスト中","final":"true","seq":"41","ts":1766630400123}
            """;

        var aliasUpdates = WhisperBridgeMessageParser.Parse(aliasPayload, ref fallbackSequence, CaptionSourceKinds.WhisperBridge);
        var alias = Assert.Single(aliasUpdates);

        Assert.Equal("テスト中", alias.Text);
        Assert.True(alias.IsFinal);
        Assert.Equal(41, alias.Sequence);
        Assert.Equal(CaptionSourceKinds.WhisperBridge, alias.Source);

        var plainUpdates = WhisperBridgeMessageParser.Parse("plain text frame", ref fallbackSequence, CaptionSourceKinds.WhisperBridge);
        var plain = Assert.Single(plainUpdates);

        Assert.Equal("plain text frame", plain.Text);
        Assert.False(plain.IsFinal);
        Assert.Equal(42, plain.Sequence);
        Assert.Equal(CaptionSourceKinds.WhisperBridge, plain.Source);
    }

    [Fact]
    public void Parse_WhisperLiveKitLegacySnapshot_ExtractsLatestLineAndBuffers()
    {
        long fallbackSequence = 0;
        string payload = """
            {
              "type":"transcript_update",
              "status":"active_transcription",
              "lines":[
                {"speaker":1,"text":"こんにちは","start":0.1,"end":1.2},
                {"speaker":2,"text":"今日は","start":"0:00:02","end":2.0}
              ],
              "buffer_transcription":" 配信です",
              "buffer_diarization":"",
              "remaining_time_transcription":0.2,
              "remaining_time_diarization":0.1
            }
            """;

        var updates = WhisperBridgeMessageParser.Parse(payload, ref fallbackSequence, CaptionSourceKinds.WhisperBridge);

        var update = Assert.Single(updates);
        Assert.Equal("今日は 配信です", update.Text);
        Assert.False(update.IsFinal);
        Assert.Equal(1, update.Sequence);
        Assert.Equal(CaptionSourceKinds.WhisperBridge, update.Source);
        Assert.Equal("wlk-line-0-00-02", update.UtteranceId);
    }

    [Fact]
    public void Parse_WhisperLiveKitReadyToStop_EmitsFinalFlushUpdate()
    {
        long fallbackSequence = 8;
        string payload = """
            {"type":"ready_to_stop"}
            """;

        var updates = WhisperBridgeMessageParser.Parse(payload, ref fallbackSequence, CaptionSourceKinds.WhisperBridge);

        var update = Assert.Single(updates);
        Assert.Equal(string.Empty, update.Text);
        Assert.True(update.IsFinal);
        Assert.Equal(9, update.Sequence);
        Assert.Equal("wlk-stop", update.UtteranceId);
    }

    [Fact]
    public void Parse_WhisperLiveKitNewSegments_ExtractsTextAndBuffers()
    {
        long fallbackSequence = 0;
        string payload = """
            {
              "type":"transcript_update",
              "status":"active_transcription",
              "segments":[
                {
                  "id":101,
                  "speaker":1,
                  "text":"今日は",
                  "final":true,
                  "buffer":{"transcription":" テスト","diarization":""}
                }
              ]
            }
            """;

        var updates = WhisperBridgeMessageParser.Parse(payload, ref fallbackSequence, CaptionSourceKinds.WhisperBridge);

        var update = Assert.Single(updates);
        Assert.Equal("今日は テスト", update.Text);
        Assert.True(update.IsFinal);
        Assert.Equal("wlk-segment-101", update.UtteranceId);
        Assert.Equal(1, update.Sequence);
    }

    [Fact]
    public void Parse_WhisperLiveKitNewSegments_InfersFinalFromStatus()
    {
        long fallbackSequence = 0;
        string payload = """
            {
              "type":"transcript_update",
              "status":"completed",
              "segments":[
                {
                  "id":201,
                  "speaker":1,
                  "text":"status final"
                }
              ]
            }
            """;

        var updates = WhisperBridgeMessageParser.Parse(payload, ref fallbackSequence, CaptionSourceKinds.WhisperBridge);

        var update = Assert.Single(updates);
        Assert.Equal("status final", update.Text);
        Assert.True(update.IsFinal);
    }

    [Fact]
    public void Parse_LegacyWithoutText_DoesNotSwallowPayloadFallback()
    {
        long fallbackSequence = 0;
        string payload = """
            {
              "lines": [],
              "payload": {"text":"fallback payload text","isFinal":false}
            }
            """;

        var updates = WhisperBridgeMessageParser.Parse(payload, ref fallbackSequence, CaptionSourceKinds.WhisperBridge);

        var update = Assert.Single(updates);
        Assert.Equal("fallback payload text", update.Text);
        Assert.False(update.IsFinal);
    }
}
