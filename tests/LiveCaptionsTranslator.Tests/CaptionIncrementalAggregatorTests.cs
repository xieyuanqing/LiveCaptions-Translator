using LiveCaptionsTranslator.captionSources;

namespace LiveCaptionsTranslator.Tests;

public class CaptionIncrementalAggregatorTests
{
    [Fact]
    public void JapanesePunctuation_TriggersSentenceCommit()
    {
        var aggregator = new CaptionIncrementalAggregator
        {
            EnablePartial = true,
            IdleFinalizeMs = 1000
        };

        var update = new CaptionUpdate
        {
            Text = "これはテストです。",
            IsFinal = false,
            Sequence = 1,
            Source = CaptionSourceKinds.WhisperBridge,
            Timestamp = DateTimeOffset.UtcNow,
            UtteranceId = "utt-1"
        };

        CaptionIncrementalResult result = aggregator.Process(update);

        Assert.Equal("これはテストです。", result.CommittedText);
    }

    [Fact]
    public void NoPunctuation_CommitsAfterIdleTimeout()
    {
        var aggregator = new CaptionIncrementalAggregator
        {
            EnablePartial = true,
            IdleFinalizeMs = 500
        };

        DateTimeOffset start = DateTimeOffset.Parse("2026-02-21T10:00:00Z");
        var update = new CaptionUpdate
        {
            Text = "無句読点テキスト",
            IsFinal = false,
            Sequence = 1,
            Source = CaptionSourceKinds.WhisperBridge,
            Timestamp = start,
            UtteranceId = "utt-2"
        };

        _ = aggregator.Process(update);
        CaptionIncrementalResult flush = aggregator.FlushIfIdle(start.AddMilliseconds(800));

        Assert.Equal("無句読点テキスト", flush.CommittedText);
    }
}
