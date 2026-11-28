# Changelog

## 2.26.5

### Changed
- **Back up everything** never copies model or LoRA files. It keeps their download links instead.
  - Before saving, it looks up LoRAs added from a file on Civitai to get their links.
  - It lists any LoRA that still has no link, so you know to keep that file yourself.
- **Restore from backup** downloads the missing models and LoRAs **automatically** from their links. You no longer
  have to pick them in a list. You can follow the downloads in Get models → Downloads.

## 2.26.4

### Added
- **Where your LoRAs came from.** LoRAs you added from a file are now looked up on Civitai by their file
  fingerprint (SHA-256). When Civitai has the file, the **Source** column in Model Manager → Installed shows the
  Civitai model and version. LocalMind also fills in:
  - the base model;
  - the trigger words, when the LoRA had none;
  - the download link, so **Export my setup** can include the LoRA.

  Each file is looked up once. LoRAs Civitai doesn't know show "(not on Civitai)". **Rescan** looks those up again.
  Double-click a LoRA to open its Civitai page.

## 2.26.3

### Added
- **Export songs and voice clips as MP3 or MP4.** Each song and voice clip in the chat now has an
  **Export (MP3 / MP4)…** button, and the Library has a new **Export…** button. Pick one of these formats in the
  save window:
  - MP3;
  - MP4 video with a moving waveform, ready for YouTube or social media;
  - the original WAV.

## 2.26.2

### Added
- **Back up everything (with API keys)** in the File menu saves one `.lmbackup` file with every setting (API keys,
  PINs and tokens included), profiles, LoRA settings, personas, characters and their pictures, saved voices,
  prompts, chats and documents. Models are not copied because they are too big. The backup lists them instead.
- **Restore from backup** in the File menu. LocalMind restarts and puts the backup back before it loads anything.
  Your current data is kept in a folder ending in `-before-restore`. After the restore, LocalMind offers to download
  any models from the backup that are missing on this PC.
- **Songs in a cloned voice.** You can ask "make a song about … by cloning this voice" and attach a recording.
  LocalMind now makes a song instead of only a spoken clip. The music models can't sing in someone else's voice, so
  it works like this:
  - the music model makes the backing track;
  - the voice model performs the lyrics in the copied voice, section by section;
  - the two are mixed into one song.

  When you don't give lyrics, the chat model writes them in the language you asked for and keeps names as you
  wrote them.

### Fixed
- While music, pictures or video were being made, the status bar said "Engine idle (no model loaded)". It now says
  the chat model was unloaded to make room and that your request is still running.
- The spinner under a finished answer no longer keeps turning at "Done (100%)".
- Music and voice progress now shows the time spent. Some models print no progress for minutes, which made the
  work look stuck at 2%.

## 2.26.1

### Added
- **Export my setup now saves everything needed to set up another PC the same way:**
  - chat, picture, video, music and voice models, and LoRAs downloaded from Civitai;
  - profiles and which ones the chat uses;
  - your own personas;
  - the settings for chat, engine, memory, pictures and voice.

  It never saves API keys, PINs, folders, the language or the theme. The recommended-models window can apply these
  settings and personas too.

### Changed
- Music models: only MiniMax Music 3 and YuE2 again. The four extra editions added in 2.26.0 are removed.

### Fixed
- Civitai models that only have preview **videos** (many MiniMax H3 models) now show a still picture in the list
  and on their page instead of an empty square.
- Downloads started in the Model Manager keep going after you close it. You can still cancel them in the Downloads
  list.

## 2.26.0

### Added
- **Recommended models.** At the first start LocalMind shows a ready-made set of models for your graphics card (8 GB
  or 16 GB), all ticked, with their sizes. Untick what you don't want and press **Download selected**.
  - Chat models get the size that fits your card; picture and video models get the largest edition that fits.
  - The set also brings ready-made profiles: pictures, editing, video, music and voice.
  - The lists live in the repository (setups/8gb.json, setups/16gb.json). When a newer version is published,
    LocalMind offers the new models once.
  - **Models → Recommended models…** opens the list at any time.
