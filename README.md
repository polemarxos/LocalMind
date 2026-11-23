# LocalMind

**Private AI chat for Windows that runs entirely on your own PC.** No cloud, no account and no Ollama needed.
Pick a model that fits your graphics card, chat with it, and use it from your phone or other PCs on your network.

## Features

- **Built-in AI engine.** LocalMind downloads and runs the official [llama.cpp](https://github.com/ggml-org/llama.cpp)
  build that matches your hardware: NVIDIA (CUDA 12 / 13), AMD and Intel (Vulkan), or CPU only. It loads as much
  of the model onto the GPU as fits.
- **Model Manager with models matched to your GPU.** About 700 GGUF models from trusted publishers (unsloth, bartowski,
  lmstudio-community, ggml-org), refreshed from Hugging Face every day.
  - By default it shows only the **best models that fit entirely in your VRAM**. Switch the *Show* box to see everything.
  - The quantization is picked automatically for your VRAM, using the real file sizes.
  - Tick the models you want and press **Download**. Downloads resume after a restart, are verified with SHA-256,
    and split files and vision files are handled automatically.
  - Use **★ Recommend for my PC** for a ready-made starter set.
- **Use it from your phone and other PCs.** Turn on *Share on network* and scan the QR code to get a mobile chat page.
  It supports photos, a PIN, and chat history shared with the PC. An OpenAI-compatible `/v1` API lets other apps
  use your GPU too.
- **Chat features:** personas, thinking/reasoning view, images (vision models), PDF and text attachments,
  paste and drag-and-drop, chat search, Markdown export and import, code highlighting with copy buttons.
- **Other backends:** you can also connect to Ollama or any OpenAI-compatible server (LM Studio, vLLM,
  or another PC running LocalMind).

## Requirements

- Windows 10/11 x64
- [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (included with Windows 11)
- For GPU speed: an NVIDIA, AMD or Intel GPU with 6 GB of VRAM or more is recommended. CPU-only works with small models.
- Disk space for models: 2–30 GB each

## Quick start

1. Download `LocalMind.exe` from [Releases](https://github.com/polemarxos/LocalMind/releases), or build it yourself (see below).
2. Run it. On first start the Model Manager opens. Press **★ Recommend for my PC**, then **Download**.
3. When the download finishes, pick the model in the toolbar and start chatting. Press **Ctrl+Enter** to send.

## Use it from your phone

**Tools → Share on network**, then tick *Share this PC's AI on my network* and scan the QR code.
See [docs/LAN-SHARING.md](docs/LAN-SHARING.md) for firewall tips and how to use the `/v1` API from other apps.

## Build from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bat
build.bat
```

This produces a single self-contained `publish\LocalMind.exe`. You can also open `LocalMind.slnx` in Visual Studio 2026.

## Project layout

| Path | What it is |
|---|---|
| `src/LocalMind/` | The Windows app (VB.NET, WinForms, .NET 10) |
| `src/LocalMind/Resources/` | Embedded offline assets: model catalog, phone web UI, highlight.js |
| `tests/LocalMind.Harness/` | Console test harness for the engine, catalog, downloads and LAN server ([how to run](tests/README.md)) |
| `tools/build_catalog.py` | Regenerates the built-in model catalog from Hugging Face |
| `docs/` | [Architecture](docs/ARCHITECTURE.md) and [LAN sharing](docs/LAN-SHARING.md) |

## Where LocalMind keeps its data

| Folder | Contents |
|---|---|
| `%AppData%\LocalMind` | Settings, chats, personas |
| `%LocalAppData%\LocalMind\engine` | llama.cpp engine |
| `%LocalAppData%\LocalMind\models` | Downloaded models (you can move this in Settings) |

Upgrading from **OllamaChat**: your old folders are moved over automatically the first time LocalMind starts.

## Releasing

Push a tag such as `v2.0.0`. GitHub Actions builds `LocalMind.exe` and attaches it to a new Release.
