<div align="center">

<img src="src/LiveCaptions-Translator.ico" width="128" height="128" alt="LiveCaptions-Translator Icon"/>

# LiveCaptions Translator

<a href="https://trendshift.io/repositories/14278" target="_blank"><img src="https://trendshift.io/api/badge/repositories/14278" alt="SakiRinn%2FLiveCaptions-Translator | Trendshift" style="width: 250px; height: 55px;" width="250" height="55"/></a>

### *Real-time subtitle translation tool powered by Whisper Bridge input*

[![Master Build](https://github.com/SakiRinn/LiveCaptions-Translator/actions/workflows/dotnet-build.yml/badge.svg?branch=master)](https://github.com/SakiRinn/LiveCaptions-Translator/actions/workflows/dotnet-build.yml)
[![GitHub Release](https://img.shields.io/github/v/release/SakiRinn/LiveCaptions-Translator?label=Latest&color=yellow)](https://github.com/SakiRinn/LiveCaptions-Translator/releases/latest)
[![Windows 11](https://img.shields.io/badge/platform-Windows11-blue?logo=windows11&style=&color=1E9BFA)](https://www.microsoft.com/en-us/software-download/windows11)
[![GitHub License](https://img.shields.io/github/license/SakiRinn/LiveCaptions-Translator)](https://github.com/SakiRinn/LiveCaptions-Translator/blob/master/LICENSE)
[![GitHub Stars](https://img.shields.io/github/stars/SakiRinn/LiveCaptions-Translator)](https://github.com/SakiRinn/LiveCaptions-Translator/stargazers)

**English** | [中文](README_zh-CN.md)

</div>

## Overview

**✨ LiveCaptions Translator = Whisper Bridge + Translate API ✨**

This is a lightweight tool that consumes captions from a local Whisper Bridge WebSocket and translates them in real time.

ASR orchestration can stay in your external control frontend, while this app focuses on subtitle rendering, translation, overlay, and history.

**🚀 Quick Start:** Download from [Releases](https://github.com/SakiRinn/LiveCaptions-Translator/releases) and start with a single click!

<div align="center">
  <img src="images/preview.png" alt="Preview of LiveCaptions Translator" width="90%" />
  <br>
  <em style="font-size:80%">Preview of LiveCaptions Translator</em>
  <br>
</div>

## Features

- **🔄 Bridge-first Integration**

  Connects to a single Whisper Bridge endpoint and keeps translation/display pipeline stable.

  Setting page includes status visibility, safe URL apply, reconnect control, and advanced stream options.

- **🧠 Whisper Bridge Input (single interface)**

  This build keeps a single ASR input path through a local WebSocket bridge, designed for WhisperLive-style streaming backends.

  The app keeps existing translation APIs and display UX while offloading ASR orchestration to your external control frontend.

- **🎨 Modern Interface**

  Easy-to-use and clean Fluent UI aligned with modern Windows aesthetics.

  It can automatically switches between light and dark themes 🌓 based on the system setting.

- **🌐 Multiple Translation Services**

  Supports various translation engines, including 2 out-of-the-box Google Translate.

  Implemented translation engines are shown in the table below:

  <div align="center">

  | API                                                 | Type        | Hosting     |
  |-----------------------------------------------------|-------------|-------------|
  | [Ollama](https://ollama.com)                        | LLM-based   | Self-hosted |
  | OpenAI Compatible API                               | LLM-based   | Online      |
  | [OpenRouter](https://openrouter.ai)                 | LLM-based   | Online      |
  | Google Translate                                    | Traditional | Online      |
  | DeepL                                               | Traditional | Online      |
  | Youdao                                              | Traditional | Online      |
  | Baidu Translate                                     | Traditional | Online      |
  | [MTranServer](https://github.com/xxnuo/MTranServer) | Traditional | Self-hosted |
  | [LibreTranslate](https://libretranslate.com/)       | Traditional | Self-hosted |

  </div>

  It's strongly recommended using **LLM-based** translation engines, as LLMs excel at handling incomplete sentences and are adept at understanding context.

- **🪟 Overlay Window**

  Open a borderless, transparent overlay window to display subtitles, providing the most immersive experience. This is very useful for scenarios like gaming, videos, and live streams!

  You can even make it completely embedded into the screen, becoming part of it. This means it won't affect any of your operations at all! This is perfect for gamers.

  <div align="center">
    <img src="images/overlay_window.png" alt="Overlay Window" width="80%" />
    <br>
    <em style="font-size:80%">Overlay window</em>
    <br>
  </div>

  You can open the Overlay Window on the taskbar and adjust its parameters such as the window background and subtitle color, font size, and transparency. Extremely high configurability allows it to completely match your preferences!

  You can adjust the number of sentences displayed simultaneously in the *Overlay Sentences* section of the setting page.

- **⚙️ Flexible Controls**

  Supports Always-on-top window and convenient translation pause/resume, and you can copy text with a single click for quick share or saving.

- **📒 History Management**

  Records original and translated text, perfect for meetings, lectures, and important discussions.

  You can export all records as a CSV file.

  <div align="center">
    <img src="images/history.png" alt="Translation history" width="90%" />
    <br>
    <em style="font-size:80%">Translation history</em>
    <br>
  </div>

- **🎞️ Log Cards**

  Recent transcription records can be displayed as Log Cards, which helps you better grasp the context.

  You can enable it on the taskbar of the main page and change the number of cards in the *Log Cards* section of the setting page.

  <div align="center">
    <img src="images/log_cards.png" alt="Log cards" width="90%" />
    <br>
    <em style="font-size:80%">Log Cards</em>
    <br>
  </div>


## Prerequisites

<div align="center">

| Requirement                                                                                                           | Details                                     |
|-----------------------------------------------------------------------------------------------------------------------|---------------------------------------------|
| <img src="https://img.shields.io/badge/Windows-11%20(22H2+)-0078D6?style=for-the-badge&logo=windows&logoColor=white"> | Desktop runtime environment.                |
| <img src="https://img.shields.io/badge/.NET-8.0+-512BD4?style=for-the-badge&logo=dotnet&logoColor=white">             | Recommended. Not test in previous versions. |

</div>

This tool is a Windows desktop app with Whisper Bridge input, tested on **Windows 11 22H2+**.

We suggest you have **.NET runtime 8.0** or higher installed. If you are not available to install one, you can download the ***with runtime*** version but its size is bigger.

<div align="center">
  <p align="center">
    <a href="https://github.com/SakiRinn/LiveCaptions-Translator/wiki">
      <img src="https://img.shields.io/badge/📚_Check_our_Wiki_for_detailed_information-2ea44f?style=for-the-badge" alt="Check our Wiki">
    </a>
  </p>
</div>

## Getting Started

> ⚠️ **IMPORTANT:** You must complete the following steps before running LiveCaptions Translator for the first time.
>
> For bridge payload details, see protocol notes below.

### Step 1: Start your bridge

Run your local ASR bridge service first (for example WhisperLiveKit runtime) and make sure it exposes a WebSocket endpoint.

### Step 2: Configure bridge endpoint in app

Open **Setting** page and set **Whisper Bridge URL** (default: `ws://127.0.0.1:8765/captions`).

Click **Apply**. The app validates URL and probes connectivity before switching endpoint, with rollback protection when an active session is running.

### Step 3: Confirm runtime status

Check bridge status in the setting page:

- `Connected` means captions are flowing.
- `Reconnecting` means bridge retries are in progress.
- Use **Reconnect** after bridge restart or endpoint changes.

After status is `Connected`, switch to Caption page and start using real-time translation! 🎉

### Bridge Payload Protocol

Your bridge should push text updates to a WebSocket endpoint (default in app: `ws://127.0.0.1:8765/captions`).

Set **Whisper Bridge URL** in the Setting page (persisted in `setting.json` as `WhisperBridgeUrl`).

#### Stable Bridge Protocol (v1)

Preferred payload fields:

- `text` (string)
- `isFinal` (bool)
- `sequence` (number)
- `source` (string)
- `timestamp` (ISO 8601 or unix ms/sec, optional)
- `utteranceId` (string, optional)

Example:

```json
{
  "text": "こんにちは、配信を始めます",
  "isFinal": false,
  "sequence": 1024,
  "source": "whisper-bridge",
  "timestamp": "2026-02-21T12:34:56.789Z",
  "utteranceId": "utt-9f8a"
}
```

Compatibility note: the client has schema fallbacks for common alias keys (`caption`, `transcript`, `final`, `seq`, `is_final`, etc.), but bridge authors should still prefer the stable fields above.

## Project Stats

### Activity

<div align="center">
  <img src="https://img.shields.io/github/issues/SakiRinn/LiveCaptions-Translator?style=for-the-badge&label=Issues&color=yellow" alt="GitHub Issues">
  <img src="https://img.shields.io/github/issues-pr/SakiRinn/LiveCaptions-Translator?style=for-the-badge&label=Pull%20Requests&color=blue" alt="GitHub Pull Requests">
  <img src="https://img.shields.io/github/discussions/SakiRinn/LiveCaptions-Translator?style=for-the-badge&label=Discussions&color=orange" alt="GitHub Discussions">
  <img src="https://img.shields.io/github/last-commit/SakiRinn/LiveCaptions-Translator?style=for-the-badge&label=Last%20Commit&color=purple" alt="GitHub Last Commit">
</div>

### Contributors

<div align="center">
  <img src="https://img.shields.io/github/contributors/SakiRinn/LiveCaptions-Translator?style=for-the-badge&label=Contributors&color=success" alt="GitHub Contributors">
  <br>
  <a href="https://github.com/SakiRinn/LiveCaptions-Translator/graphs/contributors">
    <img src="https://contrib.rocks/image?repo=SakiRinn/LiveCaptions-Translator" />
  </a>
</div>

### Star History

[![Stargazers over time](https://starchart.cc/SakiRinn/LiveCaptions-Translator.svg?variant=adaptive)](https://starchart.cc/SakiRinn/LiveCaptions-Translator)