- **Models → Export my setup…** saves everything installed here (chat, picture, video, music and voice models,
  LoRAs) and your profiles in the same format, ready to become a recommended set.
- **Music and voice profiles.** A music profile has a model, a default style and words always added to the style;
  a voice profile has a voice model and a saved voice to speak with. Choose them in Profiles or Models → Select
  models for chat.
- **More music models:** MiniMax Music 3 compact (for 8–12 GB cards), balanced (12–16 GB) and full quality, and
  YuE2 3B full quality.
- **Delete any message**, answers included (🗑 under each answer).
- **Progress in the chat:** loading, drawing, composing and their percentage now show right under the answer being
  made, not only in the corner of the window.
- **Every picture type:** Characters and the chat accept WebP, AVIF, HEIC, TIFF, JPEG XL and more (they are turned
  into PNG), and the first frame of a short video (WebM, MP4…). Character pictures added before as WebP now show.

### Changed
- Menus: **Select models for chat** (formerly Pictures & video in chat) is in the Models menu, next to
  **Recommended models** and **Export my setup**. "Get picture & video models" is gone; **Get models** does that.
  Shorter, plainer names throughout.
- The status at the top no longer says "Engine idle" while a picture, video or song is being made. It says what is
  working, and "Ready (no chat model loaded)" when nothing is.

### Fixed
- **Coloured-noise videos from some INT8 models** (for example Singularity × 10Eros Max INT8). These files keep their
  quantization settings in the file's metadata, which the engine doesn't read, so their rotated (ConvRot) weights
  were taken as plain INT8. LocalMind now adds the settings in the form the engine reads, once, without
  downloading the file again.

## 2.25.0

### Added
- **Edit pictures from the chat.** Attach a photo or refer to a picture made earlier in the chat ("change the sky in
  that picture to orange") and it is changed instead of a new one being made. The new **✏ Edit profile** (Model
  Manager → Profiles) sets the default editing model with its LoRAs and prompts. You can also choose it in Tools →
  Pictures & video in chat.
- **Extend a video from the chat.** After "generate a 6 sec video of a bird flying", write "extend the video by 6
  seconds, the bird now flies to the moon". A new clip continues from the last frame of the video. Under it, **🔗
  Merge both into one video** joins them, and **💾 Export…** saves the merged video wherever you want.
- **🗑 Delete** on your messages (next to Edit and Copy) removes the message and its answer.

## 2.24.0

