"""Regenerates src/LocalMind/Resources/catalog.json, the offline copy of the model catalog
compiled into LocalMind.exe. The app refreshes the list live from Hugging Face every 24 h
(same sources and rules, see ModelCatalog.vb); run this before a release so first-run and
offline users get a current list:

    python tools/build_catalog.py
"""
import json, os, re, sys, time, urllib.request
from concurrent.futures import ThreadPoolExecutor

# Quantizers and labs, best first. When the same model is uploaded by several of them,
# the earlier one wins (unless a later one is far more popular).
# (author, pages of 1000, minimum downloads)
SOURCES = [
    ("unsloth", 1, 0), ("bartowski", 3, 0), ("lmstudio-community", 1, 0), ("ggml-org", 1, 0),
    ("Qwen", 1, 0), ("google", 1, 0), ("mistralai", 1, 0), ("microsoft", 1, 0), ("ibm-granite", 1, 0),
    ("LiquidAI", 1, 0), ("nvidia", 1, 0), ("NousResearch", 1, 0), ("openbmb", 1, 0), ("allenai", 1, 0),
    ("tiiuae", 1, 0), ("internlm", 1, 0), ("HuggingFaceTB", 1, 0), ("zai-org", 1, 0), ("janhq", 1, 0),
    ("Menlo", 1, 0), ("huihui-ai", 1, 0), ("mlabonne", 1, 0),
    ("MaziyarPanahi", 1, 100), ("QuantFactory", 1, 100), ("mradermacher", 3, 100), ("second-state", 1, 100),
    ("Mungert", 1, 100), ("TheBloke", 1, 100), ("DavidAU", 1, 100), ("TheDrummer", 1, 50), ("Lewdiculous", 1, 100),
    ("gabriellarson", 1, 100), ("prithivMLmods", 1, 100), ("DevQuasar", 1, 100), ("tensorblock", 1, 100),
    ("RichardErkhov", 2, 100),
]
PREF = {a.lower(): i for i, (a, _, _) in enumerate(SOURCES)}
MIN_DL = {a.lower(): m for a, _, m in SOURCES}

NON_CHAT_ARCH = {"flux", "qwen_image", "lumina2", "wan", "ltxv", "hyvid", "moss_image", "gemma-embedding", "bert",
                 "nomic-bert", "sd3", "chroma", "hidream", "t5", "t5encoder", "clip", "jina-bert-v2", "xlm-roberta"}
NON_CHAT_PIPE = {"text-to-image", "image-to-image", "text-to-video", "image-to-video", "image-text-to-video",
                 "feature-extraction", "sentence-similarity", "text-to-speech", "automatic-speech-recognition", "text-to-audio"}
SKIP = ["test-", "models-moved", "embed", "reranker", "-mtp-gguf", "-mtp", "tts", "whisper", "imatrix-dataset", "stable-diffusion", "flux", "qwen-image", "sdxl"]
EXP = "&expand[]=gguf&expand[]=downloads&expand[]=pipeline_tag&expand[]=tags&expand[]=likes&expand[]=lastModified"


def fetch(source):
    author, pages, _ = source
    url = f"https://huggingface.co/api/models?author={author}&filter=gguf&sort=downloads&limit=1000{EXP}"
    out = []
    for _ in range(pages):
        r = None
        for attempt in range(6):          # Hugging Face rate-limits bursts (HTTP 429): wait and retry
            try:
                r = urllib.request.urlopen(url, timeout=120)
                break
            except Exception as e:
                print("  ! retry", author, e, file=sys.stderr)
                time.sleep(15 * (attempt + 1))
        if r is None:
            break
        out += json.load(r)
        m = re.search(r'<([^>]+)>;\s*rel="next"', r.headers.get("Link", "") or "")
        if not m:
            break
        url = m.group(1)
    return out


def norm(repo):
    author, n = repo.split("/", 1)
    a = author.lower()
    n = re.sub(r"(-GGUF-IQ-Imatrix|-GGUF|_GGUF|-gguf)$", "", n, flags=re.I)
    n = re.sub(r"-i1$", "", n)
    if a == "richarderkhov" and "_-_" in n:
        n = n.split("_-_", 1)[1]
    elif a in ("bartowski", "tensorblock") and "_" in n:
        n = n.split("_", 1)[1]
    elif a == "devquasar" and "." in n:
        n = n.split(".", 1)[1]
    return n


with ThreadPoolExecutor(3) as ex:
    raw = [m for batch in ex.map(fetch, SOURCES) for m in batch]

best = {}
for m in raw:
    g = m.get("gguf") or {}
    tags = m.get("tags", [])
    pipe = m.get("pipeline_tag") or ""
    arch = (g.get("architecture") or "").lower()
    p = g.get("total")
    rid = m["id"]
    low = rid.lower()
    author = rid.split("/")[0]
    if not p or p < 100_000_000: continue
    if arch in NON_CHAT_ARCH or pipe in NON_CHAT_PIPE: continue
    if any(x in low for x in SKIP): continue
    if m.get("downloads", 0) < MIN_DL.get(author.lower(), 100): continue
    name = norm(rid)
    key = name.lower()
    e = dict(repo=rid, name=name, author=author, params=p, arch=arch, ctx=g.get("context_length") or 0,
             dl=m.get("downloads", 0), likes=m.get("likes", 0), pipe=pipe,
             lic=next((t[8:] for t in tags if t.startswith("license:")), ""),
             upd=(m.get("lastModified") or "")[:10],
             tags=[t for t in tags if t in ("moe", "conversational", "image-text-to-text", "any-to-any", "reasoning", "code", "uncensored", "abliterated")])
    cur = best.get(key)
    pe, pc = PREF.get(author.lower(), 99), PREF.get(cur["author"].lower(), 99) if cur else 99
    if cur is None or e["dl"] > cur["dl"] * 3 or (pe < pc and cur["dl"] < e["dl"] * 3):
        if cur: e["dl"] = max(e["dl"], cur["dl"])
        best[key] = e
    else:
        cur["dl"] = max(cur["dl"], e["dl"])

out = sorted(best.values(), key=lambda e: -e["dl"])
dest = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "LocalMind", "Resources", "catalog.json")
json.dump(out, open(dest, "w"), separators=(",", ":"))
print(len(raw), "raw ->", len(out), "entries", file=sys.stderr)
