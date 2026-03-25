using System.Text;
using System.Text.Json;

using LiveCaptionsTranslator.captionSources;

const int DefaultIdleFinalizeMs = 600;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/BridgeReplayLogger -- <input-jsonl> [output-dir] [max-seconds]");
    return 2;
}

string inputPath = Path.GetFullPath(args[0]);
if (!File.Exists(inputPath))
{
    Console.Error.WriteLine($"Input file not found: {inputPath}");
    return 2;
}

string outputDir = args.Length >= 2 && !string.IsNullOrWhiteSpace(args[1])
    ? Path.GetFullPath(args[1])
    : Path.Combine(
        Path.GetDirectoryName(inputPath) ?? Environment.CurrentDirectory,
        "bridge_replay_logs",
        DateTime.Now.ToString("yyyyMMdd_HHmmss"));

double maxSeconds = 0;
if (args.Length >= 3 &&
    (!double.TryParse(args[2], out maxSeconds) || maxSeconds <= 0))
{
    Console.Error.WriteLine("max-seconds must be a positive number.");
    return 2;
}

Directory.CreateDirectory(outputDir);

string payloadLogPath = Path.Combine(outputDir, "payload_frames.jsonl");
string updatesLogPath = Path.Combine(outputDir, "caption_updates.jsonl");
string timelineEventsPath = Path.Combine(outputDir, "timeline_events.txt");
string commitsTimelinePath = Path.Combine(outputDir, "timeline_commits.txt");
string inputHeadPath = Path.Combine(outputDir, "input_head.jsonl");
string summaryJsonPath = Path.Combine(outputDir, "summary.json");
string summaryMdPath = Path.Combine(outputDir, "summary.md");

var replay = new ReplayAnalyzer
{
    InputPath = inputPath,
    MaxSeconds = maxSeconds,
    IdleFinalizeMs = DefaultIdleFinalizeMs,
};

ReplaySummary summary = await replay.RunAsync(payloadLogPath, updatesLogPath);

WriteInputHead(inputPath, inputHeadPath, 20);
TimelineStats timelineStats = WriteTextTimelines(updatesLogPath, timelineEventsPath, commitsTimelinePath);

WriteSummaryFiles(summary, summaryJsonPath, summaryMdPath);

Console.WriteLine(JsonSerializer.Serialize(new
{
    input = summary.InputPath,
    output_dir = summary.OutputDir,
    max_seconds = summary.MaxSeconds,
    payload_frames = summary.PayloadFrames,
    transcript_frames = summary.TranscriptFrames,
    parser_updates = summary.ParserUpdates,
    committed_outputs = summary.CommittedOutputs,
    display_updates = summary.DisplayUpdates,
    saw_ready_to_stop = summary.SawReadyToStop,
    timeline_event_lines = timelineStats.EventLines,
    timeline_commit_lines = timelineStats.CommitLines,
    payload_log = payloadLogPath,
    updates_log = updatesLogPath,
    timeline_events = timelineEventsPath,
    timeline_commits = commitsTimelinePath,
    input_head = inputHeadPath,
    summary_json = summaryJsonPath,
    summary_md = summaryMdPath,
}, new JsonSerializerOptions { WriteIndented = true }));

return 0;


static void WriteSummaryFiles(ReplaySummary summary, string summaryJsonPath, string summaryMdPath)
{
    var jsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
    };

    File.WriteAllText(summaryJsonPath, JsonSerializer.Serialize(summary, jsonOptions), new UTF8Encoding(false));

    var sb = new StringBuilder();
    sb.AppendLine("# Bridge Replay Log Summary");
    sb.AppendLine();
    sb.AppendLine($"- Input: `{summary.InputPath}`");
    sb.AppendLine($"- Output Dir: `{summary.OutputDir}`");
    sb.AppendLine($"- Max Seconds Filter: {(summary.MaxSeconds > 0 ? summary.MaxSeconds.ToString("0.###") : "all")}");
    sb.AppendLine($"- Generated At: `{summary.GeneratedAt:o}`");
    sb.AppendLine();
    sb.AppendLine("## Counts");
    sb.AppendLine($"- Total lines: {summary.TotalLines}");
    sb.AppendLine($"- JSON parse failures: {summary.JsonParseFailures}");
    sb.AppendLine($"- Payload frames: {summary.PayloadFrames}");
    sb.AppendLine($"- Transcript frames: {summary.TranscriptFrames}");
    sb.AppendLine($"- ready_to_stop frames: {summary.ReadyToStopFrames}");
    sb.AppendLine($"- Parser updates: {summary.ParserUpdates}");
    sb.AppendLine($"- Display updates: {summary.DisplayUpdates}");
    sb.AppendLine($"- Committed outputs: {summary.CommittedOutputs}");
    sb.AppendLine($"- Saw ready_to_stop update: {summary.SawReadyToStop}");
    sb.AppendLine();
    sb.AppendLine("## Text Analysis Files");
    sb.AppendLine("- `input_head.jsonl` (first 20 source lines, format reference)");
    sb.AppendLine("- `timeline_events.txt` (timestamped display/final/commit events)");
    sb.AppendLine("- `timeline_commits.txt` (timestamped committed captions only)");
    sb.AppendLine();
    sb.AppendLine("## Samples");
    if (summary.CommittedSamples.Count == 0)
    {
        sb.AppendLine("- (no committed samples)");
    }
    else
    {
        foreach (string sample in summary.CommittedSamples)
            sb.AppendLine($"- {sample}");
    }

    sb.AppendLine();
    sb.AppendLine("## Top Repeated Committed Texts");
    if (summary.TopRepeatedCommitted.Count == 0)
    {
        sb.AppendLine("- (none)");
    }
    else
    {
        foreach (RepeatedItem repeated in summary.TopRepeatedCommitted)
            sb.AppendLine($"- ({repeated.Count}) {repeated.Text}");
    }

    File.WriteAllText(summaryMdPath, sb.ToString(), new UTF8Encoding(false));
}


