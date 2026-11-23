# Changelog

## 2.14.3

### Changed
- LocalMind no longer loads the chat model into memory when it starts. The loading screen checks for updates,
  updates the model list and loads the interface. The model loads with your first message, or when you pick a model
  yourself. A refresh of the model list no longer loads a model either.

## 2.14.2

### Fixed
- "Jinja Exception: System message must be at the beginning" with some models (Qwen 3.5, Gemma, Mistral…). LocalMind now
  sends one system message at the start; notes added later (web results, document excerpts, search queries) go
  in front of the question they belong to.

## 2.14.1

### Fixed
- Chat: a plain request for a picture or a video ("make me a picture of…", "φτιάξε μου μια εικόνα…") stopped with
  "Accessed JObject values with invalid key value". The requested tool is now picked correctly.
- The model is really required to use the asked-for tool: the setting was always overwritten with "auto", so small
  models could still answer "I can't".

## 2.14.0

### Changed (same colors, a more professional look)
- Crisp monochrome Windows icons (Segoe Fluent Icons) instead of the colored emoji on buttons, toolbars, menus and
  tabs.
- Buttons have rounded corners, a thin border and clear hover and pressed states. Text is never cut off.
- Tabs are flat, with no white or boxy borders: the chosen tab has a soft highlight and an accent line.
- The Windows 11 interface font (Segoe UI Variable), with Segoe UI on Windows 10.
- Tidier toolbars: the Studio's Film editor and output-folder buttons sit beside the gallery, and the Model Manager's
  hint text is shorter (details in its tooltip).

## 2.13.0

### New
- **30 looks** (Settings → Appearance): dark and light themes. Each one changes the colors, the font and the contrast
  of every window and of the chat, with a live preview. Messages and questions now use the app's look instead of the
  white Windows box. Scrollbars are dark on dark looks.
- **Loading screen at start:** 1. checks for a LocalMind update and installs it (then starts the new version),
  2. updates the model list, 3. loads the interface, 4. loads your chat model. Each step shows its result. It can be
  turned off in Settings → Appearance.
- **New video models** that the engine can now run: 112 video models in total (48 before).
  - **MiniMax H3** (Hailuo, open weights): video with stereo sound, from text, a start picture, or a first and last
    picture. Light (4B encoder) and best (32B encoder) versions, an uncensored encoder, and **HyperFlow H3 Flash**
    (6 steps).
  - **H3 Ref2VA:** a new task, "🧩 Video from reference pictures", that puts the people, places and things of your
    pictures (or your 🎭 cast) into the video.
  - **LTX-2.5** distilled (8 steps) and dev, with sound.
  - **HunyuanVideo 1.5** text→video and picture→video at 480p and 720p (normal and fast CFG-distilled).
  - **LingBot-Video 1.3B** (tiny), and the community quantizations of all of these.

### Fixed
- The files list on a model page (Pictures & video) showed only one row and a white scrollbar on high-DPI screens.

## 2.12.0

### New
- **🎬 Film editor** (Studio → 🎬 Film editor, or right-click a result → 🎬 Add to film): put clips and pictures in
  order, choose how long each picture shows, the film size (wide, phone/tall, square), fades between clips, music
  (looped and faded out at the end) and voice-overs at any second. Export one MP4. Films can be saved and opened again.
- **🗣 Conversations:** in a talking-character video, write one line per speaker (`@Anna: Hello!` / `@Max: Hi!`).
  Each character speaks with their own picture and voice, and the pieces are joined with their sound.
- **🎭 Cast voices and LoRAs:** give a character a saved (cloned) voice and their own LoRA. The voice is used when they
  talk. The LoRA is added automatically whenever they appear and the model family matches.
- **📸 Make more pictures** of a cast member (other angles, expressions, light), made with a picture model that
  combines pictures.
- **📦 LoRA training set** for a cast member: every picture with a caption and a trigger word, ready for OneTrainer,
  kohya_ss, ai-toolkit or Civitai's online trainer.
