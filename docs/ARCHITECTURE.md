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

## Choosing a quantization

1. **Dense models:** the largest file that fits entirely in VRAM at 3 or more bits per weight.
   Otherwise a 3–5 bit file that runs with help from the CPU.
2. **MoE models:** prefer 4 or more bits, even if the experts spill into system RAM. Only a few billion
   parameters are active per token, so this stays fast.
3. Legacy Q4_0/Q4_1/Q5_x files are skipped when a modern K or IQ quant of similar size exists.

Memory needed is estimated as file size + 0.6 GB + KV cache, where the KV cache is 0.25 × √params(B) × context/8K GB.
Small underestimates are harmless, because llama.cpp's `--fit` moves the overflow to system RAM.