static void WriteInputHead(string inputPath, string outputPath, int maxLines)
{
    int count = 0;
    using var writer = new StreamWriter(outputPath, append: false, new UTF8Encoding(false));
    foreach (string line in File.ReadLines(inputPath))
    {
        if (string.IsNullOrWhiteSpace(line))
            continue;

        count++;
        writer.WriteLine(line);
        if (count >= Math.Max(1, maxLines))
            break;
    }
}


static TimelineStats WriteTextTimelines(string updatesLogPath, string eventsPath, string commitsPath)
{
    int eventLines = 0;
    int commitLines = 0;

    using var eventsWriter = new StreamWriter(eventsPath, append: false, new UTF8Encoding(false));
    using var commitsWriter = new StreamWriter(commitsPath, append: false, new UTF8Encoding(false));

    foreach (string line in File.ReadLines(updatesLogPath))
    {
        if (string.IsNullOrWhiteSpace(line))
            continue;

        using JsonDocument doc = JsonDocument.Parse(line);
        JsonElement root = doc.RootElement;

        int frameIndex = root.TryGetProperty("frame_index", out JsonElement frameElement) && frameElement.TryGetInt32(out int f)
            ? f
            : -1;
        double tSec = root.TryGetProperty("t_sec", out JsonElement timeElement) && timeElement.TryGetDouble(out double t)
            ? t
            : -1;

        if (!root.TryGetProperty("update", out JsonElement update))
            continue;
        if (!root.TryGetProperty("aggregator", out JsonElement aggregator))
            continue;

        long sequence = update.TryGetProperty("sequence", out JsonElement seqElement) && seqElement.TryGetInt64(out long seq)
            ? seq
            : -1;
        bool isFinal = update.TryGetProperty("is_final", out JsonElement finalElement) && finalElement.GetBoolean();
        string utteranceId = update.TryGetProperty("utterance_id", out JsonElement uttElement)
            ? (uttElement.GetString() ?? string.Empty)
            : string.Empty;
        string text = update.TryGetProperty("text", out JsonElement textElement)
            ? (textElement.GetString() ?? string.Empty)
            : string.Empty;

        bool hasDisplay = aggregator.TryGetProperty("has_display_update", out JsonElement hasDisplayElement) && hasDisplayElement.GetBoolean();
        string displayText = aggregator.TryGetProperty("display_text", out JsonElement displayElement)
            ? (displayElement.GetString() ?? string.Empty)
            : string.Empty;
        string committedText = aggregator.TryGetProperty("committed_text", out JsonElement committedElement)
            ? (committedElement.GetString() ?? string.Empty)
            : string.Empty;

        bool hasCommit = !string.IsNullOrWhiteSpace(committedText);
        bool isStop = isFinal && string.Equals(utteranceId, "wlk-stop", StringComparison.Ordinal);
        bool shouldWriteEvent = hasDisplay || hasCommit || isFinal || isStop;
        if (!shouldWriteEvent)
            continue;

        var parts = new List<string>
        {
            FormatTSec(tSec),
            frameIndex >= 0 ? $"frame={frameIndex}" : "frame=tail",
            sequence >= 0 ? $"seq={sequence}" : "seq=?",
        };

        if (isFinal)
            parts.Add("final=1");
        if (isStop)
            parts.Add("stop=1");

        if (!string.IsNullOrWhiteSpace(utteranceId) && !string.Equals(utteranceId, "wlk-legacy", StringComparison.Ordinal))
            parts.Add($"utt={ClipText(utteranceId, 48)}");

        if (!string.IsNullOrWhiteSpace(text))
            parts.Add($"text={ClipText(text, 120)}");
        if (hasDisplay)
            parts.Add($"display={ClipText(displayText, 120)}");
        if (hasCommit)
            parts.Add($"commit={ClipText(committedText, 160)}");

        eventsWriter.WriteLine(string.Join(" | ", parts));
        eventLines++;

        if (hasCommit)
        {
            commitsWriter.WriteLine(
                $"{FormatTSec(tSec)} | seq={sequence} | frame={(frameIndex >= 0 ? frameIndex : -1)} | commit={ClipText(committedText, 200)}");
            commitLines++;
        }
    }

    return new TimelineStats(eventLines, commitLines);
}


