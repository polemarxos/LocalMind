"""Builds src/LocalMind/Resources/media_catalog.json: image & video models for the Studio.

Every recipe lists the diffusion model variants (GGUF quants and INT8 ConvRot) plus the
shared parts it needs (VAE, text encoder). All files are looked up on Hugging Face so the
app only offers files that exist, with their real sizes.

    python tools/build_media_catalog.py
"""
import json, os, re, sys, urllib.request

_canon = {}
def canonical(repo):
    """Hugging Face renames repos (with a redirect); store the current name."""
    if repo not in _canon:
        try:
            _canon[repo] = json.load(urllib.request.urlopen(f"https://huggingface.co/api/models/{repo}", timeout=60))["id"]
        except Exception:
            _canon[repo] = repo
    return _canon[repo]

def tree(repo):
    repo = canonical(repo)
    url = f"https://huggingface.co/api/models/{repo}/tree/main?recursive=true"
    try:
        data = json.load(urllib.request.urlopen(url, timeout=60))
    except Exception as e:
        print("  !", repo, e, file=sys.stderr)
        return []
    return [f for f in data if f.get("type") == "file"]

_cache = {}
def files(repo):
    if repo not in _cache:
        _cache[repo] = tree(repo)
    return _cache[repo]

def one(repo, pattern):
    """A single file (component) matching the regex."""
    rx = re.compile(pattern, re.I)
    for f in files(repo):
        if rx.search(f["path"]):
            return {"repo": canonical(repo), "path": f["path"], "size": f.get("lfs", {}).get("size", f.get("size", 0))}
    print(f"  ! missing {repo} :: {pattern}", file=sys.stderr)
    return None

QUANT = re.compile(r"(?:^|[-_.])((?:UD-)?(?:I?Q\d(?:_[A-Z0-9]+)*|BF16|F16|F32))\.gguf$", re.I)

def gguf_variants(repo, folder=None, include=None):
    """label -> file for every quant in the repo (optionally only inside a folder)."""
    out = {}
    for f in files(repo):
        p = f["path"]
        if not p.lower().endswith(".gguf"): continue
        if folder and not p.startswith(folder): continue
        if include and not re.search(include, p, re.I): continue
        m = QUANT.search(os.path.basename(p))
        if not m: continue
        label = m.group(1).upper()
        if label in ("F32",): continue
        size = f.get("lfs", {}).get("size", f.get("size", 0))
        out[label] = {"repo": canonical(repo), "path": p, "size": size}
    return out

def variants_list(d, fmt="gguf"):
    return [dict(label=k, format=fmt, **v) for k, v in d.items()]

def lora(repo, pattern, name, high=False, weight=1.0):
    f = one(repo, pattern)
    return dict(name=name, high=high, weight=weight, **f) if f else None

def speed(label, steps, cfg, loras, high_steps=0, extra=""):
    loras = [l for l in loras if l]
    return dict(label=label, steps=steps, highSteps=high_steps, cfg=cfg, extra=extra, loras=loras) if loras else None

# ── shared parts ────────────────────────────────────────────────────────────
AE_FLUX1 = one("Comfy-Org/z_image_turbo", r"vae/ae\.safetensors$")
QWEN3_4B_INSTR = one("unsloth/Qwen3-4B-Instruct-2507-GGUF", r"Q4_K_M\.gguf$")
QWEN3_4B = one("unsloth/Qwen3-4B-GGUF", r"Qwen3-4B-Q4_K_M\.gguf$")
QWEN3_8B = one("unsloth/Qwen3-8B-GGUF", r"Qwen3-8B-Q4_K_M\.gguf$")
FLUX2_VAE = one("Comfy-Org/flux2-dev", r"vae/.*\.safetensors$")
QWEN3VL_8B = one("Qwen/Qwen3-VL-8B-Instruct-GGUF", r"Q4_K_M\.gguf$")
QWEN3VL_8B_VISION = one("Qwen/Qwen3-VL-8B-Instruct-GGUF", r"mmproj-Qwen3VL-8B-Instruct-Q8_0\.gguf$")
QWEN25VL_7B_VISION = one("unsloth/Qwen2.5-VL-7B-Instruct-GGUF", r"^mmproj-F16\.gguf$")
GEMMA3_12B = one("unsloth/gemma-3-12b-it-qat-GGUF", r"gemma-3-12b-it-qat-UD-Q4_K_XL\.gguf$")
QWEN3VL_4B = one("Qwen/Qwen3-VL-4B-Instruct-GGUF", r"Q4_K_M\.gguf$")
QI21_VAE = one("Comfy-Org/Qwen-Image-2.1", r"vae/qwen_image_2\.1_vae_bf16\.safetensors$")
QI_VAE = one("Comfy-Org/Qwen-Image_ComfyUI", r"vae/qwen_image_vae\.safetensors$")
QWEN25VL_7B = one("unsloth/Qwen2.5-VL-7B-Instruct-GGUF", r"Qwen2\.5-VL-7B-Instruct-Q4_K_M\.gguf$")
QWEN3_06B_BASE = one("mradermacher/Qwen3-0.6B-Base-GGUF", r"Q8_0\.gguf$")
WAN21_VAE = one("Comfy-Org/Wan_2.1_ComfyUI_repackaged", r"vae/wan_2\.1_vae\.safetensors$")
WAN22_VAE = one("Comfy-Org/Wan_2.2_ComfyUI_Repackaged", r"vae/wan2\.2_vae\.safetensors$")
UMT5 = one("city96/umt5-xxl-encoder-gguf", r"umt5-xxl-encoder-Q8_0\.gguf$")
T5XXL = one("city96/t5-v1_1-xxl-encoder-gguf", r"t5-v1_1-xxl-encoder-Q8_0\.gguf$")
CLIP_L = one("comfyanonymous/flux_text_encoders", r"^clip_l\.safetensors$")
SDXL_VAE = one("madebyollin/sdxl-vae-fp16-fix", r"^sdxl\.vae\.safetensors$")

CONV = "milo01/ComfyUI-Native-INT8_ConvRot"
def convrot(repo, pattern, label="INT8 ConvRot"):
    f = one(repo, pattern)
    return [dict(label=label, format="convrot", **f)] if f else []

WAN_NEG = "色调艳丽，过曝，静态，细节模糊不清，字幕，风格，作品，画作，画面，静止，整体发灰，最差质量，低质量，JPEG压缩残留，丑陋的，残缺的，多余的手指，画得不好的手部，画得不好的脸部，畸形的，毁容的，形态畸形的肢体，手指融合，静止不动的画面，杂乱的背景，三条腿，背景人很多，倒着走"

R = []
def recipe(**k):
    parts = [p for p in k.pop("parts") if p]
    v = k.pop("variants")
    if not v:
        print("  ! no variants for", k["id"], file=sys.stderr); return
    k.setdefault("negative", "")
    k.setdefault("sampler", "euler")
    k.setdefault("flowShift", 0)
    k.setdefault("frames", 0)
    k.setdefault("highSteps", 0)
    k.setdefault("input", "none")       # none | optional | required
    k.setdefault("tasks", ["create"])   # create restyle edit combine inpaint animate video-edit upscale
    k.setdefault("refs", 0)             # how many reference pictures edit/combine accepts
    k.setdefault("extra", "")           # extra sd-cli arguments
    k.setdefault("fps", 0)
    k.setdefault("uncensored", False)   # no content filter: listed under "🔓 Uncensored"
    k.setdefault("prefix", "")          # added before the prompt (e.g. Pony quality tags)
    if "loras" in k: k["loras"] = [l for l in k["loras"] if l]   # always-on LoRAs
    if k.get("speed") is None: k.pop("speed", None)
    k["variants"] = sorted(v, key=lambda x: x["size"])
    k["parts"] = parts
    R.append(k)

# ── images ──────────────────────────────────────────────────────────────────
recipe(id="z-image-turbo", name="Z-Image Turbo", kind="image", family="Z-Image",
       about="Fast photorealistic images in 8 steps, great text rendering. Best all-rounder for most GPUs.",
       steps=8, cfg=1.0, width=1024, height=1024, input="optional",
       tasks=["create","restyle","inpaint"],
       variants=variants_list(gguf_variants("leejet/Z-Image-Turbo-GGUF")) + convrot(CONV, r"z_image_turbo_int8_convrot"),
       parts=[dict(role="vae", **AE_FLUX1), dict(role="llm", **QWEN3_4B_INSTR)])
recipe(id="z-image", name="Z-Image (base)", kind="image", family="Z-Image",
       about="Higher-quality, more controllable Z-Image (slower: 28 steps, uses negative prompts).",
       input="optional", steps=28, cfg=5.0, width=1024, height=1024, negative="blurry, low quality, watermark",
       tasks=["create","restyle","inpaint"],
       variants=variants_list(gguf_variants("unsloth/Z-Image-GGUF")),
       parts=[dict(role="vae", **AE_FLUX1), dict(role="llm", **QWEN3_4B_INSTR)])
recipe(id="flux2-klein-4b", name="FLUX.2 klein 4B", kind="image", family="FLUX.2",
       about="Small, very fast FLUX.2 (4 steps). Text-to-image and image editing. Good on 6–8 GB.",
       steps=4, cfg=1.0, width=1024, height=1024, input="optional",
       tasks=["create","edit","combine","inpaint","pose","layout","outline","restore"], refs=3,
       variants=variants_list(gguf_variants("leejet/FLUX.2-klein-4B-GGUF")),
       parts=[dict(role="vae", **FLUX2_VAE) if FLUX2_VAE else None, dict(role="llm", **QWEN3_4B)])
