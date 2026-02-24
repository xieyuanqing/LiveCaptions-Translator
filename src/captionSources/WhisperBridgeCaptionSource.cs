using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using System.IO;

namespace LiveCaptionsTranslator.captionSources
{
    public sealed class WhisperBridgeCaptionSource : ICaptionSource
    {
        private readonly Channel<CaptionUpdate> updates = Channel.CreateUnbounded<CaptionUpdate>();
        private readonly Uri bridgeUri;
        private readonly string bridgeEndpoint;
        private readonly int reconnectIntervalMs;

        private CancellationTokenSource? loopCts;
        private Task? loopTask;

        private long fallbackSequence;
        private string generatedUtteranceId = CreateUtteranceId();

        public event Action<BridgeConnectionStatus>? StatusChanged;

        public WhisperBridgeCaptionSource(string bridgeUrl, int reconnectIntervalMs)
        {
            bridgeUri = BuildBridgeUri(bridgeUrl);
            bridgeEndpoint = bridgeUri.ToString();
            this.reconnectIntervalMs = Math.Max(300, reconnectIntervalMs);
        }

        public static bool TryNormalizeBridgeUrl(
            string bridgeUrl,
            out string normalizedBridgeUrl,
            out string validationError)
        {
            try
            {
                normalizedBridgeUrl = BuildBridgeUri(bridgeUrl).ToString();
                validationError = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                normalizedBridgeUrl = string.Empty;
                validationError = ex.Message;
                return false;
            }
        }

        public static async Task<(bool Success, string ErrorMessage)> ProbeConnectionAsync(
            string bridgeUrl,
            int timeoutMs = 2500,
            CancellationToken token = default)
        {
            Uri probeUri;
            try
            {
                probeUri = BuildBridgeUri(bridgeUrl);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }

            using var probeSocket = new ClientWebSocket();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(Math.Max(800, timeoutMs));

            try
            {
                await probeSocket.ConnectAsync(probeUri, timeoutCts.Token);
                if (probeSocket.State == WebSocketState.Open)
                {
                    await probeSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "probe",
                        CancellationToken.None);
                }
                return (true, string.Empty);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                return (false, "Connection probe timed out.");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public ChannelReader<CaptionUpdate> Updates => updates.Reader;

        public Task StartAsync(CancellationToken token = default)
        {
            if (loopTask != null && !loopTask.IsCompleted)
                return Task.CompletedTask;

            loopCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            loopTask = Task.Run(() => ReceiveLoopAsync(loopCts.Token), CancellationToken.None);
            EmitStatus(BridgeConnectionState.Connecting, "Bridge source starting.", 0);
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken token = default)
        {
            if (loopCts != null)
            {
                loopCts.Cancel();
                if (loopTask != null)
                {
                    try
                    {
                        await loopTask;
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }

                loopCts.Dispose();
                loopCts = null;
                loopTask = null;
            }

            EmitStatus(BridgeConnectionState.Stopped, "Bridge source stopped.", 0);
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            int reconnectAttempt = 0;

            while (!token.IsCancellationRequested)
            {
                using var socket = new ClientWebSocket();

                try
                {
                    reconnectAttempt++;
                    EmitStatus(
                        BridgeConnectionState.Connecting,
                        $"Connecting to {bridgeEndpoint}",
                        reconnectAttempt);

                    await socket.ConnectAsync(bridgeUri, token);
                    EmitStatus(
                        BridgeConnectionState.Connected,
                        $"Connected to {bridgeEndpoint}",
                        reconnectAttempt);

                    reconnectAttempt = 0;
                    await ReceiveMessagesAsync(socket, token);

                    if (!token.IsCancellationRequested)
                    {
                        EmitStatus(
                            BridgeConnectionState.Reconnecting,
                            $"Bridge disconnected. Retrying in {reconnectIntervalMs} ms.",
                            reconnectAttempt + 1);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested)
                    {
                        EmitStatus(
                            BridgeConnectionState.Reconnecting,
                            $"Connection error: {ex.Message}. Retrying in {reconnectIntervalMs} ms.",
                            reconnectAttempt + 1);
                    }
                }
                finally
                {
                    if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                    {
                        try
                        {
                            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "shutdown", CancellationToken.None);
                        }
                        catch
                        {
                        }
                    }
                }

                if (!token.IsCancellationRequested)
                    await Task.Delay(reconnectIntervalMs, token);
            }

            if (!token.IsCancellationRequested)
                EmitStatus(BridgeConnectionState.Stopped, "Bridge source stopped.", 0);
        }

        private async Task ReceiveMessagesAsync(ClientWebSocket socket, CancellationToken token)
        {
            while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                string payload = await ReceiveTextMessageAsync(socket, token);
                if (string.IsNullOrWhiteSpace(payload))
                    continue;

                var parsedUpdates = WhisperBridgeMessageParser.Parse(
                    payload,
                    ref fallbackSequence,
                    CaptionSourceKinds.WhisperBridge);

                foreach (CaptionUpdate parsedUpdate in parsedUpdates)
                {
                    CaptionUpdate normalized = NormalizeUpdate(parsedUpdate);
                    if (string.IsNullOrWhiteSpace(normalized.Text) && !normalized.IsFinal)
                        continue;
                    await updates.Writer.WriteAsync(normalized, token);
                }
            }
        }

