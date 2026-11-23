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
adest = os.path.join(os.path.dirname(dest), "audio_catalog.json")
json.dump(A, open(adest, "w"), ensure_ascii=False, indent=1)
print(len(A), "voice/music models ->", adest, file=sys.stderr)

for r in R:
    print(f"{r['id']:22} {len(r['variants']):2} variants  {', '.join(v['label'] for v in r['variants'])}  parts={[p['role'] for p in r['parts']]}")