- **🔍 History and ★ favourites:** search all your creations by prompt, model, task or @name, and show only favourites.
- **🌙 Overnight queue:** "Start when I'm away" waits until the PC has not been used for 5 minutes, then runs the queue.
  "💤 Sleep the PC when all is done" puts it to sleep afterwards. A notice appears on the PC (and on the phone page)
  when the jobs finish.
- **💬 Subtitles** (right-click a video): Whisper writes them with timings, either in the spoken language or straight
  into English. Other languages are translated by the chat model. They can be burned into the picture or added as a
  track that players can turn on and off. The .srt file is saved next to the video.
- **📱 Create from the phone:** a 🎨 button on the phone page makes pictures and videos on the PC (with @cast names),
  shows your creations with search and ★, and shows a notice when the PC finishes a job.

### Fixed
- Extending a video made the new part far too small (a rounding mistake in the size). It now keeps the video's shape
  at about the chosen size.

## 2.11.0

### New (Image & video studio)
- **📽 Storyboard:** write one shot per line (or write an idea and press ✨ Improve prompt to get the shots). LocalMind
  makes every shot and joins them into one video. Each shot starts from the last frame of the shot before, so the
  people and places carry on. With 🎭 Cast, a Wan VACE model gets the same cast sheet in every shot, and photo → video
  models start from an opening scene that has the cast in it.
- **🎯 Video between two pictures:** you choose the first and the last frame, and the model makes the motion in between
  (Wan 2.2 I2V A14B, or the new Wan 2.1 FLF2V 14B).
- **🗣 Talking character:** a picture plus words (spoken with the default voice or one of your cloned voices) or a
  recording. The new Wan 2.2 S2V 14B model moves the lips and face to match the voice. Long speech is made in
  5-second pieces and joined, and the voice is added as the sound.
- **⏩ Extend a video:** continues any video from its last frame (right-click a video in the gallery → Extend).
- **✨ Enhance a video:** twice the size (frame by frame with an AI enlarger such as Real-ESRGAN if one is downloaded,
  otherwise a sharp plain enlargement) and / or twice the frame rate for smoother motion. Keeps the sound.
- **✨ Improve prompt:** a chat model rewrites your idea for the chosen model: tags for SDXL / SD 1.5, sentences for
  FLUX / Qwen / Z-Image, motion and camera for video, or a list of shots for a storyboard. @Names stay as they are.

### New (chat)
- **🎭 Cast in the chat:** "make a picture of @Anna at @Old harbor" or "a video of @Max running" uses the saved look.
  The description is added to the prompt. Picture models that combine pictures get the cast pictures, and for videos
  an opening scene with the cast is made first and then animated.

### Notes
- Extending a video joins only the picture: the sound of the original is not kept.
- Sharpening frame by frame with an AI enlarger is slow on long videos. Without one, Enhance takes seconds.

## 2.10.0

### New
- **🎭 Cast — characters and locations** (Image & video studio → 🎭 Cast…): save the people and places of your
  story once (a name, a short description of what must stay the same, and 2–3 pictures), then reuse them in every
  picture and video so they look the same from shot to shot.
  - Tick who and where to use, or just write **@Name** in the prompt (e.g. “@Anna walks along @Old harbor at
    sunset”). The name is replaced with its description automatically.
  - **Describe from picture** writes the description for you with a chat model that can see pictures.
  - How the cast is used depends on the model:
    - Picture models that combine pictures (Qwen-Image Edit, FLUX.2 klein, Qwen-Image 2.1): the cast pictures are
      given as references.
    - Wan VACE video models: a reference sheet with the whole cast is used.
    - Photo → video models: LocalMind first makes the opening scene with the cast (using an installed
      picture model that can combine pictures), then animates it.
    - Text → video models: the descriptions keep the characters and places consistent.

## 2.9.1

### New
- **✔ Only models that fit my GPU** (Model Manager, both the chat and the Pictures & video tab): lists only models
  that run fully on your graphics card with the file size picked for them; works together with every other
  filter (category, uncensored, search…). On a PC without a graphics card it shows everything that runs.
- More polished Model Manager: cleaner header, flat tabs with a blue underline, taller rows and roomier columns,
  quieter grid lines, tidier downloads area.

