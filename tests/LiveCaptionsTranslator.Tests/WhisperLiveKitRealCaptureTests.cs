using LiveCaptionsTranslator.captionSources;

namespace LiveCaptionsTranslator.Tests;

public class WhisperLiveKitRealCaptureTests
{
    [Fact]
    public void RealCapture_IsParsedWithLegacySnapshotAndStopSignal()
    {
        string fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "wlk_real_capture.jsonl");
        Assert.True(File.Exists(fixturePath), "Real WLK capture fixture should exist.");

        long fallbackSequence = 0;
        int totalUpdates = 0;
        bool sawStopSignal = false;
        var parsedTexts = new List<string>();

        foreach (string line in File.ReadLines(fixturePath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var updates = WhisperBridgeMessageParser.Parse(line, ref fallbackSequence, CaptionSourceKinds.WhisperBridge);
            foreach (CaptionUpdate update in updates)
            {
                totalUpdates++;
                if (!string.IsNullOrWhiteSpace(update.Text))
                    parsedTexts.Add(update.Text);

                if (update.IsFinal && update.UtteranceId == "wlk-stop")
                    sawStopSignal = true;
            }
        }

        Assert.True(totalUpdates > 0);
        Assert.True(parsedTexts.Count > 0);
        Assert.Contains(parsedTexts, text => text.Contains("fellow Americans", StringComparison.OrdinalIgnoreCase));
        Assert.True(sawStopSignal);
    }
}
