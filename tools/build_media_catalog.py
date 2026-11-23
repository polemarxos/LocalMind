"""Builds src/LocalMind/Resources/media_catalog.json: image & video models for the Studio.

Every recipe lists the diffusion model variants (GGUF quants and INT8 ConvRot) plus the
shared parts it needs (VAE, text encoder). All files are looked up on Hugging Face so the
app only offers files that exist, with their real sizes.

    python tools/build_media_catalog.py
"""
import json, os, re, sys, urllib.request

def tree(repo):
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
            return {"repo": repo, "path": f["path"], "size": f.get("lfs", {}).get("size", f.get("size", 0))}
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
        out[label] = {"repo": repo, "path": p, "size": size}
    return out

def variants_list(d, fmt="gguf"):
    return [dict(label=k, format=fmt, **v) for k, v in d.items()]

# ── shared parts ────────────────────────────────────────────────────────────
AE_FLUX1 = one("Comfy-Org/z_image_turbo", r"vae/ae\.safetensors$")
QWEN3_4B_INSTR = one("unsloth/Qwen3-4B-Instruct-2507-GGUF", r"Q4_K_M\.gguf$")
QWEN3_4B = one("unsloth/Qwen3-4B-GGUF", r"Qwen3-4B-Q4_K_M\.gguf$")
QWEN3_8B = one("unsloth/Qwen3-8B-GGUF", r"Qwen3-8B-Q4_K_M\.gguf$")
FLUX2_VAE = one("Comfy-Org/flux2-dev", r"vae/.*\.safetensors$")
QWEN3VL_8B = one("Qwen/Qwen3-VL-8B-Instruct-GGUF", r"Q4_K_M\.gguf$")
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
    k["variants"] = sorted(v, key=lambda x: x["size"])
    k["parts"] = parts
    R.append(k)

# ── images ──────────────────────────────────────────────────────────────────
recipe(id="z-image-turbo", name="Z-Image Turbo", kind="image", family="Z-Image",
       about="Fast photorealistic images in 8 steps, great text rendering. Best all-rounder for most GPUs.",
       steps=8, cfg=1.0, width=1024, height=1024, input="optional",
       variants=variants_list(gguf_variants("leejet/Z-Image-Turbo-GGUF")) + convrot(CONV, r"z_image_turbo_int8_convrot"),
       parts=[dict(role="vae", **AE_FLUX1), dict(role="llm", **QWEN3_4B_INSTR)])
recipe(id="z-image", name="Z-Image (base)", kind="image", family="Z-Image",
       about="Higher-quality, more controllable Z-Image (slower: 28 steps, uses negative prompts).",
       steps=28, cfg=5.0, width=1024, height=1024, negative="blurry, low quality, watermark",
       variants=variants_list(gguf_variants("unsloth/Z-Image-GGUF")),
       parts=[dict(role="vae", **AE_FLUX1), dict(role="llm", **QWEN3_4B_INSTR)])
recipe(id="flux2-klein-4b", name="FLUX.2 klein 4B", kind="image", family="FLUX.2",
       about="Small, very fast FLUX.2 (4 steps). Text-to-image and image editing. Good on 6–8 GB.",
       steps=4, cfg=1.0, width=1024, height=1024, input="optional",
       variants=variants_list(gguf_variants("leejet/FLUX.2-klein-4B-GGUF")),
       parts=[dict(role="vae", **FLUX2_VAE) if FLUX2_VAE else None, dict(role="llm", **QWEN3_4B)])
recipe(id="flux2-klein-9b", name="FLUX.2 klein 9B", kind="image", family="FLUX.2",
       about="Sharper FLUX.2 (4 steps) with editing. INT8 ConvRot gives near-full quality at half the size.",
       steps=4, cfg=1.0, width=1024, height=1024, input="optional",
       variants=variants_list(gguf_variants("leejet/FLUX.2-klein-9B-GGUF")) + convrot(CONV, r"flux-2-klein-9b_int8_convrot"),
       parts=[dict(role="vae", **FLUX2_VAE) if FLUX2_VAE else None, dict(role="llm", **QWEN3_8B)])
recipe(id="qwen-image-2.1", name="Qwen-Image 2.1", kind="image", family="Qwen-Image",
       about="Top prompt following and perfect text in images (English, Chinese…). Supports editing.",
       steps=20, cfg=6.0, width=1024, height=1024, input="optional",
       variants=variants_list(gguf_variants("leejet/Qwen-Image-2.1-GGUF", include=r"^(?!.*turbo)")) +
                convrot("Comfy-Org/Qwen-Image-2.1", r"diffusion_models/qwen_image_2\.1_int8_convrot"),
       parts=[dict(role="vae", **QI21_VAE), dict(role="llm", **QWEN3VL_8B)])
recipe(id="qwen-image-2.1-turbo", name="Qwen-Image 2.1 Turbo", kind="image", family="Qwen-Image",
       about="Fast Qwen-Image 2.1 (8 steps) with the same excellent text rendering.",
       steps=8, cfg=1.0, width=1024, height=1024,
       variants=variants_list(gguf_variants("leejet/Qwen-Image-2.1-GGUF", include=r"turbo")) +
                convrot("Comfy-Org/Qwen-Image-2.1", r"diffusion_models/qwen_image_2\.1_turbo_int8_convrot"),
       parts=[dict(role="vae", **QI21_VAE), dict(role="llm", **QWEN3VL_8B)])