## 2.9.0

### New
- **Model pages** (Model Manager → 🎨 Pictures & video): click a model to see, like on Civitai:
  - **Sample pictures** (from the Hugging Face model card, the base model, or the matching Civitai model); hover for
    the prompt and settings, click to enlarge.
  - **The settings LocalMind uses:** steps (and ⚡ fast-mode steps), CFG, sampler, flow shift, size, video length,
    CLIP skip / v-prediction, words added to the prompt, negative prompt.
  - **What the author recommends** (steps, CFG, sampler, CLIP skip — read from the description), trigger words,
    likes, downloads, license, base model, last update, description and links to Hugging Face and Civitai.
  - The model's files with size and how each runs on your PC — click one to choose it.
- **Steps column** in the list (click it to sort).
- **🔓 Show NSFW / uncensored** switch: uncensored picture and video models appear in every list and their sample
  pictures are shown unblurred (otherwise NSFW samples are blurred until you click them). Civitai lookups use
  civitai.red when it is on.
- **More uncensored models:** 1,159 picture & video models now (173 uncensored). Repos that hold several models
  (e.g. collections of FLUX.2 klein or Chroma merges) are split into one entry per model.

## 2.8.0

### New
- **Pictures & video models in the Model Manager:** a new **🎨 Pictures & video** tab lists every picture,
  picture-editing and video model, with what each can do (create, edit, text→video, photo→video, video editing),
  the best file for your PC (or pick one, e.g. INT8 ConvRot or a GGUF size), download size, how it runs on your PC
  and popularity. Click column titles to sort; filter by type, search, or show only uncensored or installed ones;
  tick models and download them in one go.
- **About 22× more picture and video models: 775** (was 35):
  - Community **GGUF** models from Hugging Face for FLUX.1, FLUX.2 klein, Qwen-Image (2512, 2.1, Edit), Z-Image,
    Krea 2, Chroma, Anima and Wan (2.1 / 2.2 text→video, photo→video, VACE; SkyReels, FusionX, Turbo…).
  - **INT8 ConvRot** models of all those families (e.g. Krea-2 Turbo/Raw, Qwen-Image-Edit-2511 Lightning,
    FLUX.1-dev, flux.1-krea-dev, Z-Image-Turbo, Chroma, Anima, krea2 fine-tunes…).
  - Hundreds of single-file **SDXL** (incl. Pony / Illustrious) and **SD 1.5** checkpoints.
  - New base models: **FLUX.1 dev**, **Wan 2.1 T2V 14B**, **Wan 2.1 I2V 14B 480P / 720P**.
  - Each community model uses the settings and shared parts of its family, so files like the VAE and text
    encoder are downloaded once. Copies of the same file uploaded by different people are shown once.
- The Studio's "★ Best for my GPU" keeps a short list (popular models); "All models" shows everything.

## 2.7.3

### New
- **28 ready-made personas** (pick them in the Persona box): Straight to the point, Ελληνικός βοηθός, Simple Teacher,
  Tutor, Writing Editor, Email Writer, Translator, Storyteller, Copywriter, Social Media Manager, Senior Programmer,
  Code Reviewer, Data & Excel Helper, PC & Windows Helper, Planner, Business Advisor, Brainstorm Partner,
  Devil's Advocate, Chef, Fitness Coach, Travel Planner, Language Partner, Game Master, Picture Prompt Writer,
  Video Prompt Writer, Song Lyricist, Frank & Unfiltered and more. Your own personas are kept; new built-in ones
  arrive with updates, and ones you delete don't come back.

### Fixes
- After an automatic update the status bar keeps saying the new version is installed (it said "Everything is up to date").
- Checked on Windows: an old LocalMind updates itself to the latest release within about a minute of starting,
  and shows 🔄 Restart to update.

## 2.7.2

### New
- Model Manager: **click a column title** (Model, Size, Abilities, Quant, Download, On this PC, Downloads, Updated)
  to sort the list by it; click again to reverse. An arrow shows the current order. Choosing an order in the
  list box (Most popular, Newest…) goes back to that order.
