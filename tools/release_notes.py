"""Short release notes for one version: one plain line per change, then the download link.

    python tools/release_notes.py v2.26.36 > notes.md

Each change in CHANGELOG.md starts with a bold title ("- **Buttons under every picture.** details…"); only the title
is used here. The full text stays in CHANGELOG.md, linked at the end. The lines are kept plain (no bold, no links),
because LocalMind's update window shows them as they are.
"""
import re, sys

ver = sys.argv[1].lstrip('v')
repo = "polemarxos/LocalMind"
log = open('CHANGELOG.md', encoding='utf-8').read()
m = re.search(r'^## ' + re.escape(ver) + r'\b[^\n]*\n(.*?)(?=^## |\Z)', log, re.S | re.M)
body = m.group(1) if m else ''

KIND = {'added': 'New', 'changed': 'Changed', 'fixed': 'Fixed', 'removed': 'Removed'}


def plain(t):
    t = re.sub(r'\[([^\]]*)\]\([^)]*\)', r'\1', t)        # links → their text
    t = t.replace('**', '').replace('`', '')
    return re.sub(r'\s+', ' ', t).strip()


def title(item):
    b = re.match(r'\*\*(.+?)\*\*', item)
    if b:
        t = plain(b.group(1))
    else:
        t = plain(item)
        t = re.split(r'(?<=[.!?])\s', t)[0]               # the first sentence
    t = t.rstrip('.').strip()
    return t if len(t) <= 110 else t[:107].rstrip() + '…'


lines, kind = [], 'Changed'
for line in body.splitlines():
    h = re.match(r'^###\s+(\w+)', line)
    if h:
        kind = KIND.get(h.group(1).lower(), h.group(1))
        continue
    if line.startswith('- '):                            # top-level changes only
        lines.append(f"- {kind}: {title(line[2:])}")

url = f"https://github.com/{repo}/releases/download/v{ver}/LocalMind-win-x64.zip"
out = "\n".join(lines) if lines else "- Small fixes and improvements"
print(f"""{out}

Download: [LocalMind-win-x64.zip]({url}) (unzip and run LocalMind.exe)
All details: [CHANGELOG](https://github.com/{repo}/blob/main/CHANGELOG.md)
""")
