using System.Text;

using LiveCaptionsTranslator.utils;

namespace LiveCaptionsTranslator.captionSources
{
    public sealed class LegacyWindowsCaptionAggregator
    {
        private int idleCount;
        private int syncCount;
        private string lastOriginalCaption = string.Empty;

        public void Reset()
        {
            idleCount = 0;
            syncCount = 0;
            lastOriginalCaption = string.Empty;
        }

        public LegacyWindowsCaptionResult Process(string fullText, LegacyWindowsCaptionOptions options)
        {
            if (string.IsNullOrWhiteSpace(fullText))
                return LegacyWindowsCaptionResult.Empty;

            int maxSyncInterval = Math.Max(0, options.MaxSyncInterval);
            int maxIdleInterval = Math.Max(1, options.MaxIdleInterval);
            int displaySentences = Math.Max(0, options.DisplaySentences);

            fullText = RegexPatterns.Acronym().Replace(fullText, "$1$2");
            fullText = RegexPatterns.AcronymWithWords().Replace(fullText, "$1 $2");
            fullText = RegexPatterns.PunctuationSpace().Replace(fullText, "$1 ");
            fullText = RegexPatterns.CJPunctuationSpace().Replace(fullText, "$1");
            fullText = TextUtil.ReplaceNewlines(fullText, TextUtil.MEDIUM_THRESHOLD);

            if (string.IsNullOrWhiteSpace(fullText))
                return LegacyWindowsCaptionResult.Empty;

            bool shouldClearContexts =
                fullText.IndexOfAny(TextUtil.PUNC_EOS) == -1 && options.ContextCount > 0;

            int lastEOSIndex;
            if (Array.IndexOf(TextUtil.PUNC_EOS, fullText[^1]) != -1)
                lastEOSIndex = fullText[0..^1].LastIndexOfAny(TextUtil.PUNC_EOS);
            else
                lastEOSIndex = fullText.LastIndexOfAny(TextUtil.PUNC_EOS);

            string latestCaption = fullText[(lastEOSIndex + 1)..];
            if (lastEOSIndex > 0 && Encoding.UTF8.GetByteCount(latestCaption) < TextUtil.SHORT_THRESHOLD)
            {
                lastEOSIndex = fullText[..lastEOSIndex].LastIndexOfAny(TextUtil.PUNC_EOS);
                latestCaption = fullText[(lastEOSIndex + 1)..];
            }

            string overlayOriginalCaption = latestCaption;
            int overlayEOSIndex = lastEOSIndex;
            for (int historyCount = Math.Min(displaySentences, options.ContextCount);
                 historyCount > 0 && overlayEOSIndex > 0;
                 historyCount--)
            {
                overlayEOSIndex = fullText[..overlayEOSIndex].LastIndexOfAny(TextUtil.PUNC_EOS);
                overlayOriginalCaption = fullText[(overlayEOSIndex + 1)..];
            }

            string displayOriginalCaption = TextUtil.ShortenDisplaySentence(
                latestCaption, TextUtil.VERYLONG_THRESHOLD);

            int lastEOS = latestCaption.LastIndexOfAny(TextUtil.PUNC_EOS);
            if (lastEOS != -1)
                latestCaption = latestCaption[..(lastEOS + 1)];

            string? committedText = null;
            if (string.CompareOrdinal(lastOriginalCaption, latestCaption) != 0)
            {
                lastOriginalCaption = latestCaption;
                idleCount = 0;

                if (!string.IsNullOrEmpty(lastOriginalCaption) &&
                    Array.IndexOf(TextUtil.PUNC_EOS, lastOriginalCaption[^1]) != -1)
                {
                    syncCount = 0;
                    committedText = lastOriginalCaption;
                }
                else if (!string.IsNullOrEmpty(lastOriginalCaption) &&
                         Encoding.UTF8.GetByteCount(lastOriginalCaption) >= TextUtil.SHORT_THRESHOLD)
                {
                    syncCount++;
                }
            }
            else
            {
                idleCount++;
            }

            if ((syncCount > maxSyncInterval || idleCount == maxIdleInterval) &&
                !string.IsNullOrWhiteSpace(lastOriginalCaption))
            {
                syncCount = 0;
                committedText ??= lastOriginalCaption;
            }

            return new LegacyWindowsCaptionResult
            {
                DisplayOriginalCaption = displayOriginalCaption,
                OverlayOriginalCaption = overlayOriginalCaption,
                CurrentOriginalCaption = latestCaption,
                CommittedText = committedText,
                ShouldClearContexts = shouldClearContexts
            };
        }
    }

    public sealed class LegacyWindowsCaptionOptions
    {
        public int ContextCount { get; init; }
        public int DisplaySentences { get; init; }
        public int MaxSyncInterval { get; init; }
        public int MaxIdleInterval { get; init; }
    }

    public sealed class LegacyWindowsCaptionResult
    {
        public static LegacyWindowsCaptionResult Empty { get; } = new();

        public string DisplayOriginalCaption { get; init; } = string.Empty;
        public string OverlayOriginalCaption { get; init; } = string.Empty;
        public string CurrentOriginalCaption { get; init; } = string.Empty;
        public string? CommittedText { get; init; }
        public bool ShouldClearContexts { get; init; }
    }
}
