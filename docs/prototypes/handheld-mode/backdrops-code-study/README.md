# Code-built console backdrop study

Three original code-built scenes: NES Rooftop garden, PlayStation Seaside plaza, and PlayStation 2 Sunlit reservoir. This is an owner-review checkpoint, not an approved replacement for the app's era motif. It follows the rejected rounds A/B and generated-image study, preserving those files as history.

- [index.html](index.html): standalone review lab. Open directly, or serve this directory on port 8774.
- [study.html](study.html): self-contained interactive fragment; the editable source for the scenes and preview. No dependencies or external requests.
- [IDEAS.md](IDEAS.md): ideas for all sixteen consoles, assessment of these three proofs, and an integration plan.
- [build_lab.py](build_lab.py): dependency-free wrapper builder. Run it after editing the fragment or ideas table.

```sh
python3 docs/prototypes/handheld-mode/backdrops-code-study/build_lab.py
python3 -m http.server 8774 --bind 127.0.0.1 --directory docs/prototypes/handheld-mode/backdrops-code-study
```

Then open <http://127.0.0.1:8774>.

Select a console. Try Folder, Game and Backdrop only. Show construction exposes the tile atlas/grid or projected triangle meshes. Optional motion swaps two pre-drawn water overlays once a second; it is off by default and stops while hidden, offscreen or under reduced motion. The construction toggle is diagnostic, not another art direction.

Keep/Maybe/Not this and notes are saved per direction in a separate localStorage key. Copy all feedback includes notes. No picks from previous labs are changed. The inline preview in chat remembers its selected console and display settings through the host when available; the verdict fields belong to the standalone lab.

The scene authoring renderer runs once per selected console and caches its result. It is intentionally **not** a proposed runtime renderer for Avalonia. For production, export the approved base/overlays and display those as image layers. The cost readout includes base + two overlay RGBA8 buffers only. Browser surfaces, construction views, texture cache, GPU copies and crossfades add memory. See IDEAS.md for details.

All tile patterns, meshes, textures and placeholder covers are authored in code in this study. No image-generation model, third-party assets, owner covers, fonts, frameworks or game artwork are used. No changes under `src/`; no application build required for this documentation-only prototype.

## Review status

Not owner-approved and not tested on the Thor.

Checked in headless Chrome on macOS:

- All three consoles in folder, game and backdrop-only modes; construction views differ and toggle back to the same baked art.
- Visual inspection of all three scenes, their folder overlays, the refined mesh renderings, dark page styling and the inline preview.
- Per-direction feedback, reload persistence, feedback export with real line breaks, and the local-file entrypoint.
- Motion changes frame opacity while the base bitmap remains identical. Reduced motion and a simulated document-hidden lifecycle signal pause the frame clock.
- Exactly 15 colours in the native NES scene. No page overflow at 320, 736 and 1500 CSS pixels, including the expanded ideas table.
- No JavaScript errors or external network requests in the standalone review flow.

These checks establish that the lab works. They do not establish console recognition, visual approval, on-device thermal cost, or real Android lifecycle behavior.
