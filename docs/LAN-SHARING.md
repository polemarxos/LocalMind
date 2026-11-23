# Using LocalMind from other devices

## Phone or tablet

1. On the PC, open **Tools → Share on network** and tick *Share this PC's AI on my network*.
2. On the phone, connect to the **same Wi-Fi** and scan the QR code. It logs in automatically.
   You can also type the address shown (for example `http://192.168.1.20:8484`) and enter the PIN.
3. Tip: use the browser's *Add to Home screen* to get an app-like icon.

Chats you start on the phone appear in the PC's chat list, and the other way round.

### If the page doesn't load

- Press **🛡 Allow through Windows Firewall** in the Share window. Windows will ask for admin rights.
- In Windows, make sure the network profile is **Private**:
  Settings → Network & Internet → Wi-Fi/Ethernet → your network → *Private network*.
- Some guest or hotel Wi-Fi networks block devices from talking to each other ("client isolation").

## Other apps and PCs (OpenAI-compatible API)

| Setting | Value |
|---|---|
| Base URL | `http://<pc-address>:8484/v1` |
| API key | your LocalMind PIN |
| Model | a name from `GET /v1/models` (for example `Qwen3.5-9B · Q4_K_M`) |

This works with Continue, Open WebUI, Jan, scripts that use the `openai` package, and more.

**Another PC running LocalMind:** in Tools → Settings → Backend, choose *OpenAI-compatible server* and enter
`http://<pc-address>:8484` with the PIN. A laptop can then use the desktop's GPU.

## Security notes

- Keep *Require PIN* on. After 8 wrong PINs, that device is blocked for 5 minutes.
- Traffic is plain HTTP, so use this only on networks you trust (your home Wi-Fi).
- The llama.cpp engine itself only listens on 127.0.0.1. Other devices only ever reach LocalMind's own server.
- To let more people chat at the same time, raise **Settings → Built-in engine → Parallel chats**.