recipe(id="flux2-klein-9b", name="FLUX.2 klein 9B", kind="image", family="FLUX.2",
       about="Sharper FLUX.2 (4 steps) with editing. INT8 ConvRot gives near-full quality at half the size.",
       steps=4, cfg=1.0, width=1024, height=1024, input="optional",
       tasks=["create","edit","combine","inpaint","pose","layout","outline","restore"], refs=3,
       variants=variants_list(gguf_variants("leejet/FLUX.2-klein-9B-GGUF")) + convrot(CONV, r"flux-2-klein-9b_int8_convrot"),
       parts=[dict(role="vae", **FLUX2_VAE) if FLUX2_VAE else None, dict(role="llm", **QWEN3_8B)])
recipe(id="qwen-image-2.1", name="Qwen-Image 2.1", kind="image", family="Qwen-Image",
       about="Top prompt following and perfect text in images (English, Chinese…). Supports editing.",
       steps=20, cfg=6.0, width=1024, height=1024, input="optional",
       tasks=["create","edit","combine","inpaint","pose","layout","outline","restore"], refs=3,
       speed=speed("⚡ Lightning · 4 steps", 4, 1.0, [lora("lightx2v/Qwen-Image-2.1-Lightning", r"^pytorch_lora_weights\.safetensors$", "speed-qwen-image-2.1-lightning-4step")]),
       variants=variants_list(gguf_variants("leejet/Qwen-Image-2.1-GGUF", include=r"^(?!.*turbo)")) +
                convrot("Comfy-Org/Qwen-Image-2.1", r"diffusion_models/qwen_image_2\.1_int8_convrot"),
       parts=[dict(role="vae", **QI21_VAE), dict(role="llm", **QWEN3VL_8B), dict(role="llm_vision", edit=True, **QWEN3VL_8B_VISION)])
recipe(id="qwen-image-2.1-turbo", name="Qwen-Image 2.1 Turbo", kind="image", family="Qwen-Image",
       about="Fast Qwen-Image 2.1 (8 steps) with the same excellent text rendering.",
       input="optional", steps=8, cfg=1.0, width=1024, height=1024,
       tasks=["create","restyle","inpaint"],
       variants=variants_list(gguf_variants("leejet/Qwen-Image-2.1-GGUF", include=r"turbo")) +
                convrot("Comfy-Org/Qwen-Image-2.1", r"diffusion_models/qwen_image_2\.1_turbo_int8_convrot"),
       parts=[dict(role="vae", **QI21_VAE), dict(role="llm", **QWEN3VL_8B)])
recipe(id="qwen-image-2512", name="Qwen-Image 2512", kind="image", family="Qwen-Image",
       about="Large 20B Qwen-Image release: rich detail and layouts. Needs a big GPU or offloading.",
       input="optional", steps=20, cfg=2.5, flowShift=3, width=1024, height=1024,
       tasks=["create","restyle","inpaint"],
       speed=speed("⚡ Lightning · 8 steps", 8, 1.0, [lora("lightx2v/Qwen-Image-2512-Lightning", r"Qwen-Image-2512-Lightning-8steps-V1\.0-bf16\.safetensors$", "speed-qwen-image-2512-lightning-8step")]),
       variants=variants_list(gguf_variants("unsloth/Qwen-Image-2512-GGUF")) + convrot(CONV, r"qwen-image-2512-int8-ConvRot"),
       parts=[dict(role="vae", **QI_VAE), dict(role="llm", **QWEN25VL_7B)])
recipe(id="krea2-turbo", name="Krea 2 Turbo", kind="image", family="Krea",
       about="Aesthetic, photographic Krea 2 in few steps.",
       input="optional", steps=8, cfg=1.0, width=1024, height=1024,
       tasks=["create","restyle","inpaint"],
       variants=variants_list(gguf_variants("realrebelai/KREA-2_GGUFs", folder="TURBO")) + convrot(CONV, r"Krea2-Turbo-int8-ConvRot"),
       parts=[dict(role="vae", **WAN21_VAE), dict(role="llm", **QWEN3VL_4B)])
recipe(id="anima", name="Anima (anime)", kind="image", family="Anima",
       about="Anime and illustration style model. Small and fast.",
       input="optional", steps=30, cfg=6.0, width=1024, height=1024, negative="worst quality, low quality, blurry",
       tasks=["create","restyle","inpaint"],
       variants=variants_list(gguf_variants("Bedovyy/Anima-GGUF")) + convrot(CONV, r"anima-preview3-base-int8-ConvRot"),
       parts=[dict(role="vae", **QI_VAE), dict(role="llm", **QWEN3_06B_BASE)])
recipe(id="flux1-schnell", name="FLUX.1 schnell", kind="image", family="FLUX.1",
       about="Classic fast FLUX.1 (4 steps), Apache-2.0 licensed.",
       input="optional", steps=4, cfg=1.0, width=1024, height=1024,
       tasks=["create","restyle","inpaint"],
       variants=variants_list(gguf_variants("city96/FLUX.1-schnell-gguf")),
       parts=[dict(role="vae", **AE_FLUX1), dict(role="clip_l", **CLIP_L), dict(role="t5xxl", **T5XXL)])
sdxl = one("stabilityai/stable-diffusion-xl-base-1.0", r"^sd_xl_base_1\.0\.safetensors$")
recipe(id="sdxl", name="Stable Diffusion XL", kind="image", family="SDXL",
       about="The classic SDXL base model. Runs on 6 GB GPUs; huge community of styles.",
       steps=30, cfg=7.0, width=1024, height=1024, negative="blurry, low quality, deformed", input="optional",
       tasks=["create","restyle","inpaint","layout","outline"],
       speed=speed("⚡ Lightning · 8 steps", 8, 1.0, [lora("ByteDance/SDXL-Lightning", r"^sdxl_lightning_8step_lora\.safetensors$", "speed-sdxl-lightning-8step")], extra="--scheduler sgm_uniform"),
       variants=[dict(label="FP16", format="checkpoint", **sdxl)] if sdxl else [],
       parts=[dict(role="vae", **SDXL_VAE),
              dict(role="control-net", control=True, **one("xinsir/controlnet-union-sdxl-1.0", r"^diffusion_pytorch_model_promax\.safetensors$"))])

sd15 = one("stable-diffusion-v1-5/stable-diffusion-v1-5", r"^v1-5-pruned-emaonly\.safetensors$")
recipe(id="sd15", name="Stable Diffusion 1.5", kind="image", family="SD1.5",
       about="The classic small model: runs on almost any PC (4 GB GPU). Thousands of styles and LoRAs on Civitai.",
       steps=25, cfg=7.0, width=512, height=512, negative="blurry, low quality, deformed", input="optional",
       tasks=["create","restyle","inpaint"],
       speed=speed("⚡ LCM · 6 steps", 6, 1.5, [lora("latent-consistency/lcm-lora-sdv1-5", r"^pytorch_lora_weights\.safetensors$", "speed-sd15-lcm")], extra="--sampling-method lcm"),
       variants=[dict(label="FP32", format="checkpoint", **sd15)] if sd15 else [],
       parts=[])

# ── uncensored pictures (no content filter; shown under "🔓 Uncensored" in the Studio) ──
CHROMA_NEG = "low quality, ugly, unfinished, out of focus, deformed, disfigured, blurry, smudged, restricted palette, flat colors"
recipe(id="chroma1-hd", name="Chroma1-HD", kind="image", family="Chroma", uncensored=True,
       about="Uncensored FLUX-based model (Apache-2.0): photos, art and anime with no content filter. 26 steps.",
       steps=26, cfg=4.0, width=1024, height=1024, negative=CHROMA_NEG, input="optional",
       extra="--model-args chroma_use_dit_mask=false",
       tasks=["create","restyle","inpaint"],
       variants=variants_list(gguf_variants("silveroxides/Chroma1-HD-GGUF")),
       parts=[dict(role="vae", **AE_FLUX1), dict(role="t5xxl", **T5XXL)])
recipe(id="chroma1-flash", name="Chroma1-Flash", kind="image", family="Chroma", uncensored=True,
       about="Fast uncensored Chroma (about 12 steps, no negative prompt needed).",
       steps=12, cfg=1.0, width=1024, height=1024, input="optional",
       extra="--model-args chroma_use_dit_mask=false",
       tasks=["create","restyle","inpaint"],
       variants=variants_list(gguf_variants("silveroxides/Chroma1-Flash-GGUF")),
       parts=[dict(role="vae", **AE_FLUX1), dict(role="t5xxl", **T5XXL)])
SDXL_SPEED = lambda: speed("⚡ Lightning · 8 steps", 8, 1.0, [lora("ByteDance/SDXL-Lightning", r"^sdxl_lightning_8step_lora\.safetensors$", "speed-sdxl-lightning-8step")], extra="--scheduler sgm_uniform")
SDXL_CONTROL = lambda: dict(role="control-net", control=True, **one("xinsir/controlnet-union-sdxl-1.0", r"^diffusion_pytorch_model_promax\.safetensors$"))
def sdxl_checkpoint(rid, name, about, repo, pattern, **k):
    f = one(repo, pattern)
    recipe(id=rid, name=name, kind="image", family="SDXL", uncensored=True, about=about,
           width=1024, height=1024, input="optional", tasks=["create","restyle","inpaint","layout","outline"],
           speed=SDXL_SPEED(), variants=[dict(label="FP16", format="checkpoint", **f)] if f else [],
           parts=[dict(role="vae", **SDXL_VAE), SDXL_CONTROL()], **k)
