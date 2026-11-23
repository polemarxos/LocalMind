# Changelog

## 2.6.0

### New
- **The AI uses tools by itself:** ask "draw me…", "make a song about…" or something that needs current information,
  and the chat model draws, composes or searches the web on its own (Settings → Chat → AI tools).
- **Voice conversation** (🎙 Talk): talk hands-free; the AI answers out loud and listens again.
- **Read answers in an AI voice** — your own cloned voice too (Settings → Web & voice → AI reading voice).
- **Saved voices:** keep cloned voices by name in the Music & voice studio and reuse them anywhere.
- **Video soundtrack:** right-click a video → add AI music or a voice-over → MP4.
- **Studio:**
  - New tasks: *Copy a pose*, *Copy layout / depth* (SDXL ControlNet Union with automatic depth, or editing models),
    *Sketch → picture*, *Restore an old photo / fix faces*, *Remove the background* (transparent PNG).
  - Make up to 4 variations at once; jobs queue up so you can keep adding.
  - Style presets (photo, cinematic, anime, watercolor…).
  - Every result remembers its settings: right-click → *Reuse these settings*.
  - 📝 *Describe picture* writes a prompt from a picture (needs a chat model that can see).
- **Phone page gets everything:** pictures in chat (🖼 picture mode, also edits a photo you send), songs and videos play,
  🎤 voice input, 🔊 read aloud. The AI's own tools work from the phone too.
- **Memory monitor** in the status bar: graphics memory, RAM and what is loaded; click to free memory.

### Fixes
- Files already on disk under a renamed Hugging Face folder are reused instead of downloaded again.
- Painted-area edits blend in smoothly; everything outside the painted area stays exactly as it was. The brush is faster.
- Before downloading a picture, video, voice or music model, LocalMind shows the graphics memory and RAM it needs
  and warns if this PC is too small. Civitai files that need an account are marked before you install them.
- The AI engines (llama.cpp, stable-diffusion.cpp, FFmpeg) update themselves every few days when idle
  (Settings → Chat → Updates).

## 2.5.0

### New
- **Choose what LocalMind does:** on first start (and in Settings → Chat → Extra abilities) pick *Chat only*,
  *Pictures & video* and/or *Music & voice*. Abilities that are off are hidden and download nothing.
- **Picture editing** in the studio — pick a task at the top:
  - *Edit a picture*: describe the change ("make it night", "remove the car"). New dedicated editors:
    **Qwen-Image Edit 2511** (GGUF + INT8 ConvRot) and **FLUX.1 Kontext**; FLUX.2 klein and Qwen-Image 2.1 edit too.
  - *Change part of a picture*: paint the area with a brush, only that part changes.
  - *Combine pictures*: up to 3 reference pictures ("the person from picture 1 in the room from picture 2").
  - *Restyle* and *Enlarge 4×* (Real-ESRGAN, 4x-UltraSharp, anime upscaler).
- **Video editing:** restyle or change your own video (MP4, MOV, MKV…) while keeping its motion, with
  **Wan VACE** 1.3B / 14B / Wan 2.2 VACE Fun A14B. Also **LTX-2.3** (video *with sound*). Save results as MP4.
- **⚡ Fast mode everywhere:** Lightning / Turbo / LCM / CausVid speed-up LoRAs are added automatically for SDXL,
  SD 1.5, Qwen-Image (2.1, 2512, Edit 2511), FLUX.1 Kontext and all Wan video models (4–8 steps instead of 20–50).
- **Your LoRAs:** 🧩 LoRAs in the studio. Add files or drop them in `Models\Loras`; the model family and
  **trigger words** are read from the file and can be edited; trigger words are added to your prompt automatically.
- **Civitai:** search civitai.com (or civitai.red, 18+) for LoRAs and checkpoints by model family, see previews,
  install with one click. LoRAs go to `Models\Loras` with their trigger words; checkpoints (SDXL / Pony /
  Illustrious, SD 1.5, FLUX, Z-Image, Qwen-Image, Krea 2, Anima) appear in the studio as ⭐ models.
  An optional Civitai API key enables downloads that need an account.
- **Music & voice studio** (🎵), using the same llama.cpp engine:
  - Speech in 23 languages including **Greek** (Chatterbox Multilingual), plus Qwen3-TTS, VoxCPM2 (voice design),
    Fish Audio S2 Pro and Chatterbox Turbo.
  - **Voice cloning** from a short recording (record with 🎤 or pick a file; Whisper can write the words for you).
  - **Songs with vocals** from a style and lyrics: YuE2 3B and MiniMax Music 3.
- Stable Diffusion 1.5 added (runs on 4 GB GPUs).
- 🖼 Image in chat: an attached photo is now edited (or restyled) instead of ignored; uses your LoRAs and ⚡ Fast.