recipe(id="qwen-image-2512", name="Qwen-Image 2512", kind="image", family="Qwen-Image",
       about="Large 20B Qwen-Image release: rich detail and layouts. Needs a big GPU or offloading.",
       steps=20, cfg=2.5, flowShift=3, width=1024, height=1024,
       variants=variants_list(gguf_variants("unsloth/Qwen-Image-2512-GGUF")) + convrot(CONV, r"qwen-image-2512-int8-ConvRot"),
       parts=[dict(role="vae", **QI_VAE), dict(role="llm", **QWEN25VL_7B)])
recipe(id="krea2-turbo", name="Krea 2 Turbo", kind="image", family="Krea",
       about="Aesthetic, photographic Krea 2 in few steps.",
       steps=8, cfg=1.0, width=1024, height=1024,
       variants=variants_list(gguf_variants("realrebelai/KREA-2_GGUFs", folder="TURBO")) + convrot(CONV, r"Krea2-Turbo-int8-ConvRot"),
       parts=[dict(role="vae", **WAN21_VAE), dict(role="llm", **QWEN3VL_4B)])
recipe(id="anima", name="Anima (anime)", kind="image", family="Anima",
       about="Anime and illustration style model. Small and fast.",
       steps=30, cfg=6.0, width=1024, height=1024, negative="worst quality, low quality, blurry",
       variants=variants_list(gguf_variants("Bedovyy/Anima-GGUF")) + convrot(CONV, r"anima-preview3-base-int8-ConvRot"),
       parts=[dict(role="vae", **QI_VAE), dict(role="llm", **QWEN3_06B_BASE)])
recipe(id="flux1-schnell", name="FLUX.1 schnell", kind="image", family="FLUX.1",
       about="Classic fast FLUX.1 (4 steps), Apache-2.0 licensed.",
       steps=4, cfg=1.0, width=1024, height=1024,
       variants=variants_list(gguf_variants("city96/FLUX.1-schnell-gguf")),
       parts=[dict(role="vae", **AE_FLUX1), dict(role="clip_l", **CLIP_L), dict(role="t5xxl", **T5XXL)])
sdxl = one("stabilityai/stable-diffusion-xl-base-1.0", r"^sd_xl_base_1\.0\.safetensors$")
recipe(id="sdxl", name="Stable Diffusion XL", kind="image", family="SDXL",
       about="The classic SDXL base model. Runs on 6 GB GPUs; huge community of styles.",
       steps=30, cfg=7.0, width=1024, height=1024, negative="blurry, low quality, deformed", input="optional",
       variants=[dict(label="FP16", format="checkpoint", **sdxl)] if sdxl else [],
       parts=[dict(role="vae", **SDXL_VAE)])

# ── video ───────────────────────────────────────────────────────────────────
recipe(id="wan2.2-ti2v-5b", name="Wan 2.2 TI2V 5B (video)", kind="video", family="Wan",
       about="Text or image to video on mid-range GPUs. ~2 s clips at 832×480.",
       steps=20, cfg=6.0, flowShift=3, width=832, height=480, frames=33, negative=WAN_NEG, input="optional",
       variants=variants_list(gguf_variants("QuantStack/Wan2.2-TI2V-5B-GGUF")),
       parts=[dict(role="vae", **WAN22_VAE), dict(role="t5xxl", **UMT5)])
w13 = one("Comfy-Org/Wan_2.1_ComfyUI_repackaged", r"diffusion_models/wan2\.1_t2v_1\.3B_fp16\.safetensors$")
recipe(id="wan2.1-t2v-1.3b", name="Wan 2.1 T2V 1.3B (video)", kind="video", family="Wan",
       about="Smallest video model: short clips even on 8 GB GPUs.",
       steps=20, cfg=6.0, flowShift=3, width=832, height=480, frames=33, negative=WAN_NEG,
       variants=[dict(label="FP16", format="safetensors", **w13)] if w13 else [],
       parts=[dict(role="vae", **WAN21_VAE), dict(role="t5xxl", **UMT5)])

def wan_a14b(rid, name, about, repo, conv_hi, conv_lo, input_mode):
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
           steps=10, highSteps=8, cfg=3.5, flowShift=3, width=832, height=480, frames=33, negative=WAN_NEG, input=input_mode,
           variants=v, parts=[dict(role="vae", **WAN21_VAE), dict(role="t5xxl", **UMT5)])

wan_a14b("wan2.2-t2v-a14b", "Wan 2.2 T2V A14B (video)", "Best-quality text-to-video (two 14B experts). Needs lots of RAM; GPU 12 GB+.",
         "QuantStack/Wan2.2-T2V-A14B-GGUF", r"wan2\.2_t2v_high_int8_ConvRot", r"wan2\.2_t2v_low_int8_ConvRot", "none")
wan_a14b("wan2.2-i2v-a14b", "Wan 2.2 I2V A14B (video)", "Animate a photo into a video clip (two 14B experts). GPU 12 GB+.",
         "QuantStack/Wan2.2-I2V-A14B-GGUF", r"^diffusion_models/wan2\.2_i2v_high_int8_convrot", r"^diffusion_models/wan2\.2_i2v_low_int8_convrot", "required")

dest = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "LocalMind", "Resources", "media_catalog.json")
json.dump(R, open(dest, "w"), ensure_ascii=False, indent=1)
print(len(R), "recipes ->", dest, file=sys.stderr)
for r in R:
    print(f"{r['id']:22} {len(r['variants']):2} variants  {', '.join(v['label'] for v in r['variants'])}  parts={[p['role'] for p in r['parts']]}")