### Added
- **Profiles** (Model Manager → Profiles). A profile is one of your downloaded picture or video models, plus:
  - its LoRAs, each with its own strength;
  - trigger words;
  - a default prompt (used when you don't describe the picture), a positive prompt (always added) and a negative
    prompt.

  Mark one picture profile and one video profile as the chat's defaults, here or in Tools → Pictures & video in
  chat. Installed → **🧩 New profile** makes one for the selected model.
- **🧩 LoRA category** in the Model Manager: browse and install LoRAs from Civitai, then use them in profiles.
  **My LoRAs** (add files, edit trigger words) opens from the profile editor.
- **🖼 Library**: every picture, video, song and voice clip you made, with the prompt that made it (and the model,
  size, seed, negative prompt and lyrics). You can search the prompts and:
  - open a file, copy its prompt, put the prompt back in the chat, or attach the file to the chat;
  - enlarge a picture 4× or remove its background;
  - make a video sharper and smoother;
  - delete files.

  Songs and voice clips now save their prompts too.
- **🎭 Characters** (formerly Cast) with voices:
  - Give a character an MP3 / WAV / M4A file and its voice is cloned automatically.
  - **🎙 New voice from a file** saves a named voice you can use as @name.
  - In the chat: "@Anna using @Grandpa says: good morning". The video uses Anna's pictures and the voice, with a
    model that takes reference pictures and voices (MiniMax H3 Ref2VA, e.g. 10Eros Max Ref2VA).
- **Videos from pictures in the chat**: "make a video from that picture" uses the last picture made in the chat
  (even in an earlier answer), and an attached photo is used as before.
- **Length and size from the chat**: "a video of 3 seconds at 0.2 MP" now makes 3 seconds at about 0.2 megapixels
  (576×352). "A 2 MP picture" works too. The chat shows the size and length while the video is made.

### Removed
- The Pictures & video studio, the Music & voice studio and the Film maker. Pictures, videos, songs and voices are
  made from the chat, and the Library holds the results.

### Fixed
- **Reduce system RAM usage now really lowers RAM use.** The 2.23 version only stopped the RAM from being locked; the
  model was still copied into RAM, and the engine fills almost all free RAM with it. Now, when a model doesn't fit
  on the graphics card, its parts stay on the disk and are read when needed (Windows caches them and frees that
  cache by itself). It is on automatically for cards with 16 GB or less. If an engine build can't do this,
  LocalMind falls back to keeping the model in RAM.
- Library and Profiles: buttons are no longer cut off on small screens.
- Editing a sent message: Send now always does something. The new text appears at once, the old answer goes away
  and the new one is written. If an answer is still being written, the edit box says so.

## 2.23.1

### Added
- **Full CUDA 13 support.** With the GPU backend set to CUDA 13, everything runs on CUDA 13:
  - chat (llama.cpp);
  - pictures and video (stable-diffusion.cpp);
  - speech-to-text and subtitles (whisper.cpp).

  Those two projects only publish Windows CUDA 12 builds, so LocalMind now builds them for CUDA 13 itself, every
  week, from their latest releases. The builds support RTX 20xx through RTX 50 and include the CUDA runtime.
  LocalMind downloads them by itself. If a CUDA 13 build isn't available yet, it uses the CUDA 12 one, which runs
  on the same cards.
- Fast attention for pictures and video is now on with CUDA 13 too.

### Fixed
- Installing a GPU build of speech-to-text no longer removes the CPU build it falls back to.

## 2.23.0

### Added
- **Edit a message you sent, like in Claude chat.** Point at one of your messages and press **✏ Edit**. Change
  the text and press **Send** (or Enter; Shift+Enter for a new line, Esc to cancel). Everything after that message
  is dropped and the answer is written again. Pictures and files attached to the message stay with it. You can't
  edit while an answer is being written; press Stop first. **⧉ Copy** copies the message.
- **Change persona from the chat.** For example: "your persona is image prompt writer and generate me an image of a
  tree on a mountain. After you do that your persona is video prompt writer and, using that image, create an
  earthquake that destroys the mountain".
  - Each named persona writes the prompt for its own step. A saved persona is used when the name matches (for
    example "Simple Teacher"); otherwise a new role is made for it.
  - The steps run one after another: the picture first, then the video starts from that picture.
  - The chat shows who wrote each prompt (🎭 name: prompt).
  - "From now on your persona is …" switches the chat's persona. A new role is saved to the persona list.

- **Reduce system RAM usage** (Settings → Built-in engine). For pictures and video, the engine no longer locks a
  large block of RAM for the model, and the model file is read as needed. Recommended for 16 GB graphics cards,
  where that locked RAM can fill your memory and stall video generation. Automatic (the default) turns it on for
  cards with 16 GB or less; you can also set it on or off.

### Changed
- Switching the GPU backend (for example CUDA 12 → CUDA 13) removes the files of the previous backend once the new
  one is installed. The picture engine does the same.
- The CUDA 13 choice now says it is the best one for RTX 50 cards.

### Fixed
- Model descriptions no longer show "Your browser does not support the video tag." where a model page had videos.
  Stray indentation and blank lines are also cleaned up.
- Int8 MiniMax-H3 checkpoints (for example Singularity × 10Eros Max int8) that failed with "tensor … weight_scale
  has wrong shape: got [1, N, 1, 1], expected [N, 1, 1, 1]". LocalMind corrects the shapes in the file header
  (the file keeps its size) and tries again automatically.
- The last setting on a Settings page was cut off at the bottom.

## 2.22.1

### Changed
- **Deleting a model removes everything it used that nothing else needs.**
  - **Picture and video models:** the model file, its VAE / audio VAE, its text encoder, and its built-in and speed
    LoRAs (also for models installed from Civitai). It also removes unfinished `.part` downloads, repair markers and
    folders left empty. Files another installed model still needs are kept, and they go when you delete that one
    too.
  - **Chat models:** an imported model's vision projector and speed-up file are removed with it (downloaded ones
    already went with their folder).
  - **Voice / music:** both files, their unfinished downloads and the empty folder.