sdxl_checkpoint("realvisxl-v5", "RealVisXL V5 (photo)", "Photorealistic SDXL model without a content filter. Runs on 8 GB GPUs.",
                "SG161222/RealVisXL_V5.0", r"^RealVisXL_V5\.0_fp16\.safetensors$",
                steps=30, cfg=5.0, negative="cartoon, painting, illustration, worst quality, low quality, blurry, deformed")
sdxl_checkpoint("pony-v6-xl", "Pony Diffusion V6 XL", "Popular uncensored SDXL model for characters (anime, cartoon, furry, semi-real). Quality tags are added for you.",
                "LyliaEngine/Pony_Diffusion_V6_XL", r"^ponyDiffusionV6XL.*\.safetensors$",
                steps=25, cfg=7.0, sampler="euler_a", extra="--clip-skip 2", prefix="score_9, score_8_up, score_7_up, ",
                negative="score_4, score_5, score_6, worst quality, low quality, blurry")
sdxl_checkpoint("noobai-xl-1.1", "NoobAI-XL 1.1 (anime)", "Uncensored anime / illustration model (Illustrious family). Understands Danbooru tags.",
                "Laxhar/noobai-XL-1.1", r"^NoobAI-XL-v1\.1\.safetensors$",
                steps=28, cfg=5.0, sampler="euler_a", prefix="masterpiece, best quality, newest, absurdres, highres, ",
                negative="worst quality, old, early, low quality, lowres, signature, username, logo, bad hands, mutated hands")
rv6 = one("SG161222/Realistic_Vision_V6.0_B1_noVAE", r"^Realistic_Vision_V6\.0_NV_B1_fp16\.safetensors$")
recipe(id="realistic-vision-v6", name="Realistic Vision V6 (SD 1.5)", kind="image", family="SD1.5", uncensored=True,
       about="Small photorealistic model without a content filter. Runs on almost any PC (4 GB GPU).",
       steps=25, cfg=5.0, width=512, height=768, input="optional",
       negative="deformed, cartoon, anime, worst quality, low quality, blurry",
       tasks=["create","restyle","inpaint"],
       speed=speed("⚡ LCM · 6 steps", 6, 1.5, [lora("latent-consistency/lcm-lora-sdv1-5", r"^pytorch_lora_weights\.safetensors$", "speed-sd15-lcm")], extra="--sampling-method lcm"),
       variants=[dict(label="FP16", format="checkpoint", **rv6)] if rv6 else [],
       parts=[])

# ── dedicated picture editors ───────────────────────────────────────────────
recipe(id="qwen-image-edit-2511", name="Qwen-Image Edit 2511", kind="image", family="Qwen-Image-Edit",
       about="The strongest 'describe the change' editor: change objects, clothes, text, style or background while keeping the rest. Combines up to 3 pictures.",
       steps=20, cfg=2.5, flowShift=3, width=1024, height=1024, input="required",
       tasks=["edit","combine","inpaint","pose","layout","outline","restore"], refs=3,
       speed=speed("⚡ Lightning · 8 steps", 8, 1.0, [lora("lightx2v/Qwen-Image-Edit-2511-Lightning", r"Qwen-Image-Edit-2511-Lightning-8steps-V1\.0-bf16\.safetensors$", "speed-qwen-image-edit-2511-lightning-8step")]),
       variants=variants_list(gguf_variants("unsloth/Qwen-Image-Edit-2511-GGUF")) +
                convrot("cardamonnl/Qwen-Image-Edit-2511-int8-convrot", r"^qwen_image_edit_2511_int8_convrot\.safetensors$"),
       parts=[dict(role="vae", **QI_VAE), dict(role="llm", **QWEN25VL_7B), dict(role="llm_vision", **QWEN25VL_7B_VISION)])
recipe(id="flux1-kontext", name="FLUX.1 Kontext (edit)", kind="image", family="FLUX.1-Kontext",
       about="Edits a picture from a short instruction ('make it winter', 'remove the logo'). Runs on 6 GB GPUs.",
       steps=20, cfg=1.0, width=1024, height=1024, input="required", extra="--guidance 2.5",
       tasks=["edit","layout","outline","restore"], refs=1,
       speed=speed("⚡ Turbo · 8 steps", 8, 1.0, [lora("alimama-creative/FLUX.1-Turbo-Alpha", r"^diffusion_pytorch_model\.safetensors$", "speed-flux1-turbo-alpha-8step")], extra="--guidance 3.5"),
       variants=variants_list(gguf_variants("QuantStack/FLUX.1-Kontext-dev-GGUF")),
       parts=[dict(role="vae", **AE_FLUX1), dict(role="clip_l", **CLIP_L), dict(role="t5xxl", **T5XXL)])

# ── tools ───────────────────────────────────────────────────────────────────
bg = one("studioludens/birefnet-lite-512", r"^onnx/model\.onnx$")
if bg:
    recipe(id="remove-bg", name="Remove background (BiRefNet)", kind="tool", family="BiRefNet",
           about="Cuts out the main subject and makes the background transparent (PNG). Small and fast, runs on the CPU.",
           steps=1, cfg=0, width=0, height=0, input="required", tasks=["remove-bg"],
           variants=[dict(label="lite 512", format="onnx", **bg)], parts=[])

# ── upscalers (ESRGAN) ──────────────────────────────────────────────────────
def upscaler(rid, name, about, repo, pattern):
    f = one(repo, pattern)
    if f:
        recipe(id=rid, name=name, kind="upscale", family="ESRGAN", about=about, steps=1, cfg=0, width=0, height=0,
               input="required", tasks=["upscale"], variants=[dict(label="4×", format="esrgan", **f)], parts=[])
upscaler("upscale-realesrgan", "Real-ESRGAN 4× (photos)", "Makes photos 4× bigger and sharper. Small and fast.",
         "lllyasviel/Annotators", r"^RealESRGAN_x4plus\.pth$")
upscaler("upscale-ultrasharp", "4x-UltraSharp", "Crisp 4× enlargement for photos and renders.",
         "lokCX/4x-Ultrasharp", r"^4x-UltraSharp\.pth$")
upscaler("upscale-anime", "Real-ESRGAN 4× (anime)", "4× enlargement tuned for anime and drawings.",
         "amd/realesrgan-x4plus-anime-6b", r"RealESRGAN_x4plus_anime_6B\.pth$")

# ── video ───────────────────────────────────────────────────────────────────
recipe(id="wan2.2-ti2v-5b", name="Wan 2.2 TI2V 5B (video)", kind="video", family="Wan",
       about="Text or image to video on mid-range GPUs. ~2 s clips at 832×480.",
       steps=20, cfg=6.0, flowShift=3, width=832, height=480, frames=33, negative=WAN_NEG, input="optional",
       tasks=["create","animate"], fps=16,
       variants=variants_list(gguf_variants("QuantStack/Wan2.2-TI2V-5B-GGUF")),
       parts=[dict(role="vae", **WAN22_VAE), dict(role="t5xxl", **UMT5)])
w13 = one("Comfy-Org/Wan_2.1_ComfyUI_repackaged", r"diffusion_models/wan2\.1_t2v_1\.3B_fp16\.safetensors$")
recipe(id="wan2.1-t2v-1.3b", name="Wan 2.1 T2V 1.3B (video)", kind="video", family="Wan",
       about="Smallest video model: short clips even on 8 GB GPUs.",
       steps=20, cfg=6.0, flowShift=3, width=832, height=480, frames=33, negative=WAN_NEG,
       tasks=["create"], fps=16,
       speed=speed("⚡ CausVid · 8 steps", 8, 1.0, [lora("Kijai/WanVideo_comfy", r"^Wan21_CausVid_bidirect2_T2V_1_3B_lora_rank32\.safetensors$", "speed-wan21-1.3b-causvid")]),
       variants=[dict(label="FP16", format="safetensors", **w13)] if w13 else [],
       parts=[dict(role="vae", **WAN21_VAE), dict(role="t5xxl", **UMT5)])

WAN22_T2V_SPEED = lambda: speed("⚡ Lightning · 4+4 steps", 4, 1.0, [
    lora("lightx2v/Wan2.2-Distill-Loras", r"wan2\.2_t2v_A14b_low_noise_lora_rank64_lightx2v_4step", "speed-wan22-t2v-low-4step"),
    lora("lightx2v/Wan2.2-Distill-Loras", r"wan2\.2_t2v_A14b_high_noise_lora_rank64_lightx2v_4step", "speed-wan22-t2v-high-4step", high=True)], high_steps=4)
WAN22_I2V_SPEED = lambda: speed("⚡ Lightning · 4+4 steps", 4, 1.0, [
    lora("lightx2v/Wan2.2-Distill-Loras", r"wan2\.2_i2v_A14b_low_noise_lora_rank64_lightx2v_4step", "speed-wan22-i2v-low-4step"),
    lora("lightx2v/Wan2.2-Distill-Loras", r"wan2\.2_i2v_A14b_high_noise_lora_rank64_lightx2v_4step", "speed-wan22-i2v-high-4step", high=True)], high_steps=4)

