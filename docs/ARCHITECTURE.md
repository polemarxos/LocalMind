# Architecture

```
MainForm (WinForms + WebView2)        Phones / other PCs (browser, /v1 clients)
        │                                         │
        └────────────► AppCore ◄──── LanServer (Kestrel :8484, PIN)
                         │
        ┌────────────────┼──────────────────────────────┐
   IChatBackend     HistoryService / PersonaService   ModelLibrary ◄── DownloadManager ◄── ModelCatalog (Hugging Face)
        │
 ┌──────┼──────────────────────┐
BuiltInBackend   OllamaService   OpenAiCompatService
     │
LlamaEngine ── llama-server.exe (127.0.0.1:random port)
```

| File | Responsibility |
|---|---|
| `AppCore.vb` | Shared services and backend selection. Builds the prompt payload (persona, history window, image pruning). |
| `ChatBackend.vb` | `IChatBackend`, request options and results, plus `ThinkTagSplitter` for raw `<think>` output. |
| `LlamaEngine.vb` | Installs and updates llama.cpp from GitHub releases. Starts and stops the engine, waits for `/health`, writes the log. `AcquireAsync` loads a model and registers a request in one step, so the model is never switched mid-answer. A Job Object kills the engine with the app. |
| `OpenAiCompatService.vb` | Streaming `/v1/chat/completions` client. Reads `reasoning_content`, `timings` and `usage`. |
| `OllamaService.vb` | Ollama `/api/chat` client (optional backend). |
| `HardwareInfo.vb` | Detects GPU and VRAM (nvidia-smi, display-adapter registry), RAM and CUDA driver version. |
| `ModelCatalog.vb` | Live and offline catalog, capability tags, VRAM-fit estimate, `PickQuant` (automatic quantization), `BestThatFit` (default list). |
| `DownloadManager.vb`, `Downloader.vb` | Sequential queue, HTTP Range resume, SHA-256 check, `download.json` for resume after a restart. |
| `ModelLibrary.vb` | Scans the models folder (`model.json` per download plus loose `.gguf` files). |
| `LanServer.vb` | Phone page, `/api/*` (JSON + Server-Sent Events) and the `/v1/*` proxy. PIN check with lockout. |
| `MarkdownRenderer.vb` | Markdig (raw HTML disabled) plus the chat view's HTML, CSS and JS shell. |
| `ModelManagerForm.vb`, `SettingsForm.vb`, `LanForm.vb`, `Theme.vb` | User interface. |
| `MediaStudio.vb`, `StudioForm.vb` | Image & video studio. Recipes come from `Resources/media_catalog.json` (built by `tools/build_media_catalog.py`). Each recipe lists variants (GGUF quants, INT8 ConvRot safetensors) plus shared parts (VAE, text encoder). `PickVariant` / `BestForGpu` choose by VRAM and prefer ConvRot on NVIDIA cards with compute capability 7.5 or higher. Generation runs stable-diffusion.cpp `sd-cli` from `Engine\sd\<flavor>` and reads step progress from its output. |
| `ConvRotFix.vb`, `VideoTools.vb` | Fills in the missing ConvRot group size; FFmpeg (downloaded to `Engine\ffmpeg`) reads input videos into frames and converts WebM to MP4. |
| `LoraLibrary.vb`, `LoraForm.vb`, `MaskEditorForm.vb` | Your LoRAs in `Models\Loras` (family and trigger words from safetensors metadata, `Data\loras.json`); the brush for inpainting masks. Speed LoRAs (`speed-…`) live in the same folder. |
| `Civitai.vb`, `CivitaiForm.vb` | Civitai REST API (civitai.com / civitai.red): search LoRAs and checkpoints by base model, install LoRAs with trigger words, register checkpoints as user models (`Data\user_models.json`) built on a catalog recipe. |
| `AudioStudio.vb`, `AudioForm.vb` | Speech, voice cloning and songs with llama.cpp `llama-tts` (Chatterbox, Qwen3-TTS, VoxCPM2, Fish S2 Pro, YuE2, MiniMax Music 3). Catalog: `Resources/audio_catalog.json`. |
| `FeaturesForm.vb` | First-start choice of abilities (chat only / pictures & video / music & voice). |
| `ChatAgent.vb`, `MainForm.Agent.vb` | Tool calling: offers `generate_image`, `make_song`, `web_search` to the model (llama-server `--jinja`), runs them, and falls back to plain answers for models without tool support. Shared by the PC window and the phone page. |
| `ImageTools.vb`, `OnnxTools.vb` | Pixel work (inpaint compositing with a feathered mask, alpha, edges) and ONNX Runtime helpers: BiRefNet-lite background removal and Depth Anything V2 depth maps for ControlNet. |
| `VoiceChatForm.vb`, `VoiceLibrary.vb`, `SoundtrackForm.vb` | Hands-free voice chat (waveIn + simple voice-activity detection), saved voices, AI music / voice-over for videos. |
| `MemoryMonitor.vb` | GPU memory (PDH "GPU Adapter Memory" counters, all vendors) and RAM for the status bar. |
| `Knowledge.vb`, `WebSearch.vb`, `Voice.vb` | Document index (embeddings + cosine search), web search providers, Whisper voice input and SAPI read-aloud. |

## Choosing a quantization

1. **Dense models:** the largest file that fits entirely in VRAM at 3 or more bits per weight.
   Otherwise a 3–5 bit file that runs with help from the CPU.
2. **MoE models:** prefer 4 or more bits, even if the experts spill into system RAM. Only a few billion
   parameters are active per token, so this stays fast.
3. Legacy Q4_0/Q4_1/Q5_x files are skipped when a modern K or IQ quant of similar size exists.

Memory needed is estimated as file size + 0.6 GB + KV cache, where the KV cache is 0.25 × √params(B) × context/8K GB.
Small underestimates are harmless, because llama.cpp's `--fit` moves the overflow to system RAM.
