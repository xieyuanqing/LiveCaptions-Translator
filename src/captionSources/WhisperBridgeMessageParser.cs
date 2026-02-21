using System.Globalization;
using System.Text;
using System.Text.Json;

namespace LiveCaptionsTranslator.captionSources
{
    public static class WhisperBridgeMessageParser
    {
        private static readonly string[] TextKeys =
        [
            "text",
            "caption",
            "transcript",
            "content",
            "message"
        ];

        private static readonly string[] FinalKeys =
        [
            "isFinal",
            "is_final",
            "final",
            "done",
            "completed"
        ];

        private static readonly string[] SequenceKeys =
        [
            "sequence",
            "seq",
            "index",
            "order"
        ];

        private static readonly string[] SourceKeys =
        [
            "source",
            "origin",
            "engine"
        ];

        private static readonly string[] TimestampKeys =
        [
            "timestamp",
            "ts",
            "time",
            "createdAt",
            "created_at"
        ];

        private static readonly string[] UtteranceKeys =
        [
            "utteranceId",
            "utterance_id",
            "segmentId",
            "segment_id",
            "utt",
            "id"
        ];

        private static readonly string[] StatusKeys =
        [
            "status",
            "type",
            "event"
        ];

        private static readonly string[] LegacyBufferTranscriptionKeys =
        [
            "buffer_transcription",
            "bufferTranscription"
        ];

        private static readonly string[] LegacyBufferDiarizationKeys =
        [
            "buffer_diarization",
            "bufferDiarization"
        ];

        public static IReadOnlyList<CaptionUpdate> Parse(
            string payload,
            ref long fallbackSequence,
            string defaultSource)
        {
            var updates = new List<CaptionUpdate>();
            if (string.IsNullOrWhiteSpace(payload))
                return updates;

            try
            {
                using JsonDocument json = JsonDocument.Parse(payload);
                ParseElement(json.RootElement, updates, ref fallbackSequence, defaultSource);
            }
            catch (JsonException)
            {
                fallbackSequence++;
                updates.Add(new CaptionUpdate
                {
                    Text = payload.Trim(),
                    IsFinal = false,
                    Sequence = fallbackSequence,
                    Source = defaultSource,
                    Timestamp = DateTimeOffset.UtcNow
                });
            }

            return updates;
        }

        private static void ParseElement(
            JsonElement element,
            List<CaptionUpdate> updates,
            ref long fallbackSequence,
            string defaultSource)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Array:
                    foreach (JsonElement item in element.EnumerateArray())
                        ParseElement(item, updates, ref fallbackSequence, defaultSource);
                    break;

                case JsonValueKind.Object:
                    ParseObject(element, updates, ref fallbackSequence, defaultSource);
                    break;

                case JsonValueKind.String:
                {
                    string text = element.GetString()?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(text))
                        return;

