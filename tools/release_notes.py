"""Release notes for one version: a download link first, then only that version's CHANGELOG section.

    python tools/release_notes.py v2.6.0 > notes.md
"""
import re,sys
ver=sys.argv[1].lstrip('v'); repo="polemarxos/LocalMind"
log=open('CHANGELOG.md',encoding='utf-8').read()
m=re.search(r'^## '+re.escape(ver)+r'\b[^\n]*\n(.*?)(?=^## |\Z)', log, re.S|re.M)
body=m.group(1).strip() if m else ''
url=f"https://github.com/{repo}/releases/download/v{ver}/LocalMind-win-x64.zip"
print(f"""## ⬇ Download

**[LocalMind-win-x64.zip]({url})** — unzip and run `LocalMind.exe` (Windows 10/11, 64-bit, nothing to install).

---

{body}
""")