def wan_a14b(rid, name, about, repo, conv_hi, conv_lo, input_mode, tasks, spd=None, **k):
    lo = gguf_variants(repo, folder="LowNoise")
    hi = gguf_variants(repo, folder="HighNoise")
    v = []
    for label in lo:
        if label in hi:
            v.append(dict(label=label, format="gguf", **lo[label], high=hi[label], size_total=lo[label]["size"] + hi[label]["size"]))
    ch, cl = one(CONV, conv_hi), one(CONV, conv_lo)
    if ch and cl:
        v.append(dict(label="INT8 ConvRot", format="convrot", **cl, high=ch, size_total=ch["size"] + cl["size"]))
    recipe(id=rid, name=name, kind="video", family="Wan", about=about,
           steps=10, highSteps=8, cfg=3.5, flowShift=3, width=832, height=480, frames=33, negative=WAN_NEG, input=input_mode, tasks=tasks, fps=16, speed=spd,
           variants=v, parts=[dict(role="vae", **WAN21_VAE), dict(role="t5xxl", **UMT5)], **k)

wan_a14b("wan2.2-t2v-a14b", "Wan 2.2 T2V A14B (video)", "Best-quality text-to-video (two 14B experts). Needs lots of RAM; GPU 12 GB+.",
         "QuantStack/Wan2.2-T2V-A14B-GGUF", r"wan2\.2_t2v_high_int8_ConvRot", r"wan2\.2_t2v_low_int8_ConvRot", "none", ["create"], WAN22_T2V_SPEED())
wan_a14b("wan2.2-i2v-a14b", "Wan 2.2 I2V A14B (video)", "Animate a photo into a video clip (two 14B experts). GPU 12 GB+.",
         "QuantStack/Wan2.2-I2V-A14B-GGUF", r"^diffusion_models/wan2\.2_i2v_high_int8_convrot", r"^diffusion_models/wan2\.2_i2v_low_int8_convrot", "required", ["animate","first-last"], WAN22_I2V_SPEED())

# ── uncensored video ──
NSFW_I2V = [lora("lopi999/Wan2.2-I2V_General-NSFW-LoRA", r"NSFW-22-L-e8\.safetensors$", "builtin-wan22-i2v-nsfw-low"),
            lora("lopi999/Wan2.2-I2V_General-NSFW-LoRA", r"NSFW-22-H-e8\.safetensors$", "builtin-wan22-i2v-nsfw-high", high=True)]
wan_a14b("wan2.2-i2v-a14b-nsfw", "Wan 2.2 I2V A14B Uncensored (video)",
         "Animate a photo with no content filter: Wan 2.2 with an NSFW LoRA (uses the same base files as Wan 2.2 I2V). GPU 12 GB+.",
         "QuantStack/Wan2.2-I2V-A14B-GGUF", r"^diffusion_models/wan2\.2_i2v_high_int8_convrot", r"^diffusion_models/wan2\.2_i2v_low_int8_convrot",
         "required", ["animate","first-last"], WAN22_I2V_SPEED(), uncensored=True, loras=[l for l in NSFW_I2V if l])
nsfw14 = one("NSFW-API/NSFW_Wan_14b", r"^nsfw_wan_14b_e15_q4_k\.gguf$")
recipe(id="nsfw-wan-14b-t2v", name="NSFW Wan 14B (text to video)", kind="video", family="Wan", uncensored=True,
       about="Uncensored text-to-video: Wan 2.1 14B fine-tuned without a content filter. GPU 12 GB+.",
       steps=20, cfg=5.0, flowShift=3, width=832, height=480, frames=33, negative=WAN_NEG, fps=16,
       tasks=["create"],
       speed=speed("⚡ Lightx2v · 4 steps", 4, 1.0, [lora("lightx2v/Wan2.1-Distill-Loras", r"wan2\.1_t2v_14b_lora_rank64_lightx2v_4step\.safetensors$", "speed-wan21-14b-lightx2v-4step")]),
       variants=[dict(label="Q4_K", format="gguf", **nsfw14)] if nsfw14 else [],
       parts=[dict(role="vae", **WAN21_VAE), dict(role="t5xxl", **UMT5)])
NV = "PineAmbassador/Wan2.2-VACE-Fun-A14B-NSFW_Merge"
nlo, nhi = one(NV, r"low_noise/.*Q6_K.*\.gguf$"), one(NV, r"high_noise/.*Q6_K.*\.gguf$")
recipe(id="wan2.2-vace-fun-a14b-nsfw", name="Wan 2.2 VACE Fun A14B Uncensored (video edit)", kind="video", family="Wan-VACE", uncensored=True,
       about="Video editing and restyling with no content filter (NSFW merge of Wan 2.2 VACE Fun). Needs lots of RAM; GPU 16 GB+.",
       steps=10, highSteps=8, cfg=3.5, flowShift=3, width=832, height=480, frames=33, negative=WAN_NEG, input="optional", fps=16,
       tasks=["video-edit","animate"], speed=WAN22_T2V_SPEED(),
       variants=[dict(label="Q6_K", format="gguf", **nlo, high=nhi, size_total=nlo["size"] + nhi["size"])] if nlo and nhi else [],
       parts=[dict(role="vae", **WAN21_VAE), dict(role="t5xxl", **UMT5)])

# ── video editing (Wan VACE) and LTX-2.3 ────────────────────────────────────
vace13 = one("Comfy-Org/Wan-2.1", r"diffusion_models/wan2\.1_vace_1\.3B_fp16\.safetensors$")
recipe(id="wan2.1-vace-1.3b", name="Wan 2.1 VACE 1.3B (video edit)", kind="video", family="Wan-VACE",
       about="Restyle or change a video you give it, keeping its motion. Also makes video from a character picture. Fits 8 GB GPUs.",
       steps=20, cfg=6.0, flowShift=3, width=832, height=480, frames=33, negative=WAN_NEG, input="optional", fps=16,
       tasks=["video-edit","animate","create"],
       speed=speed("⚡ CausVid · 8 steps", 8, 1.0, [lora("Kijai/WanVideo_comfy", r"^Wan21_CausVid_bidirect2_T2V_1_3B_lora_rank32\.safetensors$", "speed-wan21-1.3b-causvid")]),
       variants=[dict(label="FP16", format="safetensors", **vace13)] if vace13 else [],
       parts=[dict(role="vae", **WAN21_VAE), dict(role="t5xxl", **UMT5)])
recipe(id="wan2.1-vace-14b", name="Wan 2.1 VACE 14B (video edit)", kind="video", family="Wan-VACE",
       about="High-quality video editing and restyling, keeping the motion of your video. GPU 12 GB+.",
       steps=20, cfg=6.0, flowShift=3, width=832, height=480, frames=33, negative=WAN_NEG, input="optional", fps=16,
       tasks=["video-edit","animate","create"],
       speed=speed("⚡ Lightx2v · 4 steps", 4, 1.0, [lora("lightx2v/Wan2.1-Distill-Loras", r"wan2\.1_t2v_14b_lora_rank64_lightx2v_4step\.safetensors$", "speed-wan21-14b-lightx2v-4step")]),
       variants=variants_list(gguf_variants("QuantStack/Wan2.1_14B_VACE-GGUF")),
       parts=[dict(role="vae", **WAN21_VAE), dict(role="t5xxl", **UMT5)])
vlo = gguf_variants("QuantStack/Wan2.2-VACE-Fun-A14B-GGUF", folder="LowNoise")
vhi = gguf_variants("QuantStack/Wan2.2-VACE-Fun-A14B-GGUF", folder="HighNoise")
recipe(id="wan2.2-vace-fun-a14b", name="Wan 2.2 VACE Fun A14B (video edit)", kind="video", family="Wan-VACE",
       about="Best-quality video editing with the two Wan 2.2 experts. Needs lots of RAM; GPU 16 GB+.",
       steps=10, highSteps=8, cfg=3.5, flowShift=3, width=832, height=480, frames=33, negative=WAN_NEG, input="optional", fps=16,
       tasks=["video-edit","animate"], speed=WAN22_T2V_SPEED(),
       variants=[dict(label=k, format="gguf", **vlo[k], high=vhi[k], size_total=vlo[k]["size"] + vhi[k]["size"]) for k in vlo if k in vhi],
       parts=[dict(role="vae", **WAN21_VAE), dict(role="t5xxl", **UMT5)])
LTX = "unsloth/LTX-2.3-GGUF"
recipe(id="ltx-2.3-distilled", name="LTX-2.3 distilled (video + sound)", kind="video", family="LTX",
       about="Fast 22B video model that also creates matching sound. Text or picture to video, 24 fps. GPU 12 GB+ and 32 GB RAM.",
       steps=8, cfg=1.0, width=1280, height=704, frames=97, input="optional", fps=24,
       negative="worst quality, low quality, blurry, distorted, artifacts",
       tasks=["create","animate"],
       variants=variants_list(gguf_variants(LTX, folder="distilled-1.1/", include=r"^(?!.*UD-)")),
       parts=[dict(role="vae", **one(LTX, r"vae/ltx-2\.3-22b-distilled_video_vae")),
              dict(role="audio-vae", **one(LTX, r"vae/ltx-2\.3-22b-distilled_audio_vae")),
              dict(role="llm", **GEMMA3_12B),
              dict(role="embeddings-connectors", **one(LTX, r"text_encoders/ltx-2\.3-22b-distilled_embeddings_connectors"))])

