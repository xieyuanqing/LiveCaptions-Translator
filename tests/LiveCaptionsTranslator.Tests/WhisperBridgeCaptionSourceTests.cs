using LiveCaptionsTranslator.captionSources;

namespace LiveCaptionsTranslator.Tests;

public class WhisperBridgeCaptionSourceTests
{
    [Theory]
    [InlineData("ws://127.0.0.1:8765/captions", "ws://127.0.0.1:8765/captions")]
    [InlineData("127.0.0.1:8765/captions", "ws://127.0.0.1:8765/captions")]
    [InlineData("wss://example.com/live", "wss://example.com/live")]
    public void TryNormalizeBridgeUrl_ValidInput_Succeeds(string input, string expected)
    {
        bool success = WhisperBridgeCaptionSource.TryNormalizeBridgeUrl(
            input,
            out string normalized,
            out string error);

        Assert.True(success);
        Assert.Equal(expected, normalized);
        Assert.Equal(string.Empty, error);
    }

    [Fact]
    public void TryNormalizeBridgeUrl_InvalidScheme_Fails()
    {
        bool success = WhisperBridgeCaptionSource.TryNormalizeBridgeUrl(
            "http://127.0.0.1:8765/captions",
            out string normalized,
            out string error);

        Assert.False(success);
        Assert.Equal(string.Empty, normalized);
        Assert.Contains("Invalid bridge URL", error);
    }
}
