namespace LiveCaptionsTranslator.captionSources
{
    public sealed class CaptionUpdate
    {
        public string Text { get; init; } = string.Empty;
        public bool IsFinal { get; init; }
        public long Sequence { get; init; }
        public string Source { get; init; } = string.Empty;
        public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
        public string UtteranceId { get; init; } = string.Empty;
    }
}
