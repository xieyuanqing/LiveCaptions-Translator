# BridgeReplayLogger

Replay a captured `asr_frames.jsonl` file through the same parser + incremental aggregator used by `LiveCaptions-Translator`, then dump detailed logs for offline analysis.

## Run

```bash
dotnet run --project tools/BridgeReplayLogger/BridgeReplayLogger.csproj -- <input-jsonl> [output-dir] [max-seconds]
```

## Outputs

- `payload_frames.jsonl`: normalized payload stream per frame.
- `caption_updates.jsonl`: parsed updates and aggregator output per step.
- `timeline_events.txt`: timestamped plain-text event timeline (final/display/commit).
- `timeline_commits.txt`: timestamped plain-text committed captions only.
- `input_head.jsonl`: first 20 lines of original input for quick format reference.
- `summary.json`: machine-readable summary metrics.
- `summary.md`: human-readable snapshot for quick review.

## Example

```bash
dotnet run --project tools/BridgeReplayLogger/BridgeReplayLogger.csproj -- \
  "C:\Users\XYQ\WhisperLiveKit\analysis_runs\validation_set_20260302_030226\runs\capture_20260302_030357_226536\raw\asr_frames.jsonl" \
  "C:\Users\XYQ\WhisperLiveKit\analysis_runs\bridge_replay_logs\capture_20260302_030357_226536"
```
