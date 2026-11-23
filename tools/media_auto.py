"""Community picture and video models for the Studio, found on Hugging Face.

Used by build_media_catalog.py after the hand-made recipes: every GGUF / INT8 ConvRot / single-file
checkpoint of a family LocalMind already supports becomes a recipe that copies the settings and shared
parts (VAE, text encoder) of that family's hand-made "template" recipe. Families stable-diffusion.cpp
can't run (or that need inputs LocalMind doesn't have) are skipped.
"""
import copy, json, os, re, sys, time, urllib.request
from concurrent.futures import ThreadPoolExecutor

CACHE = os.environ.get("HF_CACHE", os.path.join(os.path.dirname(os.path.abspath(__file__)), ".hf-cache.json"))
_cache = {}
if os.path.exists(CACHE):
    try:
        _cache = json.load(open(CACHE))
    except Exception:
        _cache = {}


def _get(url):
    if url in _cache:
        return _cache[url]
    for attempt in range(6):
        try:
            r = urllib.request.urlopen(url, timeout=120)
            data = json.load(r)
            link = r.headers.get("Link", "") or ""
            m = re.search(r'<([^>]+)>;\s*rel="next"', link)
            res = {"data": data, "next": m.group(1) if m else None}
            _cache[url] = res
            return res
        except urllib.error.HTTPError as e:
            if e.code in (429, 500, 502, 503, 504):
                time.sleep(12 * (attempt + 1))
                continue
            return {"data": None, "next": None}
        except Exception:
            time.sleep(5)
    return {"data": None, "next": None}


def save_cache():
    try:
        json.dump(_cache, open(CACHE, "w"))
    except Exception:
        pass


def listing(query, pages=1):
    url = "https://huggingface.co/api/models?" + query
    out = []
    for _ in range(pages):
        res = _get(url)
        if not res["data"]:
            break
        out += res["data"]
        if not res["next"]:
            break
        url = res["next"]
    return out


def tree(repo):
    res = _get(f"https://huggingface.co/api/models/{repo}/tree/main?recursive=true")
    return [f for f in (res["data"] or []) if isinstance(f, dict) and f.get("type") == "file"]


def size_of(f):
    return f.get("lfs", {}).get("size", f.get("size", 0))


QUANT = re.compile(r"(?:^|[-_.])((?:UD-)?(?:I?Q\d(?:_[A-Z0-9]+)*|BF16|F16|F32))\.gguf$", re.I)
NSFW = re.compile(r"nsfw|uncensor|abliter|heretic|porn|hentai|lewd|nude|naked|erotic|10eros|sexy|xxx|\blust|pony", re.I)


def clean_name(repo, filename=None):
    n = repo.split("/", 1)[1]
    n = re.sub(r"[-_.]?(gguf|ggufs|int8[-_]?convrot|convrot[-_]?int8|int8|convrot|comfyui|comfy|safetensors)$", "", n, flags=re.I)
    n = re.sub(r"[-_.]?(gguf|int8[-_]?convrot|convrot|comfyui)$", "", n, flags=re.I)
    return n.replace("_", " ").strip(" -.")


# ── which family a model belongs to ─────────────────────────────────────────
SKIP_WORDS = re.compile(r"lora|controlnet|control-net|ip-?adapter|vae\b|text[-_]?encoder|clip[-_]?vision|umt5|t5xxl|t5-v1|qwen3[-_]?\d|qwen3vl|gemma|"
                        r"mmproj|refiner|redux|fill|depth|canny|layered|radiance|flf2v|phantom|magref|humo|chrono|animate|s2v|"
                        r"camera|control|inp\b|-inp-|dancer|avatar|flashhead|upscal|embedding|reward|hps|tts|audio|ace[-_]step|"
                        r"flux\.?[-_]?3|flux3|flux\.?2[-_.]?dev|flux2[-_]dev|ideogram|kandinsky|minimax|moss|sulphur|hidream|bagel|lens|ernie|ming|"
                        r"wan2\.?5|ltx[-_.]?2\.?5|ltx[-_.]?2[-_.]?(?!3)|ltxv|hunyuan|mochi|cosmos-predict|sdxl[-_]?turbo|sdxl[-_]?lightning|"
                        r"qwen[-_ ]?3[-_ .]?\d+(\.\d)?[-_ ]?b|qwen3\.5|qwen3_5|qwen35|prompt[-_ ]?enhanc|[-_]pe[-_]|pe[-_](i2i|t2i)|"
                        r"\bh3\b|[-_]h3|scail|lingbot|lynx|one-to-all|wan-move|movii|self-forcing|selfforcing|[-_]df[-_]|npu|ios\b|"
                        r"collection|based-?models|custom-models|models$|sd2|sd-2|stable-diffusion-2|v2-1|flux[-_.]?(lite|mini)|8b-flux|sensenova|joyai|bernini|verboa|magic-wan", re.I)