- **Updates are checked at every start** (about 15 seconds after LocalMind opens, when the AI is idle):
  LocalMind itself, the AI engines and the model list.

## 2.7.1

### New
- **14× more chat models:** about 9,900 models (was 689) from 36 sources on Hugging Face: the main quantizers
  (unsloth, bartowski, LM Studio, ggml-org), the AI labs (Qwen, Google, Mistral, IBM, NVIDIA…) and popular community
  uploaders (mradermacher, MaziyarPanahi, QuantFactory, DavidAU, TheDrummer, huihui-ai…), refreshed every day.
- **★ Best that fit my GPU** shows about 3× more picks (157 instead of 54 on a 16 GB card), still only from the main
  quantizers and labs; the **🔓 Uncensored** category has its own list of up to 200.
- **Ollama-style names** in the search box: type `Qwen3.8-27B-Uncensored:iq4_xs` (or `hf.co/user/model:Q4_K_M`) and
  that model is found with that file size picked (and ticked when it is the only match).
- Typing a name searches all models, not only the best ones for your GPU.

### Fixes
- Model Manager: the toolbar wraps instead of cutting off **⟳ Update list** at the default window size.
- Dark headers in the "Available files" list; the quant column shows its choices when clicked.

## 2.7.0

### New look
- **Cleaner, easier main window:**
  - Top bar holds only what the AI is (model, persona) and the big features (🎨 Pictures & video, 🎵 Music & voice, 📱 Use on phone).
  - Everything about the next message sits in one **message box**: 📎 Attach (document, picture or any file),
    📝 Prompts, and clear on/off options 💭 Think first, 🌐 Web search, 📚 My documents, 🖼 Picture mode.
  - Send turns into Stop while the AI writes; Talk, speak, retry and clear sit next to it.
  - Every button explains itself when you point at it.
- **Welcome screen:** three simple steps on first start; once a model is ready, one-click ideas
  (write, explain, summarize, make a picture, a video or a song).
- Modern dark theme everywhere: dark title bars, menus, drop-down lists and scrollbars; calmer status bar; tidy sidebar.
- The window fits small screens, and the top bar and message options shorten their names on narrow windows.

### Automatic updates for everything
- **LocalMind installs its own updates** in the background and uses them from the next start
  (or press 🔄 Restart to update). You can turn it off in Tools → Settings → Chat → Updates.
- The AI engines (llama.cpp, stable-diffusion.cpp, whisper.cpp, FFmpeg) and the model list are checked every day
  and updated when the AI is idle.
- Help → **Update everything now**.

## 2.6.1

### New
- **Uncensored pictures and video:** new **🔓 Uncensored** list in the Studio (next to *Best for my GPU* / *All models*):
  - Pictures: Chroma1-HD and Chroma1-Flash (FLUX-based), RealVisXL V5 (photo), Pony Diffusion V6 XL, NoobAI-XL 1.1 (anime),
    Realistic Vision V6 (SD 1.5, runs on 4 GB GPUs).
  - Video: Wan 2.2 I2V Uncensored (animate a photo; reuses your Wan 2.2 I2V files and adds a small LoRA),
    NSFW Wan 14B (text to video), Wan 2.2 VACE Fun Uncensored (video editing).
  - Pony and NoobAI add their quality tags to your prompt automatically; ⚡ Lightning/LCM fast mode on the SDXL and SD 1.5 ones.
- **Videos from the chat:** say *"generate me a video of…"* (or *"φτιάξε μου ένα βίντεο…"*) and the video appears in the chat,
  on the PC and on the phone (MP4). Attach a photo to animate it.
- **Pictures and videos are made when you plainly ask**, even with small chat models that used to answer *"I can't make images"*.
  Questions like *"how do I make a video in Premiere?"* still get a normal answer.

### Fixes
- Model Manager: the 🔓 Uncensored category was empty in *★ Best that fit my GPU* — it now shows the best uncensored chat models that fit your GPU.

## 2.6.0

### New
- **New LocalMind icon** (windows, taskbar, tray) and logo.
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

## 2.0.0

### New
- **Built-in llama.cpp engine.** The app installs and updates the right build for
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