static string FormatTSec(double tSec)
{
    if (tSec < 0)
        return "TAIL";
    return $"T+{tSec,8:0.000}s";
}


static string ClipText(string? value, int maxChars)
{
    if (string.IsNullOrWhiteSpace(value))
        return "\"\"";

    string cleaned = value
        .Replace("\r", " ")
        .Replace("\n", " ")
        .Trim();

    if (cleaned.Length <= Math.Max(8, maxChars))
        return $"\"{cleaned}\"";

    int limit = Math.Max(8, maxChars);
    int overflow = cleaned.Length - limit;
    string clipped = cleaned[..limit];
    return $"\"{clipped}...(+{overflow}c)\"";
}


file sealed class ReplayAnalyzer
{
    public required string InputPath { get; init; }
    public required int IdleFinalizeMs { get; init; }
    public required double MaxSeconds { get; init; }

    public async Task<ReplaySummary> RunAsync(string payloadLogPath, string updatesLogPath)
    {
        int totalLines = 0;
        int jsonParseFailures = 0;
        int payloadFrames = 0;
        int transcriptFrames = 0;
        int readyToStopFrames = 0;
        int parserUpdates = 0;
        int displayUpdates = 0;
        int committedOutputs = 0;
        bool sawReadyToStop = false;

        long fallbackSequence = 0;
        var aggregator = new CaptionIncrementalAggregator
        {
            EnablePartial = true,
            IdleFinalizeMs = IdleFinalizeMs,
        };

        var committedSamples = new List<string>();
        var committedCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        await using var payloadWriter = new StreamWriter(payloadLogPath, append: false, new UTF8Encoding(false));
        await using var updatesWriter = new StreamWriter(updatesLogPath, append: false, new UTF8Encoding(false));

        int frameIndex = 0;
        foreach (string line in File.ReadLines(InputPath))
        {
            totalLines++;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            JsonElement root;
            try
            {
                using JsonDocument lineDoc = JsonDocument.Parse(line);
                root = lineDoc.RootElement.Clone();
            }
            catch (JsonException)
            {
                jsonParseFailures++;
                continue;
            }

            frameIndex++;
            if (root.ValueKind != JsonValueKind.Object)
                continue;

            double tSec = TryGetDouble(root, "t_sec");
            if (MaxSeconds > 0 && tSec > MaxSeconds)
                continue;

            JsonElement payload = root;
            if (TryGetPropertyIgnoreCase(root, "payload", out JsonElement nestedPayload) &&
                nestedPayload.ValueKind == JsonValueKind.Object)
            {
                payload = nestedPayload;
            }

            if (payload.ValueKind != JsonValueKind.Object)
                continue;

            payloadFrames++;

            bool isTranscript = TryGetPropertyIgnoreCase(payload, "status", out _) &&
                                TryGetPropertyIgnoreCase(payload, "lines", out _);
            if (isTranscript)
                transcriptFrames++;

            bool isReadyToStop = TryGetString(payload, "type", out string payloadType) &&
                                 string.Equals(payloadType, "ready_to_stop", StringComparison.OrdinalIgnoreCase);
            if (isReadyToStop)
                readyToStopFrames++;

            await payloadWriter.WriteLineAsync(JsonSerializer.Serialize(new
            {
                frame_index = frameIndex,
                t_sec = Math.Round(tSec, 4),
                is_transcript = isTranscript,
                payload = payload,
            }));

            string payloadJson = payload.GetRawText();
            IReadOnlyList<CaptionUpdate> updates = WhisperBridgeMessageParser.Parse(
                payloadJson,
                ref fallbackSequence,
                CaptionSourceKinds.WhisperBridge);

            foreach (CaptionUpdate update in updates)
            {
                parserUpdates++;
                CaptionIncrementalResult result = aggregator.Process(update);

                if (result.HasDisplayUpdate)
                    displayUpdates++;

                string committedText = result.CommittedText ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(committedText))
                {
                    committedOutputs++;
                    if (committedSamples.Count < 20)
                        committedSamples.Add(committedText);
                    if (committedCounts.TryGetValue(committedText, out int existing))
                        committedCounts[committedText] = existing + 1;
                    else
                        committedCounts[committedText] = 1;
                }

                if (update.IsFinal && string.Equals(update.UtteranceId, "wlk-stop", StringComparison.Ordinal))
                    sawReadyToStop = true;

                await updatesWriter.WriteLineAsync(JsonSerializer.Serialize(new
                {
                    frame_index = frameIndex,
                    t_sec = Math.Round(tSec, 4),
                    update = new
                    {
                        text = update.Text,
                        is_final = update.IsFinal,
                        sequence = update.Sequence,
                        utterance_id = update.UtteranceId,
                        source = update.Source,
                        timestamp = update.Timestamp,
                    },
                    aggregator = new
                    {
                        has_display_update = result.HasDisplayUpdate,
                        display_text = result.DisplayText,
                        current_text = result.CurrentText,
                        committed_text = committedText,
                    },
                }));
            }
        }

        CaptionIncrementalResult tail = aggregator.FlushIfIdle(DateTimeOffset.UtcNow.AddMinutes(1));
        if (!string.IsNullOrWhiteSpace(tail.CommittedText))
        {
            committedOutputs++;
            if (committedSamples.Count < 20)
                committedSamples.Add(tail.CommittedText);
            if (committedCounts.TryGetValue(tail.CommittedText, out int existing))
                committedCounts[tail.CommittedText] = existing + 1;
            else
                committedCounts[tail.CommittedText] = 1;

            await updatesWriter.WriteLineAsync(JsonSerializer.Serialize(new
            {
                frame_index = -1,
                t_sec = -1,
                update = new
                {
                    text = "",
                    is_final = true,
                    sequence = fallbackSequence,
                    utterance_id = "flush_if_idle",
                    source = "replay_logger",
                    timestamp = DateTimeOffset.UtcNow,
                },
                aggregator = new
                {
                    has_display_update = tail.HasDisplayUpdate,
                    display_text = tail.DisplayText,
                    current_text = tail.CurrentText,
                    committed_text = tail.CommittedText,
                },
            }));
        }

        List<RepeatedItem> topRepeated = committedCounts
            .OrderByDescending(item => item.Value)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .Take(10)
            .Select(item => new RepeatedItem { Text = item.Key, Count = item.Value })
            .ToList();

        return new ReplaySummary
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            InputPath = InputPath,
            OutputDir = Path.GetDirectoryName(payloadLogPath) ?? Environment.CurrentDirectory,
            MaxSeconds = MaxSeconds,
            TotalLines = totalLines,
            JsonParseFailures = jsonParseFailures,
            PayloadFrames = payloadFrames,
            TranscriptFrames = transcriptFrames,
            ReadyToStopFrames = readyToStopFrames,
            ParserUpdates = parserUpdates,
            DisplayUpdates = displayUpdates,
            CommittedOutputs = committedOutputs,
            SawReadyToStop = sawReadyToStop,
            CommittedSamples = committedSamples,
            TopRepeatedCommitted = topRepeated,
        };
    }

    private static double TryGetDouble(JsonElement obj, string key)
    {
        if (!TryGetPropertyIgnoreCase(obj, key, out JsonElement value))
            return 0;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number))
            return number;

        if (value.ValueKind == JsonValueKind.String &&
            double.TryParse(value.GetString(), out double textNumber))
        {
            return textNumber;
        }

        return 0;
    }

    private static bool TryGetString(JsonElement obj, string key, out string value)
    {
        if (!TryGetPropertyIgnoreCase(obj, key, out JsonElement prop))
        {
            value = string.Empty;
            return false;
        }

        if (prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }

        value = prop.ToString();
        return !string.IsNullOrWhiteSpace(value);
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


file sealed class ReplaySummary
{
    public DateTimeOffset GeneratedAt { get; init; }
    public required string InputPath { get; init; }
    public required string OutputDir { get; init; }
    public required double MaxSeconds { get; init; }
    public required int TotalLines { get; init; }
    public required int JsonParseFailures { get; init; }
    public required int PayloadFrames { get; init; }
    public required int TranscriptFrames { get; init; }
    public required int ReadyToStopFrames { get; init; }
    public required int ParserUpdates { get; init; }
    public required int DisplayUpdates { get; init; }
    public required int CommittedOutputs { get; init; }
    public required bool SawReadyToStop { get; init; }
    public required List<string> CommittedSamples { get; init; }
    public required List<RepeatedItem> TopRepeatedCommitted { get; init; }
}


file sealed class RepeatedItem
{
    public required string Text { get; init; }
    public required int Count { get; init; }
}


file readonly record struct TimelineStats(int EventLines, int CommitLines);