# ── more base models (also templates for community models) ─────────────────
recipe(id="flux1-dev", name="FLUX.1 dev", kind="image", family="FLUX.1",
       about="The classic high-quality FLUX.1 (20 steps). Base of hundreds of community models.",
       input="optional", steps=20, cfg=1.0, width=1024, height=1024,
       tasks=["create","restyle","inpaint"],
       variants=variants_list(gguf_variants("city96/FLUX.1-dev-gguf")),
       parts=[dict(role="vae", **AE_FLUX1), dict(role="clip_l", **CLIP_L), dict(role="t5xxl", **T5XXL)])
recipe(id="wan2.1-t2v-14b", name="Wan 2.1 T2V 14B (video)", kind="video", family="Wan",
       about="Text-to-video with the 14B Wan 2.1 model. GPU 12 GB+ (or more RAM).",
       steps=20, cfg=5.0, flowShift=3, width=832, height=480, frames=33, negative=WAN_NEG, fps=16,
       tasks=["create"],
       speed=speed("⚡ Lightx2v · 4 steps", 4, 1.0, [lora("lightx2v/Wan2.1-Distill-Loras", r"wan2\.1_t2v_14b_lora_rank64_lightx2v_4step\.safetensors$", "speed-wan21-14b-lightx2v-4step")]),
       variants=variants_list(gguf_variants("city96/Wan2.1-T2V-14B-gguf")),
       parts=[dict(role="vae", **WAN21_VAE), dict(role="t5xxl", **UMT5)])
CLIP_VISION_H = one("Comfy-Org/Wan_2.1_ComfyUI_repackaged", r"clip_vision/clip_vision_h\.safetensors$")
for res, h, w in (("480p", 480, 832), ("720p", 720, 1280)):
    recipe(id=f"wan2.1-i2v-14b-{res}", name=f"Wan 2.1 I2V 14B {res.upper()} (video)", kind="video", family="Wan",
           about=f"Animate a photo with the 14B Wan 2.1 image-to-video model ({res.upper()}). GPU 12 GB+.",
           steps=20, cfg=5.0, flowShift=3, width=w, height=h, frames=33, negative=WAN_NEG, fps=16, input="required",
           tasks=["animate"],
           speed=speed("⚡ Lightx2v · 4 steps", 4, 1.0, [lora("lightx2v/Wan2.1-Distill-Loras", r"wan2\.1_i2v_lora_rank64_lightx2v_4step\.safetensors$", "speed-wan21-i2v-14b-lightx2v-4step")]),
           variants=variants_list(gguf_variants(f"city96/Wan2.1-I2V-14B-{res.upper()}-gguf")),
           parts=[dict(role="vae", **WAN21_VAE), dict(role="t5xxl", **UMT5), dict(role="clip_vision", **CLIP_VISION_H)] if CLIP_VISION_H else [])

# ── talking video (speech to video) and first + last frame ─────────────────
WAV2VEC = one("Comfy-Org/Wan_2.2_ComfyUI_Repackaged", r"audio_encoders/wav2vec2_large_english_fp16\.safetensors$")
if WAV2VEC and CLIP_VISION_H:
    recipe(id="wan2.2-s2v-14b", name="Wan 2.2 S2V 14B (talking video)", kind="video", family="Wan-S2V",
           about="Makes the person in a picture talk or sing to a voice recording, with matching lip and face movement. "
                 "Longer speech is made in 5-second pieces and joined. GPU 12 GB+.",
           steps=20, cfg=6.0, flowShift=3, width=832, height=480, frames=81, negative=WAN_NEG, input="required", fps=16,
           tasks=["talk"],
           speed=speed("⚡ Lightning · 4 steps", 4, 1.0, [lora("lightx2v/Wan2.2-Distill-Loras", r"wan2\.2_i2v_A14b_low_noise_lora_rank64_lightx2v_4step", "speed-wan22-i2v-low-4step")]),
           variants=variants_list(gguf_variants("QuantStack/Wan2.2-S2V-14B-GGUF")) + convrot("noctrex/Wan2.2-S2V-14B-int8_convrot", r"\.safetensors$"),
           parts=[dict(role="vae", **WAN21_VAE), dict(role="t5xxl", **UMT5), dict(role="audio-encoder", **WAV2VEC)])
    recipe(id="wan2.1-flf2v-14b-720p", name="Wan 2.1 FLF2V 14B (first + last frame)", kind="video", family="Wan",
           about="Makes the motion between a start picture and an end picture you choose (720p model). GPU 12 GB+.",
           steps=20, cfg=6.0, flowShift=3, width=832, height=480, frames=33, negative=WAN_NEG, fps=16, input="required",
           tasks=["first-last"],
           speed=speed("⚡ Lightx2v · 4 steps", 4, 1.0, [lora("lightx2v/Wan2.1-Distill-Loras", r"wan2\.1_i2v_lora_rank64_lightx2v_4step\.safetensors$", "speed-wan21-i2v-14b-lightx2v-4step")]),
           variants=variants_list(gguf_variants("city96/Wan2.1-FLF2V-14B-720P-gguf")),
           parts=[dict(role="vae", **WAN21_VAE), dict(role="t5xxl", **UMT5), dict(role="clip_vision", **CLIP_VISION_H)])

# ── 2026 video models: MiniMax H3, LTX-2.5, HunyuanVideo 1.5, LingBot-Video ──
H3 = "unsloth/MiniMax-H3-GGUF"
H3_VAE = one(H3, r"^vae/minimax_h3_video_vae_fp16\.safetensors$")
H3_AVAE = one(H3, r"^vae/minimax_h3_audio_vae_fp32\.safetensors$")
H3_TE32 = one(H3, r"qwen3vl_32b_minimax_h3-Q4_K_M\.gguf$")
H3_TE4 = one("PrunaAI/P-H3-Image-Comfy", r"text_encoders/p-h3-image_text_encoder_bf16\.safetensors$")
H3_TE_UNC = one("webmp3/Sakura-MiniMax-H3-Uncensored-GGUF", r"Q4_K_M.*\.gguf$")
H3_NEG = "worst quality, low quality, blurry, distorted, artifacts, static"
def h3(rid, name, about, variants, te, **k):
    if not (H3_VAE and H3_AVAE and te):
        return
    k.setdefault("tasks", ["create", "animate", "first-last"])
    k.setdefault("input", "optional")
    recipe(id=rid, name=name, kind="video", family="MiniMax-H3", about=about,
           steps=k.pop("steps", 20), cfg=k.pop("cfg", 1.0), width=864, height=480, frames=56, fps=24, negative=H3_NEG,
           variants=variants, parts=[dict(role="vae", **H3_VAE), dict(role="audio-vae", **H3_AVAE), dict(role="llm", **te)], **k)
fl2va = variants_list(gguf_variants(H3, include=r"fl2va_pruned"))
ref2va = variants_list(gguf_variants(H3, include=r"ref2va_pruned"))
h3("minimax-h3", "MiniMax H3 (video + sound)",
   "MiniMax's Hailuo H3 (open weights): video with matching stereo sound, from text, a start picture, or a first and last picture. "
   "Uses the compact 4B text encoder. GPU 12 GB+ and 32 GB RAM.", fl2va, H3_TE4)
h3("minimax-h3-32b", "MiniMax H3 · 32B encoder (best)",
   "MiniMax H3 with its full Qwen3-VL 32B text encoder: follows long, detailed prompts best. Needs lots of RAM (48 GB+).", fl2va, H3_TE32)
h3("minimax-h3-ref2va", "MiniMax H3 Ref2VA (people & places from pictures)",
   "MiniMax H3 that puts the people, objects and places of your reference pictures (or your 🎭 cast) into a video with sound.",
   ref2va, H3_TE4, tasks=["create", "reference"], refs=3)
h3("minimax-h3-uncensored", "MiniMax H3 Uncensored (Sakura encoder)",
   "MiniMax H3 with the uncensored Sakura text encoder: no content filter. Needs lots of RAM.", fl2va, H3_TE_UNC, uncensored=True)
HF = "VideoRebirthAI/hyperflow-h3-flash"
h3("hyperflow-h3-flash", "HyperFlow H3 Flash 14B (fast, sound)",
   "A 14B distilled MiniMax H3: video with sound in 6 steps. Fits 12 GB GPUs better.",
   variants_list(gguf_variants(HF, folder="fl2va_gguf")), H3_TE4, steps=6, cfg=1.0, extra="--sigmas hyperflow-h3-flash-6")
h3("hyperflow-h3-flash-ref2va", "HyperFlow H3 Flash Ref2VA (fast, from pictures)",
   "Fast (6 steps) MiniMax H3 that uses your reference pictures or 🎭 cast.",
   variants_list(gguf_variants(HF, folder="ref2va_gguf")), H3_TE4, steps=6, cfg=1.0, extra="--sigmas hyperflow-h3-flash-6",
   tasks=["create", "reference"], refs=3)

# ── more uncensored video: 10Eros, PinkCherry, AfterMidnight (MiniMax H3 + LTX-2.3), NSFW Wan 2.2 merges ──
def cr_list(repo, items):
    """[(label, regex)] -> ConvRot variants that exist."""
    out = []
    for label, rx in items:
        f = one(repo, rx)
        if f: out.append(dict(label=label, format="convrot", **f))
    return out