def classify_image(name, arch):
    n = name.lower()
    a = (arch or "").lower()
    if a == "lumina2" or re.search(r"z[-_ ]?image", n):
        return "z-image-turbo" if "turbo" in n else "z-image"
    if a == "krea2" or re.search(r"krea[-_ ]?2|krea2|kreamania|krea[-_]engineer", n):
        return "krea2-turbo"
    if "anima" in n and a in ("cosmos", ""):
        return "anima"
    if re.search(r"qwen[-_ ]?image[-_ ]?2\.?1|qwen_image21", n) or a == "qwen_image21":
        if "edit" in n:
            return None
        return "qwen-image-2.1-turbo" if "turbo" in n else "qwen-image-2.1"
    if re.search(r"qwen[-_ ]?image", n) or a == "qwen_image":
        return "qwen-image-edit-2511" if "edit" in n else "qwen-image-2512"
    if "chroma" in n:
        return "chroma1-flash" if "flash" in n else "chroma1-hd"
    if "klein" in n:
        return "flux2-klein-9b" if re.search(r"9b", n) else ("flux2-klein-4b" if re.search(r"4b", n) else None)
    if a == "flux" or "flux" in n:
        if "kontext" in n:
            return "flux1-kontext"
        if "schnell" in n:
            return "flux1-schnell"
        if re.search(r"flux[-_ .]?2|flux2", n):
            return None
        return "flux1-dev"
    return None


def classify_video(name, arch, dual):
    n = name.lower()
    if re.search(r"ltx[-_ .]?2[-_ .]?3", n):
        return "ltx-2.3-distilled" if "distill" in n else None
    if not re.search(r"wan|skyreels|fusionx", n):
        return None
    if "image" in n or not re.search(r"t2v|i2v|ti2v|vace|1\.3b|14b|5b|a14b|fusionx|skyreels", n):
        return None
    if dual:
        if "vace" in n:
            return "wan2.2-vace-fun-a14b"
        if "i2v" in n:
            return "wan2.2-i2v-a14b"
        return "wan2.2-t2v-a14b"
    if "vace" in n:
        return "wan2.1-vace-1.3b" if "1.3b" in n else "wan2.1-vace-14b"
    if "ti2v" in n or "5b" in n:
        return "wan2.2-ti2v-5b"
    if re.search(r"2\.2|a14b", n):
        return None          # single-file Wan 2.2 merges: unknown layout
    if "i2v" in n:
        return "wan2.1-i2v-14b-720p" if "720" in n else "wan2.1-i2v-14b-480p"
    if "1.3b" in n:
        return "wan2.1-t2v-1.3b"
    return "wan2.1-t2v-14b"