        private static async Task<string> ReceiveTextMessageAsync(ClientWebSocket socket, CancellationToken token)
        {
            byte[] buffer = new byte[8192];
            using var ms = new MemoryStream();

            while (true)
            {
                var segment = new ArraySegment<byte>(buffer);
                WebSocketReceiveResult result = await socket.ReceiveAsync(segment, token);

                if (result.MessageType == WebSocketMessageType.Close)
                    return string.Empty;

                if (result.MessageType == WebSocketMessageType.Binary)
                    continue;

                if (result.Count > 0)
                    ms.Write(buffer, 0, result.Count);

                if (result.EndOfMessage)
                    break;
            }

            if (ms.Length == 0)
                return string.Empty;
            return Encoding.UTF8.GetString(ms.ToArray());
        }

        private CaptionUpdate NormalizeUpdate(CaptionUpdate update)
        {
            string source = string.IsNullOrWhiteSpace(update.Source)
                ? CaptionSourceKinds.WhisperBridge
                : update.Source.Trim();

            DateTimeOffset timestamp = update.Timestamp == default
                ? DateTimeOffset.UtcNow
                : update.Timestamp;

            string utteranceId = update.UtteranceId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(utteranceId))
            {
                utteranceId = generatedUtteranceId;
            }
            else
            {
                generatedUtteranceId = utteranceId;
            }

            if (update.IsFinal)
                generatedUtteranceId = CreateUtteranceId();

            return new CaptionUpdate
            {
                Text = update.Text.Trim(),
                IsFinal = update.IsFinal,
                Sequence = update.Sequence,
                Source = source,
                Timestamp = timestamp,
                UtteranceId = utteranceId
            };
        }

        private static Uri BuildBridgeUri(string bridgeUrl)
        {
            string normalized = string.IsNullOrWhiteSpace(bridgeUrl)
                ? "ws://127.0.0.1:8765/captions"
                : bridgeUrl.Trim();

            if (!normalized.Contains("://", StringComparison.Ordinal))
                normalized = "ws://" + normalized;

            if (!Uri.TryCreate(normalized, UriKind.Absolute, out Uri? uri) ||
                (uri.Scheme != "ws" && uri.Scheme != "wss"))
            {
                throw new ArgumentException($"Invalid bridge URL: {bridgeUrl}");
            }

            return uri;
        }

        private static string CreateUtteranceId()
        {
            return "bridge-" + Guid.NewGuid().ToString("N");
        }

        private void EmitStatus(BridgeConnectionState state, string message, int attempt)
        {
            try
            {
                StatusChanged?.Invoke(new BridgeConnectionStatus
                {
                    State = state,
                    Endpoint = bridgeEndpoint,
                    Message = message,
                    Attempt = Math.Max(0, attempt),
                    UpdatedAt = DateTimeOffset.UtcNow
                });
            }
            catch
            {
            }
        }
    }
}
