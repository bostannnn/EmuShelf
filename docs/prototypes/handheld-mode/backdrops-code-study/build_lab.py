"""Wrap the inline, dependency-free study as a standalone review lab."""

from html import escape
from pathlib import Path

ROOT = Path(__file__).resolve().parent


def ideas_table():
    rows = []
    in_table = False
    for line in (ROOT / "IDEAS.md").read_text().splitlines():
        if line.startswith("| System |"):
            in_table = True
        if in_table and not line.startswith("|"):
            break
        if not in_table or line.startswith("|---"):
            continue
        cells = [escape(cell.strip()).replace("**", "") for cell in line.strip("|").split("|")]
        tag = "th" if not rows else "td"
        rows.append("<tr>" + "".join(f"<{tag}>{cell}</{tag}>" for cell in cells) + "</tr>")
    return '<div class="idea-table"><table><thead>' + rows[0] + '</thead><tbody>' + "".join(rows[1:]) + '</tbody></table></div>'


head = """<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>EmuShelf · Code-built console backdrops</title>
<style>
:root{color-scheme:light dark;--page:light-dark(#f2f0e9,#18212a);--ink:light-dark(#25323b,#e8eeee);--muted:light-dark(#58666f,#abb8c0);--line:light-dark(#cbd1cd,#3b4953)}
*{box-sizing:border-box}body{margin:0;padding:32px;background:var(--page);font:15px/1.55 system-ui;color:var(--ink)}main{max-width:1540px;margin:auto}header{display:flex;gap:40px;align-items:end;justify-content:space-between;margin-bottom:22px}.eyebrow{font-size:11px;letter-spacing:.14em;text-transform:uppercase;color:var(--muted);margin:0 0 8px}h1{font-size:36px;line-height:1.1;margin:0;letter-spacing:-1.3px;max-width:700px}header p{max-width:540px;color:var(--muted);margin:0}h2{font-size:24px;line-height:1.2;letter-spacing:-.4px;margin:0 0 12px}h3{font-size:13px;margin:20px 0 4px}p{margin:0 0 12px}a{color:inherit;text-underline-offset:3px}.review{display:grid;grid-template-columns:1.15fr 1fr;gap:60px;border-top:1px solid var(--line);margin-top:28px;padding-top:28px}.detail{max-width:75ch;color:var(--muted)}.detail strong{color:var(--ink)}.feedback-controls{display:flex;flex-wrap:wrap;gap:8px;margin-bottom:12px}.review button{font:inherit;border:1px solid var(--line);border-radius:8px;background:light-dark(#fffefa,#25303a);color:var(--ink);padding:8px 14px;min-height:40px;cursor:pointer}.review button[aria-pressed=true]{background:light-dark(#293c46,#d6e5ed);color:light-dark(#fff,#18232c)}button:focus-visible,textarea:focus-visible,summary:focus-visible{outline:3px solid #b47a4f;outline-offset:3px}textarea{width:100%;min-height:110px;resize:vertical;font:inherit;color:var(--ink);background:light-dark(#fffefa,#25303a);border:1px solid var(--line);border-radius:8px;padding:12px}label{display:block;font-size:13px;margin-bottom:6px}.export{margin-top:12px}.small{font-size:13px;color:var(--muted)}details{margin-top:32px;border-top:1px solid var(--line);padding-top:20px}summary{font-size:20px;font-weight:600;cursor:pointer;margin-bottom:16px}.idea-table{overflow-x:auto}table{border-collapse:collapse;width:100%;font-size:14px}th,td{padding:12px 15px 12px 0;border-bottom:1px solid var(--line);text-align:left;vertical-align:top}th{font-size:12px;color:var(--muted)}td:first-child{font-weight:600;min-width:100px}td:nth-child(2){min-width:220px}td:nth-child(3),td:nth-child(4){min-width:250px}footer{font-size:12px;color:var(--muted);margin:24px 0}
@media(max-width:850px){header{display:block}header p{margin-top:18px}h1{font-size:30px}.review{grid-template-columns:1fr;gap:30px}body{padding:18px}}@media(max-width:400px){body{padding:12px}h1{font-size:27px}}
</style></head><body><main><header><div><p class="eyebrow">EmuShelf / Console backdrop study / Built in code</p><h1>Built like a game.<br>Composed for a library.</h1></div><p>Choose a rendering language first, then build a place around the folder. Three working directions to judge; thirteen more ideas below. Original tiles, meshes and textures throughout.</p></header>
"""

