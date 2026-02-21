using LiveCaptionsTranslator.captionSources;

namespace LiveCaptionsTranslator.Tests;

public class LegacyWindowsCaptionAggregatorTests
{
    [Fact]
    public void CompleteSentence_CommitsImmediately()
    {
        var aggregator = new LegacyWindowsCaptionAggregator();
        var options = new LegacyWindowsCaptionOptions
        {
            ContextCount = 0,
            DisplaySentences = 1,
            MaxSyncInterval = 3,
            MaxIdleInterval = 50
        };

        LegacyWindowsCaptionResult result = aggregator.Process("今日は配信を始めます。", options);

        Assert.Equal("今日は配信を始めます。", result.CommittedText);
        Assert.Equal("今日は配信を始めます。", result.CurrentOriginalCaption);
    }

    [Fact]
    public void NoPunctuation_CommitsOnIdleTimeout()
    {
        var aggregator = new LegacyWindowsCaptionAggregator();
        var options = new LegacyWindowsCaptionOptions
        {
            ContextCount = 0,
            DisplaySentences = 1,
            MaxSyncInterval = 99,
            MaxIdleInterval = 2
        };

        LegacyWindowsCaptionResult first = aggregator.Process("配信始めます", options);
        LegacyWindowsCaptionResult second = aggregator.Process("配信始めます", options);
        LegacyWindowsCaptionResult third = aggregator.Process("配信始めます", options);

        Assert.Null(first.CommittedText);
        Assert.Null(second.CommittedText);
        Assert.Equal("配信始めます", third.CommittedText);
    }
}
