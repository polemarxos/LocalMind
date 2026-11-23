<p align="center"><img src="docs/brand/social-preview.png" alt="LocalMind" width="760"></p>

# LocalMind

**Private AI chat for Windows that runs entirely on your own PC.** No cloud, no account, nothing else to install.
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
  paste and drag-and-drop, chat search and folders, a prompt library, Markdown export and import,
  code highlighting with copy buttons, and math formulas.
- **Chat with your documents:** point LocalMind at your folders, and answers cite the file and page.
- **Web search** with sources, **voice input** (Whisper, offline) and **read aloud**.
- **Quick ask from anywhere:** press **Ctrl+Alt+Space** to ask a question without opening the main window.
  LocalMind waits in the system tray.
- **Speed test:** see how fast each model runs on your PC.
- **Auto context:** each model uses the largest conversation memory that fits your GPU.
- **Image & video studio:** make pictures and short videos offline with Z-Image, FLUX.2 klein, Qwen-Image, Krea 2,
  Anima, SDXL and Wan 2.x, plus a **🔓 Uncensored** list and about 775 picture & video models in all (community GGUF, INT8 ConvRot and SDXL / SD 1.5 checkpoints).
  You get GGUF and **INT8 ConvRot** versions, picked to fit your VRAM.
  Or switch on **🖼 Image** in the chat: the chat model unloads to free the GPU, and the picture appears in the conversation.
- **Edit pictures and videos:** describe a change, paint an area, combine pictures, enlarge 4×, restyle your own videos.
  ⚡ Lightning/Turbo LoRAs make most models 4–8 step fast.
- **LoRAs and Civitai:** use your own LoRAs (trigger words added automatically) or search Civitai and install
  LoRAs and checkpoints with one click.
- **Music & voice:** songs with vocals, speech in 23 languages (Greek too), and voice cloning from a short recording.
- **Only what you want:** on first start choose chat only, or add pictures & video and/or music & voice.
- **Hands-free:** talk to the AI and hear the answer (in your own cloned voice if you like). The AI can also draw,
  make videos, compose and search the web by itself when you ask ("generate me a video of…").
- **Updates itself:** LocalMind, its AI engines and the model list stay up to date automatically.
- **Other servers:** you can also connect to any OpenAI-compatible server (LM Studio, vLLM,
  or another PC running LocalMind).

## Requirements

- Windows 10/11 x64
- [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (included with Windows 11)
- For GPU speed: an NVIDIA, AMD or Intel GPU with 6 GB of VRAM or more is recommended. CPU-only works with small models.
- Disk space for models: 2–30 GB each

## Quick start

1. Download `LocalMind.exe` from [Releases](https://github.com/polemarxos/LocalMind/releases), or build it yourself (see below).
2. Put it in its own folder (for example `D:\LocalMind`) and run it. On first start the Model Manager opens. Press **★ Recommend for my PC**, then **Download**.
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

LocalMind is fully portable. Everything lives next to the exe, so you can copy the folder to another PC or a USB drive:

```
LocalMind\
├─ LocalMind.exe
├─ Engine\    llama.cpp engine (downloaded automatically, 50–700 MB)
├─ Models\    downloaded AI models (can be changed in Settings)
└─ Data\      settings, chats, personas, prompts, speed-test results
```

If the exe is in a folder Windows doesn't allow writing to (such as `C:\Program Files`), LocalMind uses
`%AppData%\LocalMind` and `%LocalAppData%\LocalMind` instead. Put LocalMind in a normal folder, such as `D:\LocalMind`, on a drive
with plenty of free space.

## Releasing

On GitHub open **Actions → Build → Run workflow** and enter a version such as `v2.1.0`, or push a tag with that name.
GitHub Actions builds `LocalMind.exe` and attaches it to a new Release.
Once SignPath is set up, the exe is signed automatically (see [docs/CODE-SIGNING.md](docs/CODE-SIGNING.md)).
