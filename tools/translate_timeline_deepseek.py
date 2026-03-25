#!/usr/bin/env python3
"""Replay timeline commits through DeepSeek and log translation quality artifacts.

Usage example:
  python tools/translate_timeline_deepseek.py \
    --input "C:/.../timeline_commits.txt" \
    --output-dir "C:/.../translation_deepseek_chat_20260303_010203" \
    --api-base "https://api.deepseek.com/v1" \
    --model "deepseek-chat" \
    --temperature 1.3 \
    --title "..." \
    --description "..."

Environment:
  DEEPSEEK_API_KEY must be set.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import statistics
import time
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import Any
from urllib import error, request


LINE_RE = re.compile(
    r'^T\+\s*(?P<t>[0-9]+\.[0-9]+)s\s*\|\s*seq=(?P<seq>-?[0-9]+)\s*\|\s*frame=(?P<frame>-?[0-9]+)\s*\|\s*commit="(?P<text>.*)"\s*$'
)
KANA_RE = re.compile(r"[\u3040-\u30ff]")


@dataclass
class CommitLine:
    t_sec: float
    seq: int
    frame: int
    text: str
    raw_line_no: int


@dataclass
class TranslateResult:
    t_sec: float
    seq: int
    frame: int
    source: str
    translation: str
    latency_ms: int
    status: str
    error: str
    attempts: int


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Translate timeline commits via DeepSeek.")
    parser.add_argument("--input", required=True, help="Path to timeline_commits.txt")
    parser.add_argument("--output-dir", required=True, help="Output directory for logs")
    parser.add_argument("--api-base", default="https://api.deepseek.com/v1", help="DeepSeek API base URL")
    parser.add_argument("--model", default="deepseek-chat", help="Model name")
    parser.add_argument("--temperature", type=float, default=1.3, help="Sampling temperature")
    parser.add_argument("--target-language", default="zh-CN", help="Target translation language")
    parser.add_argument("--title", required=True, help="Stream title context")
    parser.add_argument("--description", required=True, help="Stream description context")
    parser.add_argument("--timeout-sec", type=int, default=30, help="HTTP timeout per request")
    parser.add_argument("--retry", type=int, default=3, help="Retry count for transient errors")
    parser.add_argument("--sleep-ms", type=int, default=80, help="Sleep between successful calls")
    parser.add_argument("--max-lines", type=int, default=0, help="Optional cap on commit lines")
    return parser.parse_args()


def load_commit_lines(path: Path, max_lines: int) -> list[CommitLine]:
    rows: list[CommitLine] = []
    with path.open("r", encoding="utf-8") as f:
        for line_no, raw_line in enumerate(f, start=1):
            line = raw_line.rstrip("\n")
            if not line.strip():
                continue
            match = LINE_RE.match(line)
            if not match:
                continue

            text = match.group("text").replace("\\\"", '"').strip()
            rows.append(
                CommitLine(
                    t_sec=float(match.group("t")),
                    seq=int(match.group("seq")),
                    frame=int(match.group("frame")),
                    text=text,
                    raw_line_no=line_no,
                )
            )
            if max_lines > 0 and len(rows) >= max_lines:
                break
    return rows


def build_system_prompt(target_language: str, title: str, description: str) -> str:
    return (
        "You are a professional real-time subtitle translator. "
        f"Translate Japanese subtitle fragments into {target_language}. "
        "Output one line only. Do not explain. Keep names and game terms stable. "
        "Keep incomplete fragments natural and concise. Preserve tone and intent. "
        "Do not add content not present in input. "
        "Context for this stream:\n"
        f"- Title: {title}\n"
        f"- Description: {description}"
    )


def call_chat_completions(
    api_base: str,
    api_key: str,
    model: str,
    temperature: float,
    system_prompt: str,
    text: str,
    timeout_sec: int,
    retry: int,
) -> tuple[str, int, int, str]:
    endpoint = api_base.rstrip("/") + "/chat/completions"
    payload = {
        "model": model,
        "temperature": temperature,
        "messages": [
            {"role": "system", "content": system_prompt},
            {"role": "user", "content": f"🔤 {text} 🔤"},
        ],
    }

    encoded = json.dumps(payload, ensure_ascii=False).encode("utf-8")
    attempt = 0
    while True:
        attempt += 1
        start = time.perf_counter()
        req = request.Request(
            endpoint,
            data=encoded,
            method="POST",
            headers={
                "Authorization": f"Bearer {api_key}",
                "Content-Type": "application/json",
            },
        )

        try:
            with request.urlopen(req, timeout=timeout_sec) as resp:
                body = resp.read().decode("utf-8", errors="replace")
                latency_ms = int((time.perf_counter() - start) * 1000)
                status_code = resp.status
        except error.HTTPError as exc:
            body = exc.read().decode("utf-8", errors="replace") if exc.fp else str(exc)
            latency_ms = int((time.perf_counter() - start) * 1000)
            transient = exc.code in (408, 409, 429, 500, 502, 503, 504)
            if transient and attempt <= retry:
                time.sleep(min(4.0, 0.5 * attempt))
                continue
            raise RuntimeError(f"HTTP {exc.code}: {body}") from exc
        except Exception as exc:  # noqa: BLE001
            latency_ms = int((time.perf_counter() - start) * 1000)
            if attempt <= retry:
                time.sleep(min(4.0, 0.5 * attempt))
                continue
            raise RuntimeError(f"request failed after retries: {exc}") from exc

        try:
            parsed = json.loads(body)
            content = (
                parsed.get("choices", [{}])[0]
                .get("message", {})
                .get("content", "")
                .replace("🔤", "")
                .strip()
            )
        except Exception as exc:  # noqa: BLE001
            raise RuntimeError(f"invalid JSON response: {body}") from exc

        return content, latency_ms, attempt, f"HTTP {status_code}"


def sanitize_one_line(text: str) -> str:
    return " ".join(text.replace("\r", " ").replace("\n", " ").split())


def write_files(
    out_dir: Path,
    args: argparse.Namespace,
    system_prompt: str,
    commits: list[CommitLine],
    results: list[TranslateResult],
) -> None:
    now = datetime.now().isoformat(timespec="seconds")
    timeline_path = out_dir / "translation_timeline.txt"
    timeline_compact_path = out_dir / "translation_timeline_compact.txt"
    jsonl_path = out_dir / "translation_results.jsonl"
    summary_json_path = out_dir / "translation_summary.json"
    summary_md_path = out_dir / "translation_quality_report.md"
    input_head_path = out_dir / "timeline_commits_head.txt"

    ok_rows = [r for r in results if r.status == "ok"]
    err_rows = [r for r in results if r.status != "ok"]
    latencies = [r.latency_ms for r in ok_rows]
    p50_ms = int(statistics.median(latencies)) if latencies else 0
    p95_ms = int(statistics.quantiles(latencies, n=20)[18]) if len(latencies) >= 20 else (max(latencies) if latencies else 0)

    kana_rows = [r for r in ok_rows if KANA_RE.search(r.translation)]
    empty_rows = [r for r in ok_rows if not r.translation.strip()]

    consecutive_dups = 0
    for idx in range(1, len(ok_rows)):
        if ok_rows[idx].translation == ok_rows[idx - 1].translation:
            consecutive_dups += 1

    with timeline_path.open("w", encoding="utf-8") as f:
        f.write(f"# translation timeline generated at {now}\n")
        f.write(f"# input={args.input}\n")
        f.write(f"# model={args.model} temperature={args.temperature}\n")
        f.write("# fields: t_sec | seq | frame | latency_ms | status | src | zh | attempts | note\n")
        for row in results:
            note = row.error if row.error else "-"
            f.write(
                " | ".join(
                    [
                        f"T+{row.t_sec:8.3f}s",
                        f"seq={row.seq}",
                        f"frame={row.frame}",
                        f"latency_ms={row.latency_ms}",
                        f"status={row.status}",
                        f"src={json.dumps(sanitize_one_line(row.source), ensure_ascii=False)}",
                        f"zh={json.dumps(sanitize_one_line(row.translation), ensure_ascii=False)}",
                        f"attempts={row.attempts}",
                        f"note={json.dumps(sanitize_one_line(note), ensure_ascii=False)}",
                    ]
                )
                + "\n"
            )

    with timeline_compact_path.open("w", encoding="utf-8") as f:
        f.write(f"# compact translation timeline generated at {now}\n")
        f.write("# format: timestamp + source/translation text blocks\n\n")
        for row in results:
            f.write(f"[T+{row.t_sec:8.3f}s] seq={row.seq} frame={row.frame} status={row.status} latency={row.latency_ms}ms\n")
            f.write(f"JP: {sanitize_one_line(row.source)}\n")
            if row.status == "ok":
                f.write(f"ZH: {sanitize_one_line(row.translation)}\n")
            else:
                f.write(f"ERR: {sanitize_one_line(row.error)}\n")
            f.write("---\n")

    with jsonl_path.open("w", encoding="utf-8") as f:
        for row in results:
            f.write(
                json.dumps(
                    {
                        "t_sec": row.t_sec,
                        "seq": row.seq,
                        "frame": row.frame,
                        "source": row.source,
                        "translation": row.translation,
                        "latency_ms": row.latency_ms,
                        "status": row.status,
                        "error": row.error,
                        "attempts": row.attempts,
                    },
                    ensure_ascii=False,
                )
                + "\n"
            )

    with input_head_path.open("w", encoding="utf-8") as f:
        for row in commits[:20]:
            f.write(f"T+{row.t_sec:8.3f}s | seq={row.seq} | frame={row.frame} | commit=\"{row.text}\"\n")

    summary_obj: dict[str, Any] = {
        "generated_at": now,
        "input_path": str(args.input),
        "output_dir": str(out_dir),
        "model": args.model,
        "temperature": args.temperature,
        "target_language": args.target_language,
        "title": args.title,
        "description": args.description,
        "total_commits": len(commits),
        "ok_count": len(ok_rows),
        "error_count": len(err_rows),
        "avg_latency_ms": int(sum(latencies) / len(latencies)) if latencies else 0,
        "p50_latency_ms": p50_ms,
        "p95_latency_ms": p95_ms,
        "kana_in_translation_count": len(kana_rows),
        "empty_translation_count": len(empty_rows),
        "consecutive_duplicate_translation_count": consecutive_dups,
        "artifacts": {
            "timeline_txt": str(timeline_path),
            "timeline_compact_txt": str(timeline_compact_path),
            "results_jsonl": str(jsonl_path),
            "head_txt": str(input_head_path),
            "summary_md": str(summary_md_path),
            "summary_json": str(summary_json_path),
        },
    }

    summary_json_path.write_text(json.dumps(summary_obj, ensure_ascii=False, indent=2), encoding="utf-8")

    top_errors = err_rows[:10]
    sample_rows = ok_rows[:15]
    md_lines = [
        "# DeepSeek Translation Quality Report",
        "",
        f"- Generated At: `{now}`",
        f"- Input: `{args.input}`",
        f"- Output Dir: `{out_dir}`",
        f"- Model: `{args.model}`",
        f"- Temperature: `{args.temperature}`",
        f"- Target Language: `{args.target_language}`",
        "",
        "## Stream Context",
        f"- Title: {args.title}",
        f"- Description: {args.description}",
        "",
        "## Prompt",
        "```text",
        system_prompt,
        "```",
        "",
        "## Metrics",
        f"- Total commits: {len(commits)}",
        f"- Success: {len(ok_rows)}",
        f"- Errors: {len(err_rows)}",
        f"- Avg latency: {summary_obj['avg_latency_ms']} ms",
        f"- P50 latency: {p50_ms} ms",
        f"- P95 latency: {p95_ms} ms",
        f"- Lines with kana in translation (likely JP leakage): {len(kana_rows)}",
        f"- Empty translations: {len(empty_rows)}",
        f"- Consecutive duplicate translations: {consecutive_dups}",
        "",
        "## Sample Outputs",
    ]

    if not sample_rows:
        md_lines.append("- (no successful rows)")
    else:
        for row in sample_rows:
            md_lines.append(f"- T+{row.t_sec:0.3f}s seq={row.seq}: `{row.source}` -> `{row.translation}`")

    md_lines.append("")
    md_lines.append("## Error Samples")
    if not top_errors:
        md_lines.append("- (none)")
    else:
        for row in top_errors:
            md_lines.append(f"- T+{row.t_sec:0.3f}s seq={row.seq}: {row.error}")

    summary_md_path.write_text("\n".join(md_lines) + "\n", encoding="utf-8")


def main() -> int:
    args = parse_args()
    api_key = os.getenv("DEEPSEEK_API_KEY", "").strip()
    if not api_key:
        raise SystemExit("DEEPSEEK_API_KEY is missing.")

    input_path = Path(args.input).expanduser().resolve()
    out_dir = Path(args.output_dir).expanduser().resolve()
    out_dir.mkdir(parents=True, exist_ok=True)

    commits = load_commit_lines(input_path, args.max_lines)
    if not commits:
        raise SystemExit(f"No commit lines parsed from: {input_path}")

    system_prompt = build_system_prompt(args.target_language, args.title, args.description)

    results: list[TranslateResult] = []
    for row in commits:
        try:
            translated, latency_ms, attempts, _status = call_chat_completions(
                api_base=args.api_base,
                api_key=api_key,
                model=args.model,
                temperature=args.temperature,
                system_prompt=system_prompt,
                text=row.text,
                timeout_sec=args.timeout_sec,
                retry=args.retry,
            )
            results.append(
                TranslateResult(
                    t_sec=row.t_sec,
                    seq=row.seq,
                    frame=row.frame,
                    source=row.text,
                    translation=translated,
                    latency_ms=latency_ms,
                    status="ok",
                    error="",
                    attempts=attempts,
                )
            )
        except Exception as exc:  # noqa: BLE001
            results.append(
                TranslateResult(
                    t_sec=row.t_sec,
                    seq=row.seq,
                    frame=row.frame,
                    source=row.text,
                    translation="",
                    latency_ms=0,
                    status="error",
                    error=str(exc),
                    attempts=args.retry,
                )
            )
        if args.sleep_ms > 0:
            time.sleep(args.sleep_ms / 1000.0)

    write_files(out_dir, args, system_prompt, commits, results)
    print(
        json.dumps(
            {
                "input": str(input_path),
                "output_dir": str(out_dir),
                "total": len(commits),
                "ok": sum(1 for r in results if r.status == "ok"),
                "error": sum(1 for r in results if r.status != "ok"),
                "timeline": str(out_dir / "translation_timeline.txt"),
                "report": str(out_dir / "translation_quality_report.md"),
            },
            ensure_ascii=False,
            indent=2,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