                    fallbackSequence++;
                    updates.Add(new CaptionUpdate
                    {
                        Text = text,
                        IsFinal = false,
                        Sequence = fallbackSequence,
                        Source = defaultSource,
                        Timestamp = DateTimeOffset.UtcNow
                    });
                    break;
                }
            }
        }

        private static void ParseObject(
            JsonElement obj,
            List<CaptionUpdate> updates,
            ref long fallbackSequence,
            string defaultSource)
        {
            DateTimeOffset timestamp = TryReadTimestamp(obj, TimestampKeys, out DateTimeOffset parsedTimestamp)
                ? parsedTimestamp
                : DateTimeOffset.UtcNow;

            string source = TryReadString(obj, SourceKeys, out string sourceValue)
                ? sourceValue
                : defaultSource;
            source = string.IsNullOrWhiteSpace(source) ? defaultSource : source;

            if (TryReadString(obj, ["type"], out string messageType) &&
                string.Equals(messageType.Trim(), "ready_to_stop", StringComparison.OrdinalIgnoreCase))
            {
                fallbackSequence++;
                updates.Add(new CaptionUpdate
                {
                    Text = string.Empty,
                    IsFinal = true,
                    Sequence = fallbackSequence,
                    Source = source,
                    Timestamp = timestamp,
                    UtteranceId = "wlk-stop"
                });
                return;
            }

            if (TryReadLegacySnapshot(obj, updates, ref fallbackSequence, source, timestamp))
                return;

            if (TryReadNewApiSegments(obj, updates, ref fallbackSequence, source, timestamp))
                return;

            string text = TryReadString(obj, TextKeys, out string value) ? value : string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                if (TryGetPropertyIgnoreCase(obj, "data", out JsonElement nested))
                {
                    ParseElement(nested, updates, ref fallbackSequence, defaultSource);
                    return;
                }
                if (TryGetPropertyIgnoreCase(obj, "payload", out nested))
                {
                    ParseElement(nested, updates, ref fallbackSequence, defaultSource);
                    return;
                }
                return;
            }

            bool isFinal = false;
            if (TryReadBool(obj, FinalKeys, out bool finalValue))
                isFinal = finalValue;
            else if (TryReadString(obj, StatusKeys, out string statusValue))
                isFinal = IsFinalStatus(statusValue);

            long sequence;
            if (TryReadLong(obj, SequenceKeys, out long parsedSequence) && parsedSequence > 0)
            {
                sequence = parsedSequence;
                fallbackSequence = Math.Max(fallbackSequence, parsedSequence);
            }
            else
            {
                fallbackSequence++;
                sequence = fallbackSequence;
            }

            string utteranceId = TryReadString(obj, UtteranceKeys, out string utteranceValue)
                ? utteranceValue
                : string.Empty;

            updates.Add(new CaptionUpdate
            {
                Text = text.Trim(),
                IsFinal = isFinal,
                Sequence = sequence,
                Source = string.IsNullOrWhiteSpace(source) ? defaultSource : source,
                Timestamp = timestamp,
                UtteranceId = utteranceId.Trim()
            });
        }

        private static bool TryReadLegacySnapshot(
            JsonElement obj,
            List<CaptionUpdate> updates,
            ref long fallbackSequence,
            string source,
            DateTimeOffset timestamp)
        {
            if (!TryGetPropertyIgnoreCase(obj, "lines", out JsonElement lines) ||
                lines.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            string lastLineText = string.Empty;
            string utteranceId = string.Empty;

            foreach (JsonElement line in lines.EnumerateArray())
            {
                if (line.ValueKind != JsonValueKind.Object)
                    continue;

                long speaker = 0;
                if (TryReadLong(line, ["speaker"], out long parsedSpeaker))
                    speaker = parsedSpeaker;
                if (speaker == -2)
                    continue;

                if (!TryReadString(line, ["text"], out string lineText) || string.IsNullOrWhiteSpace(lineText))
                    continue;

                lastLineText = lineText;

                if (TryBuildLegacyUtteranceId(line, out string parsedUtteranceId))
                    utteranceId = parsedUtteranceId;
            }

            string bufferDiarization =
                TryReadString(obj, LegacyBufferDiarizationKeys, out string diarizationValue)
                    ? diarizationValue
                    : string.Empty;
            string bufferTranscription =
                TryReadString(obj, LegacyBufferTranscriptionKeys, out string transcriptionValue)
                    ? transcriptionValue
                    : string.Empty;

            string text = (lastLineText + bufferDiarization + bufferTranscription).Trim();
            if (string.IsNullOrWhiteSpace(text))
                return false;

            fallbackSequence++;
            updates.Add(new CaptionUpdate
            {
                Text = text,
                IsFinal = false,
                Sequence = fallbackSequence,
                Source = source,
                Timestamp = timestamp,
                UtteranceId = string.IsNullOrWhiteSpace(utteranceId)
                    ? "wlk-legacy"
                    : utteranceId
            });

            return true;
        }

        private static bool TryReadNewApiSegments(
            JsonElement obj,
            List<CaptionUpdate> updates,
            ref long fallbackSequence,
            string source,
            DateTimeOffset timestamp)
        {
            if (!TryGetPropertyIgnoreCase(obj, "segments", out JsonElement segments) ||
                segments.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            bool messageIsFinal = false;
            if (TryReadBool(obj, FinalKeys, out bool explicitMessageFinal))
                messageIsFinal = explicitMessageFinal;
            else if (TryReadString(obj, StatusKeys, out string statusText))
                messageIsFinal = IsFinalStatus(statusText);

            int updatesBefore = updates.Count;

            foreach (JsonElement segment in segments.EnumerateArray())
            {
                if (segment.ValueKind != JsonValueKind.Object)
                    continue;

                string text = TryReadString(segment, ["text"], out string segmentText)
                    ? segmentText
                    : string.Empty;

                if (TryGetPropertyIgnoreCase(segment, "buffer", out JsonElement buffer) &&
                    buffer.ValueKind == JsonValueKind.Object)
                {
                    if (TryReadString(buffer, ["diarization"], out string bufferDiarization))
                        text += bufferDiarization;
                    if (TryReadString(buffer, ["transcription"], out string bufferTranscription))
                        text += bufferTranscription;
                }

                text = text.Trim();
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                string utteranceId = string.Empty;
                if (TryReadLong(segment, ["id"], out long segmentId))
                    utteranceId = "wlk-segment-" + segmentId;

                bool segmentIsFinal = messageIsFinal;
                if (TryReadBool(segment, FinalKeys, out bool explicitSegmentFinal))
                    segmentIsFinal = explicitSegmentFinal;
                else if (TryReadString(segment, StatusKeys, out string segmentStatusText))
                    segmentIsFinal = IsFinalStatus(segmentStatusText) || messageIsFinal;

                fallbackSequence++;
                updates.Add(new CaptionUpdate
                {
                    Text = text,
                    IsFinal = segmentIsFinal,
                    Sequence = fallbackSequence,
                    Source = source,
                    Timestamp = timestamp,
                    UtteranceId = utteranceId
                });
            }

            return updates.Count > updatesBefore;
        }

        private static bool TryBuildLegacyUtteranceId(JsonElement line, out string utteranceId)
        {
            if (TryReadString(line, ["start", "start_speaker", "id"], out string idText))
            {
                string normalized = NormalizeIdentifier(idText);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    utteranceId = "wlk-line-" + normalized;
                    return true;
                }
            }

            if (TryReadLong(line, ["start", "start_speaker", "id"], out long idValue))
            {
                utteranceId = "wlk-line-" + idValue.ToString(CultureInfo.InvariantCulture);
                return true;
            }

            utteranceId = string.Empty;
            return false;
        }

        private static string NormalizeIdentifier(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            var sb = new StringBuilder(raw.Length);
            bool lastIsDash = false;
            foreach (char ch in raw.Trim())
            {
                if (char.IsLetterOrDigit(ch))
                {
                    sb.Append(ch);
                    lastIsDash = false;
                    continue;
                }

                if (!lastIsDash)
                {
                    sb.Append('-');
                    lastIsDash = true;
                }
            }

            return sb.ToString().Trim('-');
        }

        private static bool TryReadString(JsonElement obj, IEnumerable<string> keys, out string value)
        {
            foreach (string key in keys)
            {
                if (!TryGetPropertyIgnoreCase(obj, key, out JsonElement prop))
                    continue;

                switch (prop.ValueKind)
                {
                    case JsonValueKind.String:
                        value = prop.GetString() ?? string.Empty;
                        return !string.IsNullOrWhiteSpace(value);

                    case JsonValueKind.Number:
                    case JsonValueKind.True:
                    case JsonValueKind.False:
                        value = prop.ToString();
                        return !string.IsNullOrWhiteSpace(value);
                }
            }

            value = string.Empty;
            return false;
        }

        private static bool TryReadBool(JsonElement obj, IEnumerable<string> keys, out bool value)
        {
            foreach (string key in keys)
            {
                if (!TryGetPropertyIgnoreCase(obj, key, out JsonElement prop))
                    continue;

                switch (prop.ValueKind)
                {
                    case JsonValueKind.True:
                        value = true;
                        return true;

                    case JsonValueKind.False:
                        value = false;
                        return true;

                    case JsonValueKind.Number:
                        if (prop.TryGetInt32(out int intValue))
                        {
                            value = intValue != 0;
                            return true;
                        }
                        break;

                    case JsonValueKind.String:
                    {
                        string text = prop.GetString()?.Trim() ?? string.Empty;
                        if (bool.TryParse(text, out bool boolValue))
                        {
                            value = boolValue;
                            return true;
                        }
                        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedInt))
                        {
                            value = parsedInt != 0;
                            return true;
                        }
                        if (IsFinalStatus(text))
                        {
                            value = true;
                            return true;
                        }
                        break;
                    }
                }
            }

            value = false;
            return false;
        }

        private static bool TryReadLong(JsonElement obj, IEnumerable<string> keys, out long value)
        {
            foreach (string key in keys)
            {
                if (!TryGetPropertyIgnoreCase(obj, key, out JsonElement prop))
                    continue;

                switch (prop.ValueKind)
                {
                    case JsonValueKind.Number:
                        if (prop.TryGetInt64(out long parsedLong))
                        {
                            value = parsedLong;
                            return true;
                        }
                        break;

                    case JsonValueKind.String:
                    {
                        string text = prop.GetString()?.Trim() ?? string.Empty;
                        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedLong))
                        {
                            value = parsedLong;
                            return true;
                        }
                        break;
                    }
                }
            }

            value = 0;
            return false;
        }

        private static bool TryReadTimestamp(JsonElement obj, IEnumerable<string> keys, out DateTimeOffset value)
        {
            foreach (string key in keys)
            {
                if (!TryGetPropertyIgnoreCase(obj, key, out JsonElement prop))
                    continue;

                switch (prop.ValueKind)
                {
                    case JsonValueKind.String:
                    {
                        string text = prop.GetString()?.Trim() ?? string.Empty;
                        if (DateTimeOffset.TryParse(
                                text,
                                CultureInfo.InvariantCulture,
                                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                                out DateTimeOffset parsedDateTimeOffset))
                        {
                            value = parsedDateTimeOffset;
                            return true;
                        }
                        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long unixValue))
                        {
                            value = UnixToDateTimeOffset(unixValue);
                            return true;
                        }
                        break;
                    }

                    case JsonValueKind.Number:
                        if (prop.TryGetInt64(out long unixNumber))
                        {
                            value = UnixToDateTimeOffset(unixNumber);
                            return true;
                        }
                        break;
                }
            }

            value = default;
            return false;
        }

        private static DateTimeOffset UnixToDateTimeOffset(long unixValue)
        {
            if (unixValue > 10_000_000_000)
                return DateTimeOffset.FromUnixTimeMilliseconds(unixValue);
            return DateTimeOffset.FromUnixTimeSeconds(unixValue);
        }

        private static bool IsFinalStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return false;

            string normalized = status.Trim().ToLowerInvariant();
            return normalized is "final" or "done" or "completed" or "complete" or "end" or "eos";
        }

        private static bool TryGetPropertyIgnoreCase(JsonElement obj, string targetName, out JsonElement value)
        {
            foreach (JsonProperty property in obj.EnumerateObject())
            {
                if (string.Equals(property.Name, targetName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }
    }
}
