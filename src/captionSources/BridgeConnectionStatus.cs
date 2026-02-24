namespace LiveCaptionsTranslator.captionSources
{
    public enum BridgeConnectionState
    {
        Idle,
        Connecting,
        Connected,
        Reconnecting,
        Stopped,
        Error
    }

    public sealed class BridgeConnectionStatus
    {
        public BridgeConnectionState State { get; init; } = BridgeConnectionState.Idle;
        public string Endpoint { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public int Attempt { get; init; }
        public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;

        public bool IsConnected => State == BridgeConnectionState.Connected;
    }
}
