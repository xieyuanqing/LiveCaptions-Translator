using LiveCaptionsTranslator.captionSources;

namespace LiveCaptionsTranslator.Tests;

public class WhisperLiveKitNewApiFixtureTests
{
    [Fact]
    public void NewApiMixedFixture_CoversPartialFinalStopEmptyAndMixedPayload()
    {
        string fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "wlk_new_api_mixed.jsonl");
        Assert.True(File.Exists(fixturePath), "New API mixed fixture should exist.");

        long fallbackSequence = 0;
        var allUpdates = new List<CaptionUpdate>();

        foreach (string line in File.ReadLines(fixturePath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var updates = WhisperBridgeMessageParser.Parse(line, ref fallbackSequence, CaptionSourceKinds.WhisperBridge);
            allUpdates.AddRange(updates);
        }

        Assert.DoesNotContain(allUpdates, update => string.IsNullOrWhiteSpace(update.Text) && !update.IsFinal);

        Assert.Contains(allUpdates, update => update.Text == "partial text" && !update.IsFinal);
        Assert.Contains(allUpdates, update => update.Text == "partial text done." && update.IsFinal);
        Assert.Contains(allUpdates, update => update.Text == "status inferred final" && update.IsFinal);
        Assert.Contains(allUpdates, update => update.Text == "bridge fallback" && !update.IsFinal);
        Assert.Contains(allUpdates, update => update.IsFinal && update.UtteranceId == "wlk-stop");
    }
}
