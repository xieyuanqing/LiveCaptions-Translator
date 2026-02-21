using LiveCaptionsTranslator.captionSources;

namespace LiveCaptionsTranslator.Tests;

public class WhisperBridgeReplayTests
{
    [Fact]
    public void ReplayCapture_ProducesStableCommittedCaptions()
    {
        string fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "whisper_bridge_capture.jsonl");
        Assert.True(File.Exists(fixturePath), "Replay fixture file should exist.");

        var aggregator = new CaptionIncrementalAggregator
        {
            EnablePartial = true,
            IdleFinalizeMs = 600
        };

        long fallbackSequence = 0;
        var committedCaptions = new List<string>();

        foreach (string line in File.ReadLines(fixturePath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var updates = WhisperBridgeMessageParser.Parse(line, ref fallbackSequence, CaptionSourceKinds.WhisperBridge);
            foreach (CaptionUpdate update in updates)
            {
                CaptionIncrementalResult result = aggregator.Process(update);
                if (!string.IsNullOrWhiteSpace(result.CommittedText))
                    committedCaptions.Add(result.CommittedText);
            }
        }

        CaptionIncrementalResult tail = aggregator.FlushIfIdle(DateTimeOffset.UtcNow.AddMinutes(1));
        if (!string.IsNullOrWhiteSpace(tail.CommittedText))
            committedCaptions.Add(tail.CommittedText);

        Assert.Equal(
        [
            "こんにちは。",
            "次の文です",
            "これは単発です。"
        ],
        committedCaptions);
    }
}