## 2.4.0

### New
- **Image & video studio** (🎨 Create, or Tools → Image & video studio): make pictures and short videos on your own PC.
  15 model families: Z-Image Turbo / base, FLUX.2 klein 4B / 9B, Qwen-Image 2.1 / 2.1 Turbo / 2512, Krea 2 Turbo,
  Anima (anime), FLUX.1 schnell, Stable Diffusion XL, and video with Wan 2.2 TI2V 5B, Wan 2.1 T2V 1.3B and
  Wan 2.2 T2V / I2V A14B.
- Each model has several sizes: GGUF quants (Q2–Q8) and **INT8 ConvRot** builds, which are close to full quality
  and fast on NVIDIA RTX cards (20-series and newer).
- **Best for my GPU** (the default list) shows only models that fit your VRAM and picks the right size for each.
  *All models* shows everything, marked green / yellow / red.
- One click downloads the model with its VAE and text encoder into `Models\Images`. The image engine
  (stable-diffusion.cpp, CUDA / Vulkan / CPU) installs itself into `Engine\sd`.
- Text-to-image, image-to-image (with strength), reference image editing for models that support it,
  text-to-video and image-to-video. Results go to the `Outputs` folder and show in a gallery.
- **🖼 Image in chat:** turn on the 🖼 Image switch and your message becomes a picture right in the conversation.
  The chat model first turns your words (Greek too) into a detailed English prompt. An attached photo becomes the
  starting image. Choose the model in Tools → Image in chat.
- **Shares one graphics card:** before drawing, LocalMind waits for any running answer, unloads the chat model if both
  don't fit in VRAM, runs the image model, and reloads the chat model on your next text message. You can also choose
  to always unload it or to keep it loaded (Tools → Image in chat → Graphics card memory).
- INT8 ConvRot files that leave out the rotation group size are fixed automatically after download, so
  stable-diffusion.cpp can load them (ComfyUI's default, 256, is written in).
- Release builds can be code-signed through SignPath (see docs/CODE-SIGNING.md).

## 2.3.0

### New
- **Greek interface:** menus, windows, buttons, messages and the chat view are available in Greek
  (Settings → Chat → Interface language; "auto" follows Windows).
- **Compare models:** Tools → Compare models… asks 2–3 models the same question and shows the answers side by side,
  with speed, total time and time to the first word.
- **Faster generation:** speculative decoding gives the same answers, produced faster. Models that provide an
  MTP speed-up file download it automatically and use it. Other models use n-gram drafting, which needs no extra file.
  Can be changed in Settings → Built-in engine → Faster generation.

## 2.2.0

### New
- **Chat with your documents:** in Tools → My documents, add folders or files (PDF, Word, text, Markdown, HTML, code…)
  and press *Index now*. Turn on **📚 Docs** and answers use your files and cite them, for example "[1] report.pdf, p. 3".
  A small embedding model (EmbeddingGemma, 318 MB) is downloaded on first use. It understands meaning across
  languages, so a Greek question finds English notes.
- **Web search:** turn on **🌐 Web** for current information with linked sources. DuckDuckGo needs no key and falls back
  to Wikipedia automatically. Brave Search (free key) and SearXNG are also supported (Settings → Web & voice).
- **Voice input:** **🎤 Speak** records your microphone, and Whisper turns it into text offline. It uses large-v3-turbo
  on NVIDIA GPUs and small elsewhere, and the language is detected automatically, Greek included.
- **Read aloud:** 🔊 Read under each answer, or Tools → Read answers aloud. Uses Windows voices and switches
  to a Greek voice for Greek text.
- The phone page has 📚 and 🌐 switches too.

## 2.1.0

### New
- **Fully portable:** the engine goes in `Engine\`, models in `Models\`, and settings, chats, personas and prompts
  in `Data\`, all next to `LocalMind.exe`. Existing data is moved or copied there automatically.
- **Math rendering:** formulas display properly (KaTeX, embedded, works offline), in the desktop app and on the phone page.
- **Auto-update:** checks GitHub Releases daily (and from Help → Check for updates) and updates in one click.
- **Auto context size:** each model gets the largest context that fits in VRAM (minimum configurable).
  The chosen size is shown when the model loads.
- **Speed test:** Model Manager → Installed → ⚡ Speed test measures generation speed, reading speed, load time
  and context for each model.
- **Tray icon and quick-ask hotkey:** press Ctrl+Alt+Space (changeable) anywhere to ask a question in a small window.
  Minimizing (or optionally closing) the window keeps LocalMind in the tray.
- **Prompt library:** the 📝 Prompts button inserts saved prompts, and `{text}` is replaced by what you typed.
  Several useful prompts are included.
- **Chat folders:** filter the chat list by folder, and right-click a chat → Move to folder.

### Improved
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
