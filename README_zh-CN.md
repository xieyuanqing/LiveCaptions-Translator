<div align="center">

<img src="src/LiveCaptions-Translator.ico" width="128" height="128" alt="LiveCaptions Translator图标"/>

# LiveCaptions Translator

<a href="https://trendshift.io/repositories/14278" target="_blank"><img src="https://trendshift.io/api/badge/repositories/14278" alt="SakiRinn%2FLiveCaptions-Translator | Trendshift" style="width: 250px; height: 55px;" width="250" height="55"/></a>

### *基于 Whisper Bridge 输入的实时字幕翻译工具*

[![Master Build](https://github.com/SakiRinn/LiveCaptions-Translator/actions/workflows/dotnet-build.yml/badge.svg?branch=master)](https://github.com/SakiRinn/LiveCaptions-Translator/actions/workflows/dotnet-build.yml)
[![GitHub Release](https://img.shields.io/github/v/release/SakiRinn/LiveCaptions-Translator?label=Latest&color=yellow)](https://github.com/SakiRinn/LiveCaptions-Translator/releases/latest)
[![Windows 11](https://img.shields.io/badge/platform-Windows11-blue?logo=windows11&style=&color=1E9BFA)](https://www.microsoft.com/en-us/software-download/windows11)
[![GitHub License](https://img.shields.io/github/license/SakiRinn/LiveCaptions-Translator)](https://github.com/SakiRinn/LiveCaptions-Translator/blob/master/LICENSE)
[![GitHub Stars](https://img.shields.io/github/stars/SakiRinn/LiveCaptions-Translator)](https://github.com/SakiRinn/LiveCaptions-Translator/stargazers)

[English](README.md) | **中文**

</div>

## 概述

**✨ LiveCaptions Translator = Whisper Bridge + 翻译API ✨**

这是一个通过本地 Whisper Bridge WebSocket 接收字幕并进行实时翻译的轻量级工具。

ASR 编排可以放在外部控制前端，本应用专注于字幕显示、翻译、悬浮窗与历史记录。

**🚀 快速开始:** 从[发布页面](https://github.com/SakiRinn/LiveCaptions-Translator/releases)下载并一键启动！

<div align="center">
  <img src="images/preview.png" alt="LiveCaptions Translator预览" width="90%" />
  <br>
  <em style="font-size:80%">LiveCaptions Translator预览</em>
  <br>
</div>

## 功能特性

- **🔄 Bridge 优先集成**

  使用单一 Whisper Bridge 端点输入，保证翻译与显示链路稳定。

  设置页提供状态可视化、URL 安全应用、重连控制和高级流参数。

- **🧠 Whisper Bridge 单接口输入**

  当前版本保留单一 ASR 输入链路：本地 WebSocket bridge（适配 WhisperLive 类流式后端）。

  翻译 API 与显示体验保持不变，ASR 编排建议放在外部控制前端中完成。

- **🎨 现代化界面**

  易于使用且简洁的Fluent UI与现代Windows美学保持一致。

  它可以根据系统设置自动在浅色和深色主题🌓之间切换。

- **🌐 多种翻译服务**

  支持各种翻译引擎，包括2个开箱即用的谷歌翻译。

  已实现的翻译引擎如下表所示：

  <div align="center">

  | API                                                 | 类型    | 托管方式 |
  |-----------------------------------------------------|-------|------|
  | [Ollama](https://ollama.com)                        | 基于LLM | 自托管  |
  | OpenAI兼容API                                         | 基于LLM | 在线   |
  | [OpenRouter](https://openrouter.ai)                 | 基于LLM | 在线   |
  | 谷歌翻译                                                | 传统翻译  | 在线   |
  | DeepL                                               | 传统翻译  | 在线   |
  | 有道翻译                                                | 传统翻译  | 在线   |
  | 百度翻译                                                | 传统翻译  | 在线   |
  | [MTranServer](https://github.com/xxnuo/MTranServer) | 传统翻译  | 自托管  |
  | [LibreTranslate](https://libretranslate.com/)       | 传统翻译  | 自托管  |

  </div>

  强烈推荐使用 **基于LLM** 的翻译引擎，因为LLM擅长处理不完整的句子并能很好地理解上下文。

- **🪟 悬浮窗口**

  打开无边框、透明的悬浮窗口显示字幕，提供最沉浸式的体验。这对游戏、视频和直播等场景非常有用！

  您甚至可以使其完全嵌入到屏幕中，成为屏幕的一部分。这意味着它不会影响您的任何操作！这对游戏玩家来说再合适不过了。

  <div align="center">
    <img src="images/overlay_window.png" alt="悬浮窗口" width="80%" />
    <br>
    <em style="font-size:80%">悬浮窗口</em>
    <br>
  </div>

  您可以在任务栏上打开悬浮窗口，以及调整诸如窗口背景和字幕颜色、字体大小和透明度等参数。极高的可配置性使其能够完全符合您的偏好！

  您可以在设置页的 *Overlay Sentences* 选项调整同时显示的句子数量。

- **⚙️ 灵活控制**

  支持窗口置顶和便利的翻译暂停/恢复，并且您可以一键复制文本以便快速分享或保存。

- **📒 历史记录管理**

  记录原文和翻译文本，非常适合会议、讲座和重要讨论。

  您可以将所有记录导出为CSV文件。

  <div align="center">
    <img src="images/history.png" alt="翻译历史" width="90%" />
    <br>
    <em style="font-size:80%">翻译历史</em>
    <br>
  </div>

- **🎞️ 日志卡片**

  最近的转录记录可以显示为日志卡片，这有助于您更好地把握上下文。

  您可以在主页任务栏上启用它，并在设置页的 *Log Cards* 选项调整卡片数量。

  <div align="center">
    <img src="images/log_cards.png" alt="日志卡片" width="90%" />
    <br>
    <em style="font-size:80%">日志卡片</em>
    <br>
  </div>


## 系统要求

<div align="center">

| 要求                                                                                                                    | 详情          |
|-----------------------------------------------------------------------------------------------------------------------|-------------|
| <img src="https://img.shields.io/badge/Windows-11%20(22H2+)-0078D6?style=for-the-badge&logo=windows&logoColor=white"> | 桌面运行环境      |
| <img src="https://img.shields.io/badge/.NET-8.0+-512BD4?style=for-the-badge&logo=dotnet&logoColor=white">             | 推荐。未在之前版本测试 |

</div>

本工具是基于 Whisper Bridge 输入的 Windows 桌面应用，当前在 **Windows 11 22H2+** 环境验证。

我们建议您安装 **.NET运行时8.0** 或更高版本。如果您无法安装，可以下载 ***with runtime*** 版本，但其文件较大。

<div align="center">
  <p align="center">
    <a href="https://github.com/SakiRinn/LiveCaptions-Translator/wiki">
      <img src="https://img.shields.io/badge/📚_查看我们的Wiki获取详细信息-2ea44f?style=for-the-badge" alt="查看我们的Wiki">
    </a>
  </p>
</div>

## 入门指南

> ⚠️ **重要:** 首次运行LiveCaptions Translator前，您必须完成以下步骤。
>
> Bridge 消息格式请参考下方协议说明。

### 步骤1: 启动 bridge 服务

先启动本地 ASR bridge（例如 WhisperLiveKit runtime），并确认其提供 WebSocket 端点。

### 步骤2: 在应用中配置 bridge 地址

打开 **设置** 页，填写 **Whisper Bridge URL**（默认：`ws://127.0.0.1:8765/captions`）。

点击 **Apply**。应用会先校验 URL 并探测连接，再切换端点；当存在活动会话时会启用回滚保护。

### 步骤3: 确认运行状态

在设置页检查 bridge 状态：

- `Connected` 表示字幕输入正常。
- `Reconnecting` 表示正在自动重连。
- bridge 重启或端点变化后可点击 **Reconnect**。

状态为 `Connected` 后，切回 Caption 页面即可开始实时翻译！🎉

### Bridge 消息协议

bridge 需要向 WebSocket 端点持续推送文本增量（应用默认地址：`ws://127.0.0.1:8765/captions`）。

可在设置页填写 **Whisper Bridge URL**（会持久化到 `setting.json` 的 `WhisperBridgeUrl` 字段）。

#### Bridge 稳定协议（v1）

推荐消息字段：

- `text`（字符串）
- `isFinal`（布尔）
- `sequence`（数字）
- `source`（字符串）
- `timestamp`（ISO 8601 或 unix 毫秒/秒，可选）
- `utteranceId`（字符串，可选）

示例：

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

兼容说明：客户端对常见别名字段（如 `caption`、`transcript`、`final`、`seq`、`is_final` 等）做了容错，但仍建议 bridge 优先使用上面的稳定字段。

## 项目统计

### 活动

<div align="center">
  <img src="https://img.shields.io/github/issues/SakiRinn/LiveCaptions-Translator?style=for-the-badge&label=Issues&color=yellow" alt="GitHub Issues">
  <img src="https://img.shields.io/github/issues-pr/SakiRinn/LiveCaptions-Translator?style=for-the-badge&label=Pull%20Requests&color=blue" alt="GitHub Pull Requests">
  <img src="https://img.shields.io/github/discussions/SakiRinn/LiveCaptions-Translator?style=for-the-badge&label=Discussions&color=orange" alt="GitHub Discussions">
  <img src="https://img.shields.io/github/last-commit/SakiRinn/LiveCaptions-Translator?style=for-the-badge&label=Last%20Commit&color=purple" alt="GitHub Last Commit">
</div>

### 贡献者

<div align="center">
  <img src="https://img.shields.io/github/contributors/SakiRinn/LiveCaptions-Translator?style=for-the-badge&label=Contributors&color=success" alt="GitHub Contributors">
  <br>
  <a href="https://github.com/SakiRinn/LiveCaptions-Translator/graphs/contributors">
    <img src="https://contrib.rocks/image?repo=SakiRinn/LiveCaptions-Translator" />
  </a>
</div>

### Star历史

[![Stargazers over time](https://starchart.cc/SakiRinn/LiveCaptions-Translator.svg?variant=adaptive)](https://starchart.cc/SakiRinn/LiveCaptions-Translator)
