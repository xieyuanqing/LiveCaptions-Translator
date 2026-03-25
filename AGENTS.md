# LiveCaptions-Translator Agent Guide

## Purpose

`LiveCaptions-Translator` is the Windows desktop subtitle client in this stack. It consumes caption messages from the local Whisper bridge, translates them, and renders them in the main window and overlay UI.

## Tech Stack

- .NET 8
- WPF desktop application
- Windows-focused runtime and packaging

## Important Local Contracts

- Cross-repo canonical defaults and bridge payload rules now live in:
  - `C:\Users\XYQ\whisper-stack\cross-repo-contracts.md`
  - `C:\Users\XYQ\whisper-stack\change-rules.md`
- Treat `LiveCaptions-Translator` as a bridge consumer with compatibility tolerance, not the canonical producer-side contract source.
- If desktop work depends on broader compatibility aliases or tolerant parsing beyond the canonical fields, keep that as consumer logic unless the shared standard is explicitly updated.

## Working Rules

- Keep Windows UX, overlay behavior, and bridge stability first.
- Favor backward-compatible handling of incoming caption payloads.
- Treat `publish/` and `obj/` as generated output; do not hand-edit them.
- Be careful when changing settings persistence, reconnect logic, or overlay rendering because these affect live usage directly.

## Useful Paths

- `src/`: WPF app source
- `tests/`: test code
- `tools/`: developer tooling and helper scripts
- `images/`: README and product assets

## Verification

- Build: `dotnet build LiveCaptionsTranslator.sln`
- Test: `dotnet test LiveCaptionsTranslator.sln`

If a change here depends on `WhisperLiveKit` bridge behavior, state the expected payload or endpoint explicitly.