## 2.22.0

### Changed
- **One download list for everything.** Every download appears in the **Downloads** list at the bottom of the Model
  Manager, with an icon for its kind (💬 LLM, 🖼 picture, 🎬 video, 🎵 music, 🗣 voice, 🧩 LoRA). That covers:
  - models from the LocalMind lists and from Civitai;
  - downloads started in the Studio, in Music & voice and from the chat;
  - missing parts fetched when you press Generate.

  Each row shows its progress, speed and status, up to three run at a time, and Cancel / Retry / Clear finished work
  for all of them.
- The other download progress bars and counters are removed: the gallery's bar and "x downloading" line, the
  Music & Voice list's bar, the Studio's and the Music & voice window's download bars, and the main window's download
  counter. Those places now just say the model was added to the download list.


## 2.21.3

### Fixed
- **Asking for a picture or video cut the model's thinking off halfway.** LocalMind made the engine write the
  picture request from the very first word, which stopped a thinking model's reasoning in the middle. With
  **Think** on, the model now thinks to the end and then asks for the picture (or video). If it answers in words
  instead, the picture is still made. With Think off nothing changes.


## 2.21.2

### Fixed
- **Installed showed only chat models.** It now lists everything you downloaded: LLM, picture and video models
  (also the ones installed from Civitai), music and voice models, and LoRAs.
  - A **type** list at the top filters by kind. The total size and the free disk space count all of them.
  - **Delete** and **Open folder** work for every kind. "Use in chat" and "Speed test" are for LLMs, and **Open in
    the studio** is for picture / video models and LoRAs.
  - A model whose main file is downloaded but which still needs a part says "parts download when used". A file
    shared by several models is listed once.


## 2.21.1

### Changed
- **civitai.red shows every picture, NSFW included.** A model's card uses its first picture, whatever its rating.
  Its page now shows **all its pictures** in a row under the big one: click one to see it large, and click the big
  picture to open it full size. On civitai.com only the safe pictures are shown, and a card whose first picture is
  NSFW uses the first safe one instead of staying empty. In the LocalMind list, "Show NSFW" decides the same.
