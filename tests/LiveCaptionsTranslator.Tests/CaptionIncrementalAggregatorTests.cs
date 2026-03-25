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

    [Fact]
    public void FinalGrowingPrefix_IsNotSuppressedAsDuplicate()
    {
        var aggregator = new CaptionIncrementalAggregator
        {
            EnablePartial = true,
            IdleFinalizeMs = 1000,
            SyncCommitThreshold = 3
        };

        var first = aggregator.Process(new CaptionUpdate
        {
            Text = "今日は",
            IsFinal = true,
            Sequence = 1,
            Source = CaptionSourceKinds.WhisperBridge,
            Timestamp = DateTimeOffset.UtcNow,
            UtteranceId = "utt-grow"
        });

        var second = aggregator.Process(new CaptionUpdate
        {
            Text = "今日は配信です",
            IsFinal = true,
            Sequence = 2,
            Source = CaptionSourceKinds.WhisperBridge,
            Timestamp = DateTimeOffset.UtcNow,
            UtteranceId = "utt-grow"
        });

        Assert.Equal("今日は", first.CommittedText);
        Assert.Equal("今日は配信です", second.CommittedText);
    }

    [Fact]
    public void ContinuousPartialText_CommitsBySyncThreshold()
    {
        var aggregator = new CaptionIncrementalAggregator
        {
            EnablePartial = true,
            IdleFinalizeMs = 5000,
            SyncCommitThreshold = 1
        };

        _ = aggregator.Process(new CaptionUpdate
        {
            Text = "無句読点テキスト",
            IsFinal = false,
            Sequence = 1,
            Source = CaptionSourceKinds.WhisperBridge,
            Timestamp = DateTimeOffset.UtcNow,
            UtteranceId = "utt-sync"
        });

        CaptionIncrementalResult second = aggregator.Process(new CaptionUpdate
        {
            Text = "無句読点テキストが続きます",
            IsFinal = false,
            Sequence = 2,
            Source = CaptionSourceKinds.WhisperBridge,
            Timestamp = DateTimeOffset.UtcNow,
            UtteranceId = "utt-sync"
        });

        Assert.Equal("無句読点テキストが続きます", second.CommittedText);
    }
}