# ── make recipes ────────────────────────────────────────────────────────────
def build(R, used_repos):
    """Adds community recipes to R (the list of hand-made recipes)."""
    templates = {r["id"]: r for r in R}
    added = []
    seen_names = {r["name"].lower() for r in R}

    def make(tid, repo, name, variants, dl, tags, key_id=None):
        t = templates.get(tid)
        if not t or not variants:
            return
        key = name.lower()
        if key in seen_names:
            return
        seen_names.add(key)
        r = copy.deepcopy(t)
        r["id"] = "hf-" + re.sub(r"[^a-z0-9]+", "-", (key_id or repo).lower()).strip("-")
        r["name"] = name
        r["about"] = f"Community model by {repo.split('/')[0]} on Hugging Face, based on {t['name']}. " + \
                     ("Uncensored. " if (NSFW.search(repo) or "not-for-all-audiences" in tags) else "")
        r["variants"] = sorted(variants, key=lambda x: x.get("size_total", x["size"]))
        r["uncensored"] = bool(NSFW.search(repo) or "not-for-all-audiences" in tags)
        r["dl"] = dl
        r["repo"] = repo
        r["auto"] = True
        r.pop("loras", None)
        if "pony" in repo.lower() and t["family"] == "SDXL":
            r["prefix"] = "score_9, score_8_up, score_7_up, "
            r["extra"] = "--clip-skip 2"
        if re.search(r"v[-_]?pred", repo, re.I):
            r["extra"] = (r.get("extra", "") + " --prediction v").strip()
        added.append(r)

    # 1) GGUF picture & video models
    cands = {}
    for pipe in ("text-to-image", "image-to-image", "text-to-video", "image-to-video"):
        for m in listing(f"filter=gguf&pipeline_tag={pipe}&sort=downloads&limit=1000&expand[]=gguf&expand[]=downloads&expand[]=tags", pages=2):
            cands.setdefault(m["id"], (m, pipe))
    # repos without a pipeline tag (e.g. Chroma, Krea 2) — by name
    for q in ("chroma", "krea", "z-image", "qwen-image", "flux", "wan2", "wan2.1", "wan2.2", "fusionx", "skyreels", "anima"):
        for m in listing(f"search={q}&filter=gguf&sort=downloads&limit=300&expand[]=gguf&expand[]=downloads&expand[]=tags"):
            cands.setdefault(m["id"], (m, ""))
    print("  gguf candidates:", len(cands), file=sys.stderr)

    jobs = []
    for repo, (m, pipe) in cands.items():
        if repo in used_repos or m.get("downloads", 0) < 30 or SKIP_WORDS.search(repo):
            continue
        arch = (m.get("gguf") or {}).get("architecture") or ""
        jobs.append((repo, m, pipe, arch))
    with ThreadPoolExecutor(3) as ex:
        trees = dict(zip([j[0] for j in jobs], ex.map(lambda j: tree(j[0]), jobs)))
    for repo, m, pipe, arch in jobs:
        files = [f for f in trees[repo] if f["path"].lower().endswith(".gguf") and not SKIP_WORDS.search(os.path.basename(f["path"]))]
        if not files:
            continue
        low, high = {}, {}
        single = {}
        for f in files:
            mm = QUANT.search(os.path.basename(f["path"]))
            if not mm:
                continue
            label = mm.group(1).upper()
            if label == "F32":
                continue
            p = f["path"].lower()
            entry = {"repo": repo, "path": f["path"], "size": size_of(f)}
            if "high" in p:
                high[label] = entry
            elif "low" in p:
                low[label] = entry
            else:
                single[label] = entry
        stems = {re.sub(r"(high|low)[-_]?noise|[-_.]?(high|low)\b", "", QUANT.sub("", os.path.basename(f["path"])).lower())
                 for f in files if QUANT.search(os.path.basename(f["path"]))}
        if len(stems) > 1:
            continue     # a collection of different models in one repo
        dual = bool(high and low)
        is_video = bool(re.search(r"wan|ltx|skyreels|fusionx", repo, re.I)) and not re.search(r"image", repo, re.I)
        if is_video:
            tid = classify_video(repo, arch, dual)
        else:
            tid = classify_image(repo, arch)
        if not tid:
            continue
        if dual and tid.startswith("wan2.2-") and "a14b" in tid:
            variants = [dict(label=k, format="gguf", **low[k], high=high[k], size_total=low[k]["size"] + high[k]["size"]) for k in low if k in high]
        else:
            if tid in ("wan2.2-t2v-a14b", "wan2.2-i2v-a14b", "wan2.2-vace-fun-a14b"):
                continue
            variants = [dict(label=k, format="gguf", **v) for k, v in single.items() if v["size"] < 4e10]
        make(tid, repo, clean_name(repo), variants, m.get("downloads", 0), m.get("tags", []))

    # 2) INT8 ConvRot models
    conv = {}
    for q in ("int8-convrot", "int8_convrot", "convrot-int8", "convrot_int8", "int8 convrot"):
        for m in listing(f"search={urllib.request.quote(q)}&sort=downloads&limit=300&expand[]=downloads&expand[]=tags"):
            conv.setdefault(m["id"], m)
    print("  convrot candidates:", len(conv), file=sys.stderr)
    seen_sizes = {v.get("size") for r in R for v in r["variants"]}
    cj = [(r, m) for r, m in conv.items() if r not in used_repos and not re.search(r"int4|w4a|nvfp4|w4a4|w4a8|fp4", r, re.I)]
    with ThreadPoolExecutor(3) as ex:
        ctrees = dict(zip([j[0] for j in cj], ex.map(lambda j: tree(j[0]), cj)))
    for repo, m in cj:
        files = [f for f in ctrees[repo] if f["path"].lower().endswith(".safetensors") and re.search(r"convrot|int8", f["path"], re.I)
                 and not re.search(r"int4|w4a|nvfp4|fp4", f["path"], re.I) and 1e9 < size_of(f) < 3.2e10]
        for f in files:
            base = os.path.basename(f["path"])
            full = repo + "/" + base
            if SKIP_WORDS.search(base) or SKIP_WORDS.search(repo.split("/")[1]):
                continue
            label_src = base if len(files) > 1 else repo
            if re.search(r"wan|ltx", full, re.I):
                p = full.lower()
                if "high" in p or "low" in p:
                    continue     # Wan 2.2 expert pairs: only the hand-made ones
                tid = classify_video(full, "wan" if "wan" in p else "", False)
            else:
                tid = classify_image(full, "")
            if not tid:
                continue
            if size_of(f) in seen_sizes:
                continue     # the same file uploaded again elsewhere
            seen_sizes.add(size_of(f))
            stem = os.path.splitext(base)[0] if len(files) > 1 else repo.split("/")[1]
            stem = re.sub(r"[-_.]?(int8row|int8|convrot|comfyui|comfy|native|unet|simple|ctq|learned|256|tensorwise)\b", "", stem, flags=re.I)
            stem = re.sub(r"[-_.]?(int8|convrot)", "", stem, flags=re.I).replace("_", " ").strip(" -.")
            name = f"{stem} (INT8 ConvRot · {repo.split('/')[0]})"
            v = [dict(label="INT8 ConvRot", format="convrot", repo=repo, path=f["path"], size=size_of(f))]
            make(tid, repo, name, v, m.get("downloads", 0), m.get("tags", []), key_id=repo + "-" + base)

    # 3) Single-file SDXL and SD 1.5 checkpoints
    ck = {}
    for flt, pages in (("stable-diffusion-xl", 3), ("stable-diffusion", 2)):
        for m in listing(f"filter={flt}&filter=text-to-image&sort=downloads&limit=1000&expand[]=downloads&expand[]=tags&expand[]=siblings", pages=pages):
            ck.setdefault(m["id"], m)
    print("  checkpoint candidates:", len(ck), file=sys.stderr)
    kj = []
    for repo, m in ck.items():
        if repo in used_repos or m.get("downloads", 0) < 100 or "lora" in [t.lower() for t in m.get("tags", [])] or \
           any("stable-diffusion-2" in t.lower() or "sd-2" in t.lower() for t in m.get("tags", [])):
            continue
        if SKIP_WORDS.search(repo) or re.search(r"turbo|lightning|lcm|hyper|inpaint|instruct|pix2pix|sd3|stable-diffusion-3|flux|kolors|pixart|playground", repo, re.I):
            continue
        root = [s["rfilename"] for s in m.get("siblings", []) if "/" not in s["rfilename"] and s["rfilename"].endswith(".safetensors")
                and not re.search(r"lora|vae|ema-only-unet|inpaint|refiner|lcm|turbo|lightning", s["rfilename"], re.I)]
        if root:
            kj.append((repo, m, root))
    kj = kj[:900]
    with ThreadPoolExecutor(3) as ex:
        ktrees = dict(zip([j[0] for j in kj], ex.map(lambda j: _get(f"https://huggingface.co/api/models/{j[0]}/tree/main"), kj)))
    for repo, m, root in kj:
        sizes = {f["path"]: size_of(f) for f in (ktrees[repo]["data"] or []) if isinstance(f, dict) and f.get("type") == "file"}
        best = None
        tags = [t.lower() for t in m.get("tags", [])]
        xl = "stable-diffusion-xl" in tags or re.search(r"xl|pony|illustrious|noob", repo, re.I)
        for fn in root:
            s = sizes.get(fn, 0)
            ok = (5.0e9 < s < 7.6e9) if xl else (1.9e9 < s < 4.4e9)
            if ok and (best is None or ("fp16" in fn.lower()) > ("fp16" in best[0].lower())):
                best = (fn, s)
        if not best:
            continue
        tid = "sdxl" if xl else "sd15"
        if xl and re.search(r"illustrious|noob|[-_]il[-_v]", repo, re.I):
            tid = "noobai-xl-1.1"
        if xl and re.search(r"realvis|juggernaut|real|photo", repo, re.I):
            tid = "realvisxl-v5"
        if not xl and re.search(r"real|photo", repo, re.I):
            tid = "realistic-vision-v6"
        v = [dict(label="FP16" if "fp16" in best[0].lower() or best[1] < (7e9 if xl else 2.3e9) else "FP32", format="checkpoint",
                  repo=repo, path=best[0], size=best[1])]
        make(tid, repo, clean_name(repo), v, m.get("downloads", 0), m.get("tags", []))

    save_cache()
    print("  community recipes:", len(added), file=sys.stderr)
    return added