review = """
<section class="review" aria-label="Direction and feedback">
  <div><p class="eyebrow">The idea behind this scene</p><h2 id="direction-title"></h2><p id="direction-idea" class="detail"></p><h3>How it is built</h3><p id="direction-build" class="detail"></p><h3>What to judge</h3><p id="direction-judge" class="detail"></p><p class="small">This is a design proposal. The application still uses its existing backdrop.</p></div>
  <div><h2>Your take on this direction</h2><div class="feedback-controls" aria-label="Verdict"><button type="button" data-verdict="keep" aria-pressed="false">Keep</button><button type="button" data-verdict="maybe" aria-pressed="false">Maybe</button><button type="button" data-verdict="no" aria-pressed="false">Not this</button></div><label for="direction-note">What works, and what should change?</label><textarea id="direction-note" placeholder="Notes for this direction…"></textarea><div class="feedback-controls export"><button type="button" id="copy-feedback">Copy all feedback</button><span id="save-status" class="small" role="status">Feedback stays in this browser.</span></div><textarea id="feedback-export" aria-label="Feedback to copy" readonly hidden></textarea></div>
</section>
<details><summary>Ideas for all sixteen systems</summary><p class="detail">Only NES, PlayStation and PlayStation 2 have working proofs here. Every other row is a proposal to choose and develop, not a finished backdrop.</p>TABLE<p style="margin-top:20px"><a href="IDEAS.md">Full art-direction notes and implementation plan</a></p></details>
<footer>Scene rendering happens once per console and is cached. Optional motion only swaps pre-drawn water frames. The readout estimates three RGBA asset buffers; it excludes construction views, browser/GPU copies and transition surfaces. No third-party or generated game artwork is loaded. Not yet tested on the Thor.</footer>
<script>
(() => {
  const root=document.getElementById('es-code-study'), api=root.backdropStudy, key='emushelf-code-backdrops-01';
  const get=id=>document.getElementById(id), notes=get('direction-note');let picks={};
  try { const value=JSON.parse(localStorage.getItem(key)||'{}');if(value&&typeof value==='object'&&!Array.isArray(value))picks=value; } catch {}
  const directions={
    nes:{idea:'A canal town in compact adventure-game tiles: flower boxes, inset windows, terracotta roofs and a rooftop greenhouse. The buildings frame a quiet opening of sky.',build:'Nine hand-authored 8 × 8 tile patterns and a fixed 15-colour palette. The scene is baked at 240 × 135 and scaled eight times. Show construction reveals the grid and tile atlas.',judge:'Do the tile clusters and silhouettes read as 8-bit? Is the muted palette too gentle for NES? This is an original interpretation, not hardware-accurate NES output.'},
    ps1:{idea:'An open coastal piazza, with low-poly buildings, a striped canopy, a balcony, angular trees and steps toward the sea. Texture and geometry carry the console identity.',build:'3D meshes projected to depth-buffered triangles, affine-mapped original textures, 5-bit colour channels and ordered dithering. Rendered once at 320 × 180. Show construction reveals the mesh.',judge:'Does it feel like early textured 3D rather than a generic low-poly illustration? Does the scene still frame the case when you switch to Game?'},
    ps2:{idea:'A flooded reservoir beneath a monumental stone arch. Distant architecture and reflections create space; the foreground arch frames the folder.',build:'An extruded arch of wedge-shaped stones, modelled piers and steps, baked face lighting and distance haze. Reflections use an inverted mesh pass. Baked at 960 × 540; no live lighting or blur.',judge:'Does the scale and lighting evoke the generation? The material detail is still a prototype. The arch should support the folder without becoming the main attraction.'}
  };
  function update(){const id=api.state.system,info=api.info[id],d=directions[id],pick=picks[id]||{};get('direction-title').textContent=info.short+' · '+info.title;get('direction-idea').textContent=d.idea;get('direction-build').textContent=d.build;get('direction-judge').textContent=d.judge;notes.value=typeof pick.note==='string'?pick.note:'';document.querySelectorAll('[data-verdict]').forEach(b=>b.setAttribute('aria-pressed',String(pick.verdict===b.dataset.verdict)));}
  function save(){try{localStorage.setItem(key,JSON.stringify(picks));get('save-status').textContent='Saved in this browser.';}catch{get('save-status').textContent='Storage unavailable; copy feedback before closing.';}}
  document.querySelectorAll('[data-verdict]').forEach(b=>b.onclick=()=>{const id=api.state.system;picks[id]={...picks[id],verdict:b.dataset.verdict};save();update();});
  notes.oninput=()=>{const id=api.state.system;picks[id]={...picks[id],note:notes.value};save();};
  get('copy-feedback').onclick=async()=>{const text=Object.entries(api.info).map(([id,i])=>`${i.name} — ${i.title}: ${picks[id]?.verdict||'Not judged'}${picks[id]?.note?'\\n'+picks[id].note:''}`).join('\\n\\n');get('feedback-export').value=text;get('feedback-export').hidden=false;try{await navigator.clipboard.writeText(text);get('save-status').textContent='All feedback copied.';}catch{get('save-status').textContent='Select and copy the feedback below.';}};
  root.addEventListener('studychange',update);update();
})();
</script></main></body></html>
"""

output = head + (ROOT / "study.html").read_text() + review.replace("TABLE", ideas_table())
(ROOT / "index.html").write_text(output)
print(f"Built {ROOT / 'index.html'} ({len(output):,} characters)")