ER = "Abiray/"
h3("10eros-max-h3", "10Eros Max · MiniMax H3 (NSFW, sound)",
   "10Eros Max: the most popular uncensored MiniMax H3 fine-tune (motion and detail from LTX-2.3, Wan 2.2 and Krea 2). "
   "Video with sound from text, a start picture, or first + last picture. No content filter. GPU 12 GB+ and 32 GB RAM.",
   variants_list(gguf_variants(ER + "10Eros-Max-GGUF")) +
   cr_list("cicalooo/10Eros-Max-h3-int8-convrot", [("INT8 ConvRot", r"^10Eros_Max_h3_fl2va_beta2_pruned_int8_convrot\.safetensors$")]),
   H3_TE4, uncensored=True)
h3("10eros-max-h3-turbo", "10Eros Max Turbo · MiniMax H3 (NSFW, 8 steps)",
   "10Eros Max Turbo beta 7: uncensored MiniMax H3 with sound in only 8 steps (several times faster). Keep the guidance at 1.",
   variants_list(gguf_variants(ER + "10Eros-Max-Turbo-Beta7-GGUF")), H3_TE4, uncensored=True, steps=8, cfg=1.0)
h3("10eros-max-h3-turbo-hybrid", "10Eros Max Turbo Hybrid · MiniMax H3 (NSFW, fast)",
   "10Eros Max Turbo Hybrid beta 5: fast (8 steps) uncensored MiniMax H3 with more motion variety.",
   variants_list(gguf_variants(ER + "10Eros-Max-Turbo-Hybrid-Beta5-GGUF")) +
   cr_list("cicalooo/10Eros-Max-h3-int8-convrot", [("INT8 ConvRot (beta 3)", r"^10Eros_Max_h3_TURBO-hybrid_beta3_int8_convrot\.safetensors$")]),
   H3_TE4, uncensored=True, steps=8, cfg=1.0)
h3("10eros-max-h3-hybrid", "10Eros Max Hybrid · MiniMax H3 (NSFW)",
   "10Eros Max Hybrid beta 5 at full quality (20 steps): uncensored MiniMax H3 with sound.",
   variants_list(gguf_variants(ER + "10Eros-Max-Hybrid-Beta5-GGUF")), H3_TE4, uncensored=True)
h3("10eros-max-h3-ref2va", "10Eros Max Ref2VA · MiniMax H3 (NSFW, from pictures)",
   "Uncensored 10Eros Max that puts the people of your reference pictures (or your 🎭 cast) into a video with sound.",
   variants_list(gguf_variants(ER + "10Eros-Max-ref2va-Beta2-GGUF")) +
   cr_list("cicalooo/10Eros-Max-h3-int8-convrot", [("INT8 ConvRot", r"^10Eros_Max_h3_ref2va_beta2_pruned_int8_convrot\.safetensors$")]),
   H3_TE4, uncensored=True, tasks=["create", "reference"], refs=3)
h3("10eros-max-h3-turbo-ref2va", "10Eros Max Turbo Ref2VA · MiniMax H3 (NSFW, fast, from pictures)",
   "Fast (8 steps) uncensored 10Eros Max for your reference pictures or 🎭 cast.",
   cr_list("cicalooo/10Eros-Max-h3-int8-convrot", [("INT8 ConvRot", r"^10Eros_Max_h3_TURBO_ref2va_beta2_int8_convrot\.safetensors$")]),
   H3_TE4, uncensored=True, tasks=["create", "reference"], refs=3, steps=8, cfg=1.0)
PC = "SexGod1979/PinkCherry-NSFW_MiniMax-H3"
h3("pinkcherry-h3", "PinkCherry · MiniMax H3 (NSFW, sound)",
   "PinkCherry: an uncensored MiniMax H3 fine-tune for adult scenes, with sound. Text, start picture or first + last picture.",
   cr_list(PC, [("INT8 ConvRot", r"^beta-0\.6-fl2va/PinkCherry_fl2va_MiniMax_H3_pruned_int8_convrot-beta-0\.6\.safetensors$")]),
   H3_TE4, uncensored=True)
h3("pinkcherry-h3-ref2va", "PinkCherry Ref2VA · MiniMax H3 (NSFW, from pictures)",
   "Uncensored PinkCherry that uses your reference pictures or 🎭 cast.",
   cr_list(PC, [("INT8 ConvRot", r"^ref2va-beta-06/pinkcherrry_minimax_h3_ref2va_beta-0\.6_int8_convrot\.safetensors$")]),
   H3_TE4, uncensored=True, tasks=["create", "reference"], refs=3)
AM = "SexGod1979/AfterMidnight-MiniMax-H3-NSFW"
h3("aftermidnight-h3-ref2va", "AfterMidnight · MiniMax H3 Ref2VA (NSFW LoRA)",
   "MiniMax H3 Ref2VA with the AfterMidnight NSFW LoRA (adult scenes, coherent motion) Uses your reference pictures or 🎭 cast.",
   ref2va, H3_TE4, uncensored=True, tasks=["create", "reference"], refs=3,
   loras=[lora(AM, r"sexytime_rank64-v1\.2\.safetensors$", "builtin-h3-aftermidnight-v1.2")])

# ── the original 10Eros Max files (TenStrip) and their best-known merges, as more file choices ──
def stv(repo, pattern, label):
    """One safetensors file as a variant: INT8 files get the ConvRot repair, full files load as they are."""
    f = one(repo, pattern)
    if not f: return []
    return [dict(label=label, format="convrot" if re.search(r"int8|convrot", f["path"], re.I) else "safetensors", **f)]
def add_variants(rid, extra):
    for r in R:
        if r["id"] == rid:
            r["variants"] = sorted(r["variants"] + extra, key=lambda x: x["size"])
TS = "TenStrip/10Eros-Max"
add_variants("10eros-max-h3", stv(TS, r"^10Eros_Max_h3_fl2va_beta2_pruned\.safetensors$", "BF16 beta2 (TenStrip)") +
             stv("CornLogic/10EROS-INT8", r"fl2va_INT8_test4_pruned", "INT8 test4 (CornLogic)"))
add_variants("10eros-max-h3-turbo", stv(TS, r"_8stepTURBO_beta7_int8HQ\.safetensors$", "INT8 HQ (TenStrip)") +
             stv(TS, r"_8stepTURBO_beta7_fix1-native_int8_protected_edges_HQ", "INT8 HQ fix1 (TenStrip)") +
             stv(TS, r"_8stepTURBO_beta7\.safetensors$", "BF16 (TenStrip)") +
             stv(TS, r"_8stepTURBO_beta7_fix1\.safetensors$", "BF16 fix1 (TenStrip)"))
add_variants("10eros-max-h3-turbo-hybrid", stv(TS, r"TURBO-hybrid_beta5_int8\.safetensors$", "INT8 (TenStrip)") +
             stv(TS, r"TURBO-hybrid_beta5\.safetensors$", "BF16 (TenStrip)") +
             stv(TS, r"TURBO-hybrid_beta4_int8_convrot", "INT8 beta4 (TenStrip)") +
             variants_list(gguf_variants("electroglyph/10Eros_Max_h3_TURBO-hybrid_beta3-GGUF")))
add_variants("10eros-max-h3-hybrid", stv(TS, r"h3_hybrid_beta5_int8\.safetensors$", "INT8 (TenStrip)") +
             stv(TS, r"h3_hybrid_beta5\.safetensors$", "BF16 (TenStrip)"))
add_variants("10eros-max-h3-ref2va", stv(TS, r"h3_ref2va_beta2_pruned\.safetensors$", "BF16 (TenStrip)"))
add_variants("10eros-max-h3-turbo-ref2va", stv(TS, r"TURBO_ref2va_beta2\.safetensors$", "BF16 (TenStrip)"))
add_variants("pinkcherry-h3", stv(PC, r"1\.8-hybrid/.*int8.*\.safetensors$", "INT8 hybrid v1.8") +
             stv(PC, r"v1\.5-fl2va/pinkcherry_v1\.5_MiniMaxH3_fl2va_int8_pruned", "INT8 v1.5"))
h3("10eros-max-h3-turbo-v1-hybrid", "10Eros Max Turbo v1.0 Hybrid · MiniMax H3 (NSFW, 8 steps)",
   "The newest 10Eros Max (TenStrip): beta 5 + beta 7 + Sulphur3 on a first/last-frame hybrid, made for several reference "
   "pictures or first + last frame, 8 steps. GPU 16 GB+ or lots of RAM.",
   stv(TS, r"8stepTurbo_v1\.0_hybrid-int8_HQ", "INT8 HQ") + stv(TS, r"8stepTurbo_v1\.0_hybrid\.safetensors$", "BF16"),
   H3_TE4, uncensored=True, steps=8, cfg=1.0)
h3("10eros-max-h3-raw", "10Eros Max Raw beta 7 · MiniMax H3 (NSFW)",
   "10Eros Max beta 7 without the built-in turbo: full quality and the best sound (about 20 steps).",
   stv(TS, r"h3_raw_beta7_int8", "INT8"), H3_TE4, uncensored=True)
