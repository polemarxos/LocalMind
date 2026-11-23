"""Regenerates src/LocalMind/Resources/catalog.json, the offline copy of the model catalog
compiled into LocalMind.exe. The app refreshes the list live from Hugging Face every 24 h;
run this before a release so first-run/offline users get a current list:

    python tools/build_catalog.py
"""
import json, os, urllib.request, re, sys
NON_CHAT_ARCH={"flux","qwen_image","lumina2","wan","ltxv","hyvid","moss_image","gemma-embedding","bert","nomic-bert","sd3","chroma","hidream","t5","t5encoder","clip"}
NON_CHAT_PIPE={"text-to-image","image-to-image","text-to-video","image-to-video","image-text-to-video","feature-extraction","sentence-similarity","text-to-speech","automatic-speech-recognition","text-to-audio"}
PREF={"unsloth":0,"bartowski":1,"lmstudio-community":2,"ggml-org":3}
raw=[]
for author in PREF:
    url=f"https://huggingface.co/api/models?author={author}&filter=gguf&sort=downloads&limit=300&expand[]=gguf&expand[]=downloads&expand[]=pipeline_tag&expand[]=tags&expand[]=likes&expand[]=lastModified"
    raw+=json.load(urllib.request.urlopen(url,timeout=90))
def norm(repo):
    n=repo.split("/")[1]
    n=re.sub(r"-GGUF$","",n,flags=re.I)
    if repo.startswith("bartowski/") and "_" in n: n=n.split("_",1)[1]
    return n
best={}
for m in raw:
    g=m.get("gguf") or {}
    tags=m.get("tags",[]); pipe=m.get("pipeline_tag") or ""
    arch=(g.get("architecture") or "").lower(); p=g.get("total")
    rid=m["id"]; low=rid.lower()
    if not p or p<100_000_000: continue
    if arch in NON_CHAT_ARCH or pipe in NON_CHAT_PIPE: continue
    if any(x in low for x in ["test-","models-moved","embed","reranker","-mtp-gguf","tts","whisper","imatrix"]): continue
    name=norm(rid); key=name.lower()
    e=dict(repo=rid,name=name,author=rid.split("/")[0],params=p,arch=arch,ctx=g.get("context_length") or 0,
           dl=m.get("downloads",0),likes=m.get("likes",0),pipe=pipe,
           lic=next((t[8:] for t in tags if t.startswith("license:")),""),
           upd=(m.get("lastModified") or "")[:10],
           tags=[t for t in tags if t in ("moe","conversational","image-text-to-text","any-to-any","reasoning","code","uncensored","abliterated")])
    cur=best.get(key)
    if cur is None or (e["dl"]>cur["dl"]*3) or (PREF[e["author"]]<PREF[cur["author"]] and cur["dl"]<e["dl"]*3):
        if cur: e["dl"]=max(e["dl"],cur["dl"])
        best[key]=e
    else: cur["dl"]=max(cur["dl"],e["dl"])
out=sorted(best.values(),key=lambda e:-e["dl"])
dest=os.path.join(os.path.dirname(os.path.abspath(__file__)),"..","src","LocalMind","Resources","catalog.json")
json.dump(out,open(dest,"w"),separators=(",",":"))
print(len(raw),"raw ->",len(out),"entries", file=sys.stderr)
