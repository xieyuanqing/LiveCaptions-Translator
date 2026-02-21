import argparse
import asyncio
import json
import socket
import subprocess
import tempfile
import time
import importlib
from datetime import datetime
from pathlib import Path
from typing import TextIO

import numpy as np
import requests


def wait_for_port(host: str, port: int, timeout_sec: float) -> bool:
    deadline = time.time() + timeout_sec
    while time.time() < deadline:
        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
            sock.settimeout(1.0)
            if sock.connect_ex((host, port)) == 0:
                return True
        time.sleep(0.5)
    return False


def append_trace(trace_path: Path, message: str) -> None:
    trace_path.parent.mkdir(parents=True, exist_ok=True)
    timestamp = datetime.utcnow().strftime("%Y-%m-%dT%H:%M:%S.%fZ")
    with trace_path.open("a", encoding="utf-8") as trace:
        trace.write(f"{timestamp} {message}\n")


def start_wlk_server(wlk_repo: Path, host: str, port: int, log_path: Path) -> tuple[subprocess.Popen, TextIO, list[str]]:
    log_path.parent.mkdir(parents=True, exist_ok=True)
    log_file = log_path.open("a", encoding="utf-8")

    cmd = [
        "wlk",
        "--host",
        host,
        "--port",
        str(port),
        "--pcm-input",
        "--model",
        "tiny.en",
        "--backend-policy",
        "localagreement",
        "--backend",
        "whisper",
        "--language",
        "en",
        "--min-chunk-size",
        "0.1",
        "--log-level",
        "INFO",
    ]

    process = subprocess.Popen(
        cmd,
        cwd=str(wlk_repo),
        stdout=log_file,
        stderr=subprocess.STDOUT,
        text=True,
        encoding="utf-8",
    )
    return process, log_file, cmd


def download_reference_wav() -> Path:
    url = "https://github.com/ggerganov/whisper.cpp/raw/master/samples/jfk.wav"
    temp_dir = Path(tempfile.gettempdir())
    wav_path = temp_dir / "wlk_jfk.wav"
    if wav_path.exists() and wav_path.stat().st_size > 0:
        return wav_path

    response = requests.get(url, timeout=30)
    response.raise_for_status()
    wav_path.write_bytes(response.content)
    return wav_path


def load_pcm_chunks(wav_path: Path, chunk_samples: int = 1600) -> list[bytes]:
    librosa = importlib.import_module("librosa")

    audio, _ = librosa.load(str(wav_path), sr=16000, mono=True)
    pcm = np.clip(audio, -1.0, 1.0)
    pcm_i16 = (pcm * 32767.0).astype(np.int16)

    chunks: list[bytes] = []
    for i in range(0, len(pcm_i16), chunk_samples):
        chunks.append(pcm_i16[i : i + chunk_samples].tobytes())
    return chunks


async def capture_payloads(ws_url: str, chunks: list[bytes]) -> list[dict]:
    websockets = importlib.import_module("websockets")

    captured: list[dict] = []

    async with websockets.connect(ws_url, max_size=8 * 1024 * 1024) as ws:
        async def receiver() -> None:
            while True:
                raw = await ws.recv()
                payload = json.loads(raw)
                captured.append(payload)
                if payload.get("type") == "ready_to_stop":
                    break

        recv_task = asyncio.create_task(receiver())

        for chunk in chunks:
            await ws.send(chunk)
            await asyncio.sleep(0.02)

        # Empty payload is the stop signal expected by AudioProcessor.process_audio
        await ws.send(b"")

        await asyncio.wait_for(recv_task, timeout=600)

    return captured


def save_payloads(output_path: Path, payloads: list[dict]) -> None:
    output_path.parent.mkdir(parents=True, exist_ok=True)
    with output_path.open("w", encoding="utf-8") as f:
        for payload in payloads:
            f.write(json.dumps(payload, ensure_ascii=False) + "\n")