OR = "OliviaRossi/Minimax-h3_Singularity-x-10Eros-Max_PRISM"
h3("singularity-10eros-prism", "Singularity × 10Eros Max PRISM · MiniMax H3 (NSFW)",
   "The most downloaded 10Eros merge: Singularity V2 × 10Eros Max (PRISM). Uncensored video with sound.",
   stv(OR, r"Minimax-h3_Singularity_x_10Eros-Max_PRISM_int8\.safetensors$", "INT8") +
   stv(OR, r"^PRISM_DMAD_PDMD_int8\.safetensors$", "INT8 DMAD-PDMD") +
   stv(OR, r"PRISM_fp16\.safetensors$", "FP16"), H3_TE4, uncensored=True)
h3("singularity-10eros-pikaflex", "Singularity V2 × 10Eros Max PikaFlex · MiniMax H3 (NSFW)",
   "Singularity V2 × 10Eros Max with PikaFlex motion (0.85 or 1.00 strength).",
   stv(OR, r"SingularityV2_PikaFlex_0\.85_int8HQ", "INT8 HQ · 0.85") + stv(OR, r"SingularityV2_PikaFlex_1\.00_int8HQ", "INT8 HQ · 1.00") +
   stv(OR, r"PRISM2_PikaFlex_0\.85_int8HQ", "INT8 HQ · PRISM2 0.85"), H3_TE4, uncensored=True)
h3("singularity-10eros-turbo", "Singularity V2 × 10Eros Max Turbo · MiniMax H3 (NSFW, 8 steps)",
   "10Eros Max Turbo beta 7 / Turbo-Hybrid beta 5 with Singularity: fast uncensored video with sound in 8 steps.",
   stv(OR, r"8stepTURBO_beta7_SingularityV2_int8HQ", "INT8 HQ · beta 7") + stv(OR, r"TURBO-hybrid_beta5_Singularity_v1\.3_int8", "INT8 · hybrid beta 5"),
   H3_TE4, uncensored=True, steps=8, cfg=1.0)


# LTX-2.3 dev fine-tunes (10Eros, PinkCherry): dev parts; a distilled LoRA makes them fast
L23_DEV = [dict(role="vae", **one(LTX, r"vae/ltx-2\.3-22b-dev_video_vae")),
           dict(role="audio-vae", **one(LTX, r"vae/ltx-2\.3-22b-dev_audio_vae")),
           dict(role="llm", **GEMMA3_12B),
           dict(role="embeddings-connectors", **one(LTX, r"text_encoders/ltx-2\.3-22b-dev_embeddings_connectors"))]
def ltx23(rid, name, about, variants, spd, **k):
    recipe(id=rid, name=name, kind="video", family="LTX", about=about, uncensored=True,
           steps=k.pop("steps", 30), cfg=k.pop("cfg", 3.0), width=1280, height=704, frames=97, input="optional", fps=24,
           negative="worst quality, low quality, blurry, distorted, artifacts, subtitles, text",
           tasks=["create", "animate"], speed=spd, variants=variants, parts=L23_DEV, **k)
DMD = lambda: speed("⚡ DMD · 8 steps", 8, 1.0, [lora("TenStrip/LTX2.3_DMD_Lora", r"LTX2\.3_DMD_hybrid_v2\.safetensors$", "speed-ltx23-dmd-hybrid-v2")])
ltx23("ltx-2.3-10eros-1.5", "10Eros 1.5 · LTX-2.3 (NSFW, sound)",
      "10Eros v1.5: the popular uncensored LTX-2.3 fine-tune with sound, best for picture to video. Write detailed, directive prompts. GPU 12 GB+ and 32 GB RAM.",
      variants_list(gguf_variants("vantagewithai/LTX2.3-10Eros-1.5-GGUF")), DMD())
ltx23("ltx-2.3-10eros", "10Eros v1 · LTX-2.3 (NSFW, sound)",
      "The first 10Eros LTX-2.3 remix: uncensored video with sound from text or a picture.",
      variants_list(gguf_variants("vantagewithai/LTX2.3-10Eros-GGUF")), DMD())
ltx23("pinkcherry-ltx-2.3", "PinkCherry · LTX-2.3 (NSFW, sound)",
      "PinkCherry v1.8: uncensored LTX-2.3 fine-tune for adult scenes, with sound. Text or picture to video.",
      variants_list(gguf_variants("SexGod1979/PinkCherry_NSFW_LTX23", folder="v1.8/")),
      speed("⚡ Distilled · 8 steps", 8, 1.0, [lora("SexGod1979/PinkCherry_NSFW_LTX23", r"distil_lora/.*condsafe\.safetensors$", "speed-ltx23-distilled-condsafe")]))
for r in R:
    if r["id"] == "pinkcherry-ltx-2.3":
        for v in r["variants"]: v["label"] = v["label"].split("_V")[0]
recipe(id="ltx-2.3-nsfw-motion", name="LTX-2.3 NSFW motion (fast, sound)", kind="video", family="LTX", uncensored=True,
       about="The fast LTX-2.3 distilled model with an NSFW motion LoRA: uncensored video with sound in 8 steps.",
       steps=8, cfg=1.0, width=1280, height=704, frames=97, input="optional", fps=24,
       negative="worst quality, low quality, blurry, distorted, artifacts",
       tasks=["create", "animate"],
       loras=[lora("lynaNSFW/LTX2.3_NSFW_motion", r"NSFWMOTION_00750\.safetensors$", "builtin-ltx23-nsfw-motion")],
       variants=variants_list(gguf_variants(LTX, folder="distilled-1.1/", include=r"^(?!.*UD-)")),
       parts=[dict(role="vae", **one(LTX, r"vae/ltx-2\.3-22b-distilled_video_vae")),
              dict(role="audio-vae", **one(LTX, r"vae/ltx-2\.3-22b-distilled_audio_vae")),
              dict(role="llm", **GEMMA3_12B),
              dict(role="embeddings-connectors", **one(LTX, r"text_encoders/ltx-2\.3-22b-distilled_embeddings_connectors"))])

# Wan 2.2 NSFW merges (speed already built in: 4 + 4 steps, guidance 1)
NOCONV = r"^\b$"   # (no ConvRot copy)
wan_a14b("smoothmix-wan2.2-i2v", "SmoothMix Wan 2.2 I2V (NSFW, fast)",
         "SmoothMix: a popular uncensored Wan 2.2 merge with smooth motion and the speed-up built in (4 + 4 steps). Photo or text to video. GPU 12 GB+.",
         "Bedovyy/smoothMixWan22-I2V-GGUF", NOCONV, NOCONV, "optional", ["animate", "create", "first-last"], uncensored=True)
wan_a14b("dasiwa-wan2.2-i2v", "DaSiWa Wan 2.2 I2V Midnight Flirt (NSFW, fast)",
         "DaSiWa Midnight Flirt: uncensored Wan 2.2 merge with the Lightspeed speed-up built in (4 + 4 steps). Animates a photo. GPU 12 GB+.",
         "Bedovyy/dasiwaWAN22I2V14B-GGUF", NOCONV, NOCONV, "required", ["animate", "first-last"], uncensored=True)
for rid in ("smoothmix-wan2.2-i2v", "dasiwa-wan2.2-i2v"):
    for r in R:
        if r["id"] == rid: r.update(steps=4, highSteps=4, cfg=1.0, flowShift=5)
EN = "gfaione/wan2.2_enhanced_nsfw"
ehi, elo = one(EN, r"FASTMOVEQ6KHigh\.gguf$"), one(EN, r"FASTMOVEQ6KLow\.gguf$")
ehi2, elo2 = one(EN, r"FASTMOVEV2Q4KMH\.gguf$"), one(EN, r"FASTMOVEV2Q4KML\.gguf$")
recipe(id="wan2.2-enhanced-nsfw-fastmove", name="Wan 2.2 Enhanced NSFW FastMove (fast)", kind="video", family="Wan", uncensored=True,
       about="Enhanced NSFW SVI Camera FastMove: uncensored Wan 2.2 with more camera and body motion, speed-up built in (4 + 4 steps). GPU 12 GB+.",
       steps=4, highSteps=4, cfg=1.0, flowShift=5, width=832, height=480, frames=81, negative=WAN_NEG, input="optional", fps=16,
       tasks=["animate", "create", "first-last"],
       variants=([dict(label="Q4_K_M (v2)", format="gguf", **elo2, high=ehi2, size_total=elo2["size"] + ehi2["size"])] if elo2 and ehi2 else []) +
                ([dict(label="Q6_K", format="gguf", **elo, high=ehi, size_total=elo["size"] + ehi["size"])] if elo and ehi else []),
       parts=[dict(role="vae", **WAN21_VAE), dict(role="t5xxl", **UMT5)])
recipe(id="wan2.2-rapid-aio-nsfw", name="Wan 2.2 Rapid AIO NSFW v10 (4 steps)", kind="video", family="Wan", uncensored=True,
       about="Phr00t's Rapid All-In-One NSFW v10: one uncensored 14B Wan 2.2 model (no second expert) that makes video in only 4 steps. Photo or text to video.",
       steps=4, cfg=1.0, flowShift=5, width=832, height=480, frames=81, negative=WAN_NEG, input="optional", fps=16,
       tasks=["animate", "create"],
       variants=variants_list(gguf_variants("desirel/WAN2.2-14B-Rapid-AllInOne-GGUF-NSFW-v10")),
       parts=[dict(role="vae", **WAN21_VAE), dict(role="t5xxl", **UMT5)])