- Civitai key help, step 3: scroll down to "Security & apps" and click "Add API Key" (Civitai's current page).


## 2.21.0

### Changed
- **One "🔑 Civitai API key" button** replaces the key box. You find it at the top of Models → Picture / Video when
  the source is Civitai, in the Civitai window, and in Settings → Built-in engine. It shows ✔ when a key is saved.
  It opens a window that has:
  - what the key is for, and five numbered steps to get one;
  - **Open Civitai account settings**, which goes straight to the right page;
  - a box for the key, with 👁 to show or hide it and 📋 Paste;
  - **✓ Test key**, which asks Civitai whether the key works and shows the account name;
  - **Save** (it checks the key first), **Remove key** and **Cancel**;
  - a note that the key stays on this PC and is sent only to Civitai.
- When a Civitai download needs an account, this window opens by itself. Save the key and press Install again.


## 2.20.3

### Added
- **Save your Civitai API key.** Next to the "Civitai API key" box in Models → Picture / Video (Civitai source) and in
  the Civitai window there is a **💾 Save** button (Enter works too). The key is also in **Settings → Built-in engine**,
  under the Hugging Face token. Once saved it is used for every Civitai download.
- **"How do I get a key?"** explains step by step how to create a free key on civitai.com and can open the page:
  1. Sign in at civitai.com.
  2. Profile picture → Account settings.
  3. API Keys → "+ Add API key".
  4. Copy the key and paste it in LocalMind.


## 2.20.2

### Fixed
- **The LTX-2.5 list looked empty.** Its models have no example pictures on Hugging Face, so every card was a blank
  tile. Cards and model pages now show pictures made with that model family on Civitai (the same picture for a model
  every time). This also helps many MiniMax H3, FLUX and Qwen-Image uploads. The LTX-2.5 list holds 12 models, plus
  2 uncensored ones with "Show NSFW".


## 2.20.1

### Added
- **The original 10Eros Max files from TenStrip/10Eros-Max** are now file choices in the 10Eros models:
  - Turbo beta 7: INT8 HQ, INT8 HQ fix1, BF16, BF16 fix1.
  - Turbo-Hybrid beta 5 and beta 4: INT8 and BF16.
  - Hybrid beta 5: INT8 and BF16.
  - FL2VA beta 2 and Ref2VA / Turbo Ref2VA beta 2: BF16.
- New 10Eros models:
  - **10Eros Max Turbo v1.0 Hybrid**, the newest (INT8 HQ or BF16, 8 steps).
  - **10Eros Max Raw beta 7**: no built-in turbo, best sound.
  - **Singularity × 10Eros Max PRISM**, the most downloaded 10Eros merge (INT8, INT8 DMAD-PDMD, FP16).
  - **Singularity V2 × 10Eros Max PikaFlex** and **Singularity V2 × 10Eros Max Turbo**.
- More file choices: CornLogic's 10Eros INT8 test4, electroglyph's Turbo-Hybrid beta 3 GGUF, PinkCherry hybrid v1.8
  and v1.5 INT8.


## 2.20.0

### Changed
- **Picture and Video look like Civitai.** Models → Picture and Video are now galleries: a picture for every model,
  its page on the right (a large picture, size, whether it fits your GPU, description) and an Install button.
  - **Source** at the top: the LocalMind list (Hugging Face models, with LocalMind's settings), **Civitai**, or
    **Civitai.red** (everything, 18+).
  - With Civitai you choose models (checkpoints) or LoRAs. Video shows MiniMax H3 and LTX-2.5; Picture shows every
    picture type LocalMind runs.
  - Family, sort and search filters; "More…" loads the next page.
  - **Several installs at once**: press Install on one model, pick another and press Install again. The button reads
    "Cancel" while that model downloads, and the bottom line shows how many are downloading.
  - Installed models show ✔ and can be deleted from their page.
- The Civitai window of the studio and the LoRA list uses the same gallery.
- Music and Voice keep their short table.


## 2.19.0

### Added
- **Video models from Civitai: MiniMax H3 and LTX-2.5.** In Models → Video, "🌐 Browse Civitai" opens on all video
  checkpoints (DaSiWa MiniMax H3, Singularity × 10Eros Max, H3 Remix, Joy-LTX 2.5 and others). Install one with one
  click; it appears in the video list and the studio and uses the matching MiniMax H3 or LTX-2.5 parts.
  - Ref2VA models (people from pictures) are recognised, and turbo / 8-step versions get 8 steps.
  - LTX-2.5 dev and distilled versions get their own settings.
  - INT8 / ConvRot, GGUF and FP8 files are used. Formats the engine can't run (NVFP4, INT4, W4A8) are skipped.
- **Picture models from Civitai:** "All picture models" lists every type LocalMind runs: SDXL / Pony / Illustrious,
  SD 1.5, FLUX.1 (dev, Krea, schnell, Kontext), FLUX.2 klein, Z-Image, Qwen-Image (also 2.1), Krea 2 and Anima.

### Changed
- The Civitai browser's model list starts with "All picture models" and "All video models". Wan and LTX-2.3 video
  checkpoints are no longer listed.


## 2.18.2

### Fixed
- **Switching between Picture and Video several times listed the wrong models.** The kind of list could stay on
  Video after you went back to Picture (or the other way round). It now always matches the category you pick.

### Added
- **🌐 Browse Civitai** in Models → Picture and Video. It opens the Civitai browser: civitai.com, or civitai.red for
  the full catalogue (18+). Search checkpoints and LoRAs, see their pictures and install them with one click. They
  then appear in the list and in the studio.

### Changed
- "Recommend for my PC" is removed from the LLM list.


## 2.18.1

### Fixed
- **Model lists showed the wrong model.** After a list changed (a search, the GPU filter, Update list, or switching
  between LLM / Picture / Video / Music / Voice), the page on the right could still show an earlier model, and
  picking a file there could change a different row. The page now always shows the selected row's model, and a
  file you pick applies to that model.
- **"Update list" failed** with "Index was out of range… (Parameter 'index')". It happened while the list was being
  rebuilt; it is fixed.
- **The LLM list shows all its models by default**: every Qwen 3.8, Gemma 3 and Gemma 4 model (about 470). Before,
  it opened on "Best that fit my GPU", which hides models that don't fit fully, such as Qwen 3.8 27B on a 16 GB
  card. "Only models that fit my GPU" still narrows it.
- The file list on a model's page no longer gets a horizontal scrollbar that hides its rows.


## 2.18.0

### Changed
- **Simpler Model Manager.**
  - Two tabs: **Models** and **Installed**.
  - Models has one bar of categories: **LLM · Picture · Video · Music · Voice**.
  - The category and "Most popular" lists, and the picture/video type list, are gone. Each list has only the
    search box, "Best for my GPU" and two checkboxes, with plain text and no extra icons.
- **LLM list: every Qwen 3.8, Gemma 3 and Gemma 4 model** (official and community versions), about 470.
- Music and voice models have their own categories. The NSFW checkbox shows only where it applies.

### Fixed
- The "Available files" list on a chat model's page could show only its header and a scrollbar. It now fits the
  width.


## 2.17.2

### Fixed
- "The instance of CoreWebView2 is uninitialized" when a chat was clicked in the list while the chat view was still
  starting. The chat is now shown as soon as the view is ready, and nothing in the main window writes to the chat
  view before that.


## 2.17.1

### Changed
- **Chat model list: about 200 models instead of 3,300.**
  - The official Qwen and Gemma 3 / 4 releases are listed, from Qwen, Google, unsloth, ggml-org, LM Studio and
    bartowski's copies of them.
  - The well-known uncensored versions are listed too: abliterated, uncensored and heretic ones with 40,000+
    downloads, such as Huihui Qwen3.8 27B abliterated and Gemma 4 uncensored heretic.
  - Other community fine-tunes are no longer listed. Models you downloaded stay, and "Search all of Hugging
    Face" still finds anything.


## 2.17.0

### Changed
- **Shorter model lists: only the families worth using.**
  - Chat: **Qwen** and **Gemma 3 / Gemma 4** (with their fine-tunes), about 3,300 instead of 9,900.
  - Pictures: **FLUX** (FLUX.1, Kontext, FLUX.2), **Krea 2** and **Qwen-Image** (also Edit).
  - Video: **LTX-2.5** and **MiniMax H3**, with its fine-tunes (10Eros, PinkCherry, AfterMidnight, HyperFlow).
  - Kept as tools: the picture enlargers and the background remover.
  - Models you already downloaded always stay listed, whatever their family. "Search all of Hugging Face" still
    finds anything.
- The "⭐ Top … most popular" selector is gone.
- The Studio only offers tasks that a listed model can do. Talking character and Edit / restyle a video needed
  Wan, so they appear only if you already have such a model.


## 2.16.2

### Fixed
- **"prompt encoding failed" with 10Eros Max.** The 10Eros, PinkCherry and AfterMidnight MiniMax H3 models used the
  32B Sakura text encoder (14 GB). Together with the video model that is more than a 16 GB graphics card and
  32 GB of RAM can hold, so the prompt could not be read. They now use the compact 4B MiniMax H3 text encoder
  (the video model itself is what makes them uncensored). "MiniMax H3 Uncensored (Sakura encoder)" still uses the
  32B one, for PCs with lots of memory.
- When a model's files are larger than the free RAM, the engine now reads them from the disk as needed
  instead of failing to load them.
- A model whose main file is downloaded but which now needs another part (like the encoder above) is still
  used by the chat; the missing part downloads by itself when you generate.
- Error messages show the engine's real error lines, not only "generate failed". The full engine output of the
  last failed run is saved to `Data\engine-last.log`.


## 2.16.1

### Fixed
- **"generate failed: cannot load component" with 10Eros Max (MiniMax H3) and other community GGUF models.**
  Files made with the ComfyUI-GGUF converter store a few small tensors flattened, with their real shape written
  in a note that only ComfyUI reads, so the engine refused them. LocalMind now gives those tensors their real
  shape back. Only the file's header changes, in place, in under a second: nothing is downloaded or copied again.
  Already downloaded models are repaired by themselves the next time you press Generate.


## 2.16.0

### Added
- **Clone a voice from the chat.** Attach a recording (MP3, WAV, M4A, OGG, FLAC… or a video), or drop or paste it
  into the message box. Then write e.g. "clone this voice and say: Good morning everyone!" (Greek works too:
  "κλωνοποίησε τη φωνή και πες: …"). The answer is a player with the words spoken in that voice.
  - LocalMind takes a clean 15-second sample of the recording. For models that copy closer with the words of the
    recording (Qwen3-TTS), it writes them down by itself.
  - The voice is kept in your voice library as "Last chat voice". Give it a name ("…and save it as Maria") to
    keep it, then later ask "say … with Maria's voice" without attaching anything.
  - A recording attached earlier in the same conversation can be used again ("now say this in that voice").
  - Needs a voice model that can copy voices (Qwen3-TTS, Chatterbox or VoxCPM2) and 🎵 Music & voice switched on.


## 2.15.0

### Added
- **19 more uncensored video models (🔓 NSFW):**
  - **10Eros Max on MiniMax H3** (video with sound): 10Eros Max, Max Turbo (8 steps), Turbo Hybrid, Hybrid,
    Ref2VA (from pictures or your 🎭 cast) and Turbo Ref2VA.
  - **PinkCherry** on MiniMax H3 (also Ref2VA) and on LTX-2.3; **AfterMidnight** (MiniMax H3 Ref2VA with its NSFW LoRA).
  - **10Eros 1.5 and 10Eros v1 on LTX-2.3** (with a ⚡ DMD 8-step fast mode), LTX-2.3 NSFW motion.
  - **Wan 2.2:** SmoothMix, DaSiWa Midnight Flirt, Enhanced NSFW FastMove and Rapid AIO NSFW v10 (4 steps).
  - The MiniMax H3 ones use the uncensored Sakura text encoder.
- **Most popular only:** every model list (chat, pictures, video, music & voice) has a "⭐ Top 30 most popular"
  selector (15 / 30 / 50 / 100 / all), ranked by Hugging Face downloads. Typing a search still finds every model,
  and the models you installed are always listed.
- **Pictures, videos and music apart:** Models → 🎨 Pictures & video has three buttons: 🖼 Picture models,
  🎬 Video models and 🎵 Music & voice models (songs, read aloud, voice cloning), each with its own kinds.
- **Download many at once:** tick models in the picture, video and music lists (the ticks stay when you switch
  lists) and press Download once: they download at the same time (three at a time; a shared part is fetched once).
  One failing model does not stop the others. Chat models also download three at a time.
- **Default models for the chat:** choose which model is used when you ask the chat for a picture or a video
  (Tools → Pictures & video in chat, or "☆ Make default" in the Studio). "Automatic" picks the best one for your PC.

### Fixed
- The file list on a model's page could show no rows (only a scrollbar line). It now always shows its rows and fits
  the width.

## 2.14.4

### Fixed (full bug and visual check)
- **No more sudden closing:** an unexpected error is now logged to `Data\errors.log` and shown in a message, and
  LocalMind keeps running. Before, any unhandled error closed the app.
- Closing a window (Studio, Film editor, Cast, Music & voice, Civitai, Documents…) while it was still working could
  crash LocalMind when the next progress update arrived. Progress updates now go nowhere once a window is closed.
- The creations history and favourites could get corrupted when the PC and the phone searched them at the same time.
- Settings: all five tabs fit (two were hidden behind scroll arrows), and "Web & voice" no longer shows a double "&".
- List headers stay dark to the right of the last column. Progress bars use the app's colors instead of white.
- Share on network: a checkbox text was cut off.
- The buttons above the message box show only icons on narrow windows instead of overflowing.

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
