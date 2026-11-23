# Changelog

## Unreleased
- Portable layout: the engine downloads to `Engine\` and models to `Models\` next to `LocalMind.exe`.
  Existing engine and models are moved there automatically when they are on the same drive.
- Release builds publish only `LocalMind.exe`.

## 2.0.0 — renamed from OllamaChat to LocalMind

### New
- **Built-in llama.cpp engine.** Ollama is no longer required. The app installs and updates the right build for
  the GPU (CUDA 12/13, Vulkan, ROCm, CPU) and runs it hidden on 127.0.0.1. The engine is shut down when the app
  closes, even after a crash.
- **Model Manager** (Ctrl+M):
  - about 700 GGUF models, refreshed from Hugging Face every day, with a built-in copy for offline use;
  - by default only the best models that fit in VRAM are shown;
  - automatic quantization choice;
  - a download queue that resumes and checks SHA-256;
  - a recommend button, search across all of Hugging Face, an installed-models tab and GGUF import.
- **LAN sharing:**
  - a mobile chat page with a QR code login and PIN;
  - an OpenAI-compatible `/v1` API;
  - brute-force lockout;
  - a button to add a firewall rule.
- **Settings window:** backend choice, engine variant, context size, GPU layers, KV-cache type, parallel chats,
  unload when idle, models folder, Hugging Face token.
- Paste images and files with Ctrl+V, drag and drop, Esc to stop, rename chats with F2, image thumbnails,
  a copy-answer button, and the model and tok/s under each answer.

### Improved
- Chat history is cached in memory instead of re-reading every file after each message.
- Opening a chat renders it in one script call.
- Streaming re-render rate adapts to answer length.
- highlight.js is embedded, so it works offline.
- Large images are downscaled, and old images are dropped from the context.
- Settings and history are saved atomically.

### Security
- Raw HTML in model output is no longer rendered in the chat view.

### Migration
- `%AppData%\OllamaChat` and `%LocalAppData%\OllamaChat` are moved to `…\LocalMind` on first start.
- Users upgrading from v1 are asked once whether to switch from Ollama to the built-in engine.