def main() -> int:
    parser = argparse.ArgumentParser(description="Run WhisperLiveKit and capture real WS payloads.")
    parser.add_argument(
        "--wlk-repo",
        type=Path,
        default=Path(r"C:\Users\XYQ\WhisperLiveKit"),
        help="Path to local WhisperLiveKit repo",
    )
    parser.add_argument(
        "--output-jsonl",
        type=Path,
        default=Path(r"C:\Users\XYQ\LiveCaptions-Translator\tests\LiveCaptionsTranslator.Tests\Fixtures\wlk_real_capture.jsonl"),
        help="Where to store captured websocket payloads",
    )
    parser.add_argument(
        "--output-log",
        type=Path,
        default=Path(r"C:\Users\XYQ\LiveCaptions-Translator\tests\LiveCaptionsTranslator.Tests\Fixtures\wlk_server_capture.log"),
        help="Where to append wlk server logs",
    )
    parser.add_argument(
        "--output-trace",
        type=Path,
        default=Path(r"C:\Users\XYQ\LiveCaptions-Translator\tests\LiveCaptionsTranslator.Tests\Fixtures\wlk_capture_trace.log"),
        help="Where to store capture procedure trace",
    )
    parser.add_argument(
        "--output-report",
        type=Path,
        default=Path(r"C:\Users\XYQ\LiveCaptions-Translator\tests\LiveCaptionsTranslator.Tests\Fixtures\wlk_capture_report.json"),
        help="Where to store structured capture report",
    )
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=8766)
    args = parser.parse_args()

    wlk_repo = args.wlk_repo.resolve()
    if not wlk_repo.exists():
        raise FileNotFoundError(f"WhisperLiveKit repo not found: {wlk_repo}")

    args.output_jsonl.parent.mkdir(parents=True, exist_ok=True)
    args.output_jsonl.write_text("", encoding="utf-8")
    args.output_trace.parent.mkdir(parents=True, exist_ok=True)
    args.output_trace.write_text("", encoding="utf-8")
    args.output_report.parent.mkdir(parents=True, exist_ok=True)
    args.output_log.parent.mkdir(parents=True, exist_ok=True)
    with args.output_log.open("a", encoding="utf-8") as header:
        header.write(f"\n=== Capture started {datetime.utcnow().isoformat()}Z ===\n")

    append_trace(args.output_trace, f"wlk repo: {wlk_repo}")
    append_trace(args.output_trace, f"payload output: {args.output_jsonl}")
    append_trace(args.output_trace, f"server log output: {args.output_log}")

    server, server_log_file, server_cmd = start_wlk_server(wlk_repo, args.host, args.port, args.output_log)
    append_trace(args.output_trace, f"started server pid={server.pid} cmd={' '.join(server_cmd)}")

    started_at = datetime.utcnow().isoformat() + "Z"
    wav_path: Path | None = None
    chunks: list[bytes] = []
    payloads: list[dict] = []

    try:
        if server.poll() is not None:
            raise RuntimeError(f"WLK server exited early with code {server.returncode}")

        if not wait_for_port(args.host, args.port, timeout_sec=300):
            if server.poll() is not None:
                raise RuntimeError(f"WLK server exited before listen, code={server.returncode}")
            raise TimeoutError("WLK server did not start listening in time.")
        append_trace(args.output_trace, f"server listening on {args.host}:{args.port}")

        wav_path = download_reference_wav()
        append_trace(args.output_trace, f"using wav file: {wav_path}")
        chunks = load_pcm_chunks(wav_path)
        append_trace(args.output_trace, f"prepared pcm chunks: {len(chunks)}")

        ws_url = f"ws://{args.host}:{args.port}/asr"
        append_trace(args.output_trace, f"connecting websocket: {ws_url}")
        payloads = asyncio.run(capture_payloads(ws_url, chunks))
        append_trace(args.output_trace, f"captured websocket frames: {len(payloads)}")

        save_payloads(args.output_jsonl, payloads)
        append_trace(args.output_trace, f"saved payload jsonl: {args.output_jsonl}")
        print(f"Captured {len(payloads)} payload frames -> {args.output_jsonl}")
        return 0
    except Exception as ex:
        append_trace(args.output_trace, f"capture failed: {type(ex).__name__}: {ex}")
        raise
    finally:
        append_trace(args.output_trace, "shutting down server")

        if server.poll() is None:
            server.terminate()
            try:
                server.wait(timeout=10)
            except subprocess.TimeoutExpired:
                server.kill()
                server.wait(timeout=10)

        try:
            server_log_file.flush()
        finally:
            server_log_file.close()

        append_trace(args.output_trace, f"server exited with code {server.returncode}")

        report = {
            "startedAt": started_at,
            "finishedAt": datetime.utcnow().isoformat() + "Z",
            "host": args.host,
            "port": args.port,
            "wlkRepo": str(wlk_repo),
            "serverCommand": server_cmd,
            "wavPath": str(wav_path) if wav_path else None,
            "chunkCount": len(chunks),
            "payloadFrameCount": len(payloads),
            "serverExitCode": server.returncode,
            "outputJsonl": str(args.output_jsonl),
            "outputLog": str(args.output_log),
            "outputTrace": str(args.output_trace),
        }
        args.output_report.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")

        print(f"Saved WLK server logs -> {args.output_log}")
        print(f"Saved capture trace -> {args.output_trace}")
        print(f"Saved capture report -> {args.output_report}")


if __name__ == "__main__":
    raise SystemExit(main())