L25 = "unsloth/LTX-2.5-GGUF"
L25_PARTS = [dict(role="vae", **one("Lightricks/LTX-2.5", r"^vae/ltx-2\.5-video-vae-conv-bf16\.safetensors$")),
             dict(role="audio-vae", **one("Lightricks/LTX-2.5", r"^vae/ltx-2\.5-audio-vae-bf16\.safetensors$")),
             dict(role="llm", **one("elix3r/gemma4-12b-with-proj-ltx-2.5-GGUF", r"Q4_K_M\.gguf$"))]
recipe(id="ltx-2.5-distilled", name="LTX-2.5 distilled (video + sound)", kind="video", family="LTX-2.5",
       about="LTX-2.5 (August 2026): sharper video with matching sound, multi-shot prompts, 8 steps. Text or picture to video, 24 fps. GPU 12 GB+ and 32 GB RAM.",
       steps=8, cfg=1.0, width=1280, height=704, frames=121, fps=24, input="optional", tasks=["create", "animate"],
       negative="worst quality, low quality, blurry, distorted, artifacts",
       variants=variants_list(gguf_variants(L25, folder="distilled/")), parts=L25_PARTS)
recipe(id="ltx-2.5-dev", name="LTX-2.5 dev (best quality, sound)", kind="video", family="LTX-2.5",
       about="The full LTX-2.5 (20+ steps): the best quality of LTX, with sound. Slower than the distilled one.",
       steps=20, cfg=3.0, width=1280, height=704, frames=121, fps=24, input="optional", tasks=["create", "animate"],
       negative="worst quality, low quality, blurry, distorted, artifacts",
       variants=[v for v in variants_list(gguf_variants(L25)) if not v["path"].startswith("distilled/")], parts=L25_PARTS)

HV = "Comfy-Org/HunyuanVideo_1.5_repackaged"
HV_PARTS = [dict(role="vae", **one(HV, r"vae/hunyuanvideo15_vae_fp16\.safetensors$")),
            dict(role="llm", **QWEN25VL_7B),
            dict(role="t5xxl", **one(HV, r"text_encoders/byt5_small_glyphxl_fp16\.safetensors$"))]
for res, w, h_ in (("480p", 848, 480), ("720p", 1280, 720)):
    for kind, tasks, inp in (("t2v", ["create"], "none"), ("i2v", ["animate"], "required")):
        repo = f"jayn7/HunyuanVideo-1.5_{kind.upper()}_{res}-GGUF"
        sp = None
        if kind == "t2v" and res == "480p":
            sp = speed("⚡ Lightx2v · 4 steps", 4, 1.0, [lora(HV, r"loras/hunyuanvideo1\.5_t2v_480p_lightx2v_4step", "speed-hunyuan15-t2v-480p-4step")])
        recipe(id=f"hunyuanvideo1.5-{kind}-{res}", name=f"HunyuanVideo 1.5 {kind.upper()} {res}", kind="video", family="HunyuanVideo",
               about=f"Tencent's HunyuanVideo 1.5 (8B): {'text to video' if kind == 't2v' else 'animates a picture'} at {res}, strong motion and text in video. GPU 8 GB+.",
               steps=20, cfg=6.0, width=w, height=h_, frames=33, fps=24, input=inp, tasks=tasks, speed=sp,
               negative="worst quality, low quality, blurry, distorted",
               variants=variants_list(gguf_variants(repo, folder=f"{res}/")), parts=HV_PARTS)
        recipe(id=f"hunyuanvideo1.5-{kind}-{res}-distilled", name=f"HunyuanVideo 1.5 {kind.upper()} {res} (fast, CFG-distilled)", kind="video", family="HunyuanVideo",
               about=f"HunyuanVideo 1.5 {kind.upper()} {res}, CFG-distilled: about twice as fast at the same steps.",
               steps=20, cfg=1.0, width=w, height=h_, frames=33, fps=24, input=inp, tasks=tasks,
               negative="",
               variants=variants_list(gguf_variants(repo, folder=f"{res}_distilled/")), parts=HV_PARTS)

LB = "off-grid-ai/LingBot-Video-Dense-1.3B-GGUF"
recipe(id="lingbot-video-dense-1.3b", name="LingBot-Video 1.3B (tiny)", kind="video", family="LingBot",
       about="A very small text-to-video model (1.3B, under 1.5 GB): short clips even on 6 GB GPUs or CPU.",
       steps=20, cfg=3.0, width=832, height=480, frames=33, fps=16, tasks=["create"],
       negative="low quality, worst quality, blurry, flickering, watermark, text",
       variants=variants_list(gguf_variants(LB)), parts=[dict(role="vae", **WAN21_VAE), dict(role="llm", **QWEN3VL_4B)])

# ── community models from Hugging Face (copy a family's settings) ───────────
import media_auto
used = set()
for r in R:
    for v in r["variants"]:
        used.add(v["repo"])
for r in R:     # popularity of the hand-made ones, for sorting
    try:
        r["dl"] = media_auto._get(f"https://huggingface.co/api/models/{r['variants'][0]['repo']}?expand[]=downloads")["data"].get("downloads", 0)
    except Exception:
        r["dl"] = 0
    r["repo"] = r["variants"][0]["repo"]
if os.environ.get("NO_AUTO") != "1":
    R.extend(media_auto.build(R, used))

dest = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "LocalMind", "Resources", "media_catalog.json")
json.dump(R, open(dest, "w"), ensure_ascii=False, indent=1)
print(len(R), "recipes ->", dest, file=sys.stderr)
# ── voice & music (llama.cpp llama-tts) ─────────────────────────────────────
A = []
def audio(id, name, kind, about, repo, model, mmproj, clone=False, langs=None, transcript=False, design=False):
    m, mm = one(repo, model), one(repo, mmproj)
    if not (m and mm):
        print("  ! skip", id, file=sys.stderr); return
    A.append(dict(id=id, name=name, kind=kind, about=about, model=m, mmproj=mm, clone=clone,
                  langs=langs or [], transcript=transcript, design=design))
CB_LANGS = ["ar","da","de","el","en","es","fi","fr","he","hi","it","ja","ko","ms","nl","no","pl","pt","ru","sv","sw","tr","zh"]
audio("chatterbox-ml", "Chatterbox Multilingual", "voice",
      "Natural speech in 23 languages, Greek included. Clones a voice from a short recording.",
      "ggml-org/chatterbox-GGUF", r"^chatterbox-Q8_0\.gguf$", r"^mmproj-chatterbox-F16\.gguf$", clone=True, langs=CB_LANGS)
audio("chatterbox-turbo", "Chatterbox Turbo (English)", "voice",
      "Fast, expressive English speech with voice cloning.",
      "ggml-org/chatterbox-turbo-GGUF", r"^chatterbox-turbo-Q8_0\.gguf$", r"^mmproj-chatterbox-turbo-F16\.gguf$", clone=True, langs=["en"])
audio("qwen3-tts", "Qwen3-TTS 1.7B", "voice",
      "Very close voice cloning (give the words of the recording for best results). 10 languages: en, zh, de, it, pt, es, ja, ko, fr, ru.",
      "ggml-org/Qwen3-TTS-12Hz-1.7B-Base-GGUF", r"Base-Q8_0\.gguf$", r"^mmproj-.*-Q8_0\.gguf$", clone=True,
      langs=["en","zh","de","it","pt","es","ja","ko","fr","ru"], transcript=True)
audio("voxcpm2", "VoxCPM2", "voice",
      "Clones voices, or designs a new voice from a description like (a calm older man). 48 kHz audio.",
      "ggml-org/VoxCPM2-GGUF", r"^VoxCPM2-Q8_0\.gguf$", r"^mmproj-VoxCPM2-BF16\.gguf$", clone=True, design=True)
audio("s2-pro", "Fish Audio S2 Pro", "voice",
      "Top-quality speech and cloning; several speakers and cues like [laughing] or [whisper]. Needs 8 GB+ VRAM.",
      "ggml-org/s2-pro-GGUF", r"^s2-pro-Q8_0\.gguf$", r"^mmproj-s2-pro-BF16\.gguf$", clone=True, transcript=True)
audio("yue2-3b", "YuE2 3B (songs)", "music",
      "Full songs with vocals and instruments from a style and lyrics, 48 kHz stereo, up to 6 minutes.",
      "ggml-org/YuE2-3B-GGUF", r"^YuE2-3B-Q8_0\.gguf$", r"^mmproj-YuE2-3B-BF16\.gguf$")
audio("minimax-music3", "MiniMax Music 3 (songs)", "music",
      "High-quality songs with vocals from a description and lyrics, 44.1 kHz stereo. Needs 16 GB+ VRAM or lots of RAM.",
      "ggml-org/MiniMax-Music3-GGUF", r"^MiniMax-Music3-Q8_0\.gguf$", r"^mmproj-MiniMax-Music3-BF16\.gguf$")
for a in A:     # popularity, for the "most popular" list
    try:
        a["dl"] = media_auto._get(f"https://huggingface.co/api/models/{a['model']['repo']}?expand[]=downloads")["data"].get("downloads", 0)
    except Exception:
        a["dl"] = 0
adest = os.path.join(os.path.dirname(dest), "audio_catalog.json")
json.dump(A, open(adest, "w"), ensure_ascii=False, indent=1)
print(len(A), "voice/music models ->", adest, file=sys.stderr)

for r in R:
    print(f"{r['id']:22} {len(r['variants']):2} variants  {', '.join(v['label'] for v in r['variants'])}  parts={[p['role'] for p in r['parts']]}")
