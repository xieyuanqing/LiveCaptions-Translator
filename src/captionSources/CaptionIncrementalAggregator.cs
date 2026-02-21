using LiveCaptionsTranslator.utils;

namespace LiveCaptionsTranslator.captionSources
{
    public sealed class CaptionIncrementalAggregator
    {
        private static readonly char[] TerminalPunctuation = ".?!。？！!！?？…".ToCharArray();

        private long lastSequence = -1;
        private string currentText = string.Empty;
        private string currentUtteranceId = string.Empty;
        private string lastCommittedText = string.Empty;
        private string lastDisplayText = string.Empty;
        private DateTimeOffset lastUpdateAt = DateTimeOffset.MinValue;
        private readonly Queue<string> pendingCommittedTexts = new();

        public bool EnablePartial { get; set; } = true;
        public int IdleFinalizeMs { get; set; } = 1200;

        public void Reset()
        {
            lastSequence = -1;
            currentText = string.Empty;
            currentUtteranceId = string.Empty;
            lastCommittedText = string.Empty;
            lastDisplayText = string.Empty;
            lastUpdateAt = DateTimeOffset.MinValue;
            pendingCommittedTexts.Clear();
        }

        public CaptionIncrementalResult Process(CaptionUpdate update)
        {
            if (update.Sequence <= lastSequence)
                return pendingCommittedTexts.Count > 0 ? BuildResult() : CaptionIncrementalResult.None;
            lastSequence = update.Sequence;

            string incomingText = NormalizeCaption(update.Text);
            DateTimeOffset updateTimestamp = update.Timestamp == default
                ? DateTimeOffset.UtcNow
                : update.Timestamp;

            bool utteranceChanged = !string.IsNullOrWhiteSpace(currentUtteranceId) &&
                                   !string.IsNullOrWhiteSpace(update.UtteranceId) &&
                                   !string.Equals(currentUtteranceId, update.UtteranceId, StringComparison.Ordinal);

            if (utteranceChanged)
                EnqueueCommitted(CommitBuffer());

            if (!string.IsNullOrWhiteSpace(update.UtteranceId))
                currentUtteranceId = update.UtteranceId;

            if (!string.IsNullOrWhiteSpace(incomingText))
            {
                currentText = MergeWithOverlap(currentText, incomingText);
                lastUpdateAt = updateTimestamp;
            }

            if (update.IsFinal || EndsWithTerminalPunctuation(currentText))
                EnqueueCommitted(CommitBuffer());

            return BuildResult();
        }

        public CaptionIncrementalResult FlushIfIdle(DateTimeOffset now)
        {
            if (pendingCommittedTexts.Count > 0)
                return BuildResult();

            if (string.IsNullOrWhiteSpace(currentText) || lastUpdateAt == DateTimeOffset.MinValue)
                return CaptionIncrementalResult.None;

            int idleFinalizeMs = Math.Max(250, IdleFinalizeMs);
            if ((now - lastUpdateAt).TotalMilliseconds < idleFinalizeMs)
                return CaptionIncrementalResult.None;

            EnqueueCommitted(CommitBuffer());
            return BuildResult();
        }

        private CaptionIncrementalResult BuildResult()
        {
            string? committedText = pendingCommittedTexts.Count > 0
                ? pendingCommittedTexts.Dequeue()
                : null;

            string displayText = string.Empty;

            if (!string.IsNullOrWhiteSpace(committedText))
                displayText = committedText;
            else if (EnablePartial && !string.IsNullOrWhiteSpace(currentText))
                displayText = currentText;

            if (string.IsNullOrWhiteSpace(displayText))
                displayText = lastDisplayText;

            bool hasDisplayUpdate = string.CompareOrdinal(lastDisplayText, displayText) != 0;
            if (hasDisplayUpdate)
                lastDisplayText = displayText;

            return new CaptionIncrementalResult
            {
                DisplayText = displayText,
                OverlayText = displayText,
                CurrentText = currentText,
                CommittedText = committedText,
                HasDisplayUpdate = hasDisplayUpdate
            };
        }

        private string? CommitBuffer()
        {
            string candidate = NormalizeCaption(currentText);
            currentText = string.Empty;
            currentUtteranceId = string.Empty;

            if (string.IsNullOrWhiteSpace(candidate))
                return null;
            if (string.CompareOrdinal(candidate, lastCommittedText) == 0)
                return null;
            if (!string.IsNullOrWhiteSpace(lastCommittedText) &&
                TextUtil.Similarity(candidate, lastCommittedText) > 0.95)
            {
                return null;
            }

            lastCommittedText = candidate;
            return candidate;
        }

        private void EnqueueCommitted(string? committedText)
        {
            if (string.IsNullOrWhiteSpace(committedText))
                return;
            pendingCommittedTexts.Enqueue(committedText);
        }

        private static bool EndsWithTerminalPunctuation(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            int index = text.Length - 1;
            while (index >= 0 && char.IsWhiteSpace(text[index]))
                index--;
            if (index < 0)
                return false;

            return Array.IndexOf(TerminalPunctuation, text[index]) != -1;
        }

        private static string MergeWithOverlap(string previousText, string incomingText)
        {
            if (string.IsNullOrWhiteSpace(previousText))
                return incomingText;
            if (incomingText.StartsWith(previousText, StringComparison.Ordinal))
                return incomingText;
            if (previousText.StartsWith(incomingText, StringComparison.Ordinal))
                return previousText;

            if (TextUtil.Similarity(previousText, incomingText) > 0.96)
                return incomingText.Length >= previousText.Length ? incomingText : previousText;

            int overlapLength = FindOverlapLength(previousText, incomingText);
            if (overlapLength >= 3)
                return previousText + incomingText[overlapLength..];

            return incomingText;
        }

        private static int FindOverlapLength(string previousText, string incomingText)
        {
            int maxOverlap = Math.Min(previousText.Length, incomingText.Length);
            for (int overlapLength = maxOverlap; overlapLength > 0; overlapLength--)
            {
                if (previousText.EndsWith(incomingText[..overlapLength], StringComparison.Ordinal))
                    return overlapLength;
            }
            return 0;
        }

        private static string NormalizeCaption(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            text = text.Replace("\r", string.Empty).Trim();
            if (text.Contains('\n'))
                text = TextUtil.ReplaceNewlines(text, TextUtil.MEDIUM_THRESHOLD);

            text = RegexPatterns.Acronym().Replace(text, "$1$2");
            text = RegexPatterns.AcronymWithWords().Replace(text, "$1 $2");
            text = RegexPatterns.PunctuationSpace().Replace(text, "$1 ");
            text = RegexPatterns.CJPunctuationSpace().Replace(text, "$1");
            return text.Trim();
        }
    }

    public sealed class CaptionIncrementalResult
    {
        public static CaptionIncrementalResult None { get; } = new();

        public bool HasDisplayUpdate { get; init; }
        public string DisplayText { get; init; } = string.Empty;
        public string OverlayText { get; init; } = string.Empty;
        public string CurrentText { get; init; } = string.Empty;
        public string? CommittedText { get; init; }
    }
}
