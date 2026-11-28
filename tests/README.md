# LocalMind test harness

`LocalMind.Harness` is a console app that compiles LocalMind's non-UI source files and tests them against real
services: Hugging Face, real downloads, and a real llama.cpp server. It runs on Windows or Linux with the .NET 10 SDK.

```bash
cd tests/LocalMind.Harness
dotnet run -- prompts    # tests/prompts.json: real chat messages and the steps they must give (runs on every build)
dotnet run -- unit       # think-tag splitter, quant parsing, Markdown safety, catalog, best-fit list, prompt payload
dotnet run -- live       # live Hugging Face catalog, search, and automatic quant choice for 0–24 GB VRAM
dotnet run -- download   # real downloads into /tmp/oc-models: cancel, resume, SHA-256 check, library registration
dotnet run -- server     # client + LAN server against a running llama-server at 127.0.0.1:18080
dotnet run -- engine     # LlamaEngine end-to-end: start, model switching, thinking, concurrency, LAN /v1 proxy
```

- `download` must run before `server` and `engine`, because they use the models it downloads.
- `engine` expects a llama.cpp build in LocalMind's engine folder
  (`Engine/cpu/llama-server[.exe]` next to the harness executable, in `bin/Debug/net10.0/`). The app installs one there automatically on Windows.
- `server` expects you to start one yourself, for example:
  `llama-server -m Qwen3-0.6B-Q4_K_M.gguf --host 127.0.0.1 --port 18080 -c 8192 -np 2 --no-webui`
- One concurrency check depends on timing: with a tiny model the first answer can occasionally finish
  before the second request arrives.

## Adding a message to tests/prompts.json

When LocalMind reads a chat message wrong (makes a picture you didn't ask for, skips the song), add that message to
`tests/prompts.json` with the steps it should give, in order: `picture`, `video`, `longer-video`, `song`, `voice`, or
an empty list for a normal answer. Every build runs `dotnet run -- prompts`, and a wrong result stops the build, so
the same mistake can't come back in a release.
