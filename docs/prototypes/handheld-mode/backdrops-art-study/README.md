# Stage backdrop art study 01

2026-11-07. First quality checkpoint: four original generated images for two starting consoles. This is not a finished set of sixteen or an owner-approved selection.

Open index.html directly, or serve this directory with `python3 -m http.server 8773 --bind 127.0.0.1`. The lab uses the existing Stage folder and plate CSS, original placeholder covers, folder/game/art-only modes, fullscreen, and per-direction localStorage notes and feedback export. It makes no external network requests.

## Art direction and assessment

- Saltwind Coast: NES exploration; strong framing and colour, but too much texture detail for authentic NES. Candidate for SNES if the owner prefers its richness.
- Cloud Observatory: NES exploration; attractive decorative architecture, but reads more like SNES/GBA. Proposed reassignment, not an approved pick.
- The Quiet Aqueduct: suggested PS2 lead. Strong atmospheric adventure identity, rich edges, quiet centre. More detailed than actual PS2 rendering.
- Last Lap of Summer: PS2 alternate, borrowing coastal arcade driving scenery. More modern lighting than period rendering.

Generated with the built-in image-generation tool. Exact prompts are in prompts.json. Original PNG outputs are preserved alongside this file. No third-party game artwork or real user covers are included. Images are 1672×941 (approximately 16:9); the lab fills the Stage with a negligible edge crop. These are stylistic concept assets, not palette-accurate native-resolution pixel art or captures from hardware.

## Proposed implementation after selection

1. Record the owner's chosen console assignments and IDs. Do not infer approval from the recommendations above. Expand only the accepted direction into the remaining systems.
2. Introduce a string BackdropId in KnownSystems and pass it through HandheldPlatform and HandheldBackdrop with value equality. Retain a fallback for unknown IDs.
3. Package chosen images as app resources. Use an image control filling the Stage; decode once, cache only the active and next assets, and measure actual decoded/GPU memory on the Thor. The lab reports PNG bytes separately from estimated RGBA memory; viewer surfaces and GPU copies are additional.
4. Keep one bounded crossfade on system change; changing games within the same system should not restart the scene. Cancel obsolete loads and transitions when switching rapidly.
5. Optional motion: author separate depth layers for the aqueduct, or a small water/cloud overlay for the coastal scenes. Use smooth transform animation if device testing supports it. Do not invent foreground/background separation by moving the entire painting as if it had depth. A layered asset pass is still needed.
6. Pause animation when hidden or backgrounded and respect reduced motion. The user's update makes frame-step and asset-size limits flexible; choose those from visual quality and measured battery/heat, not the old arbitrary limits.
7. Check unknown-ID fallback, cache disposal and cancellation, lifecycle pause/resume, and equality-controlled transitions. Validate on-device readability, temperature, memory and switching before broad integration.

## Validation

Browser-checked image loading, direction switching, Stage layout and the game overlay at the available narrow viewport. All four thumbnail images rendered. No app source changed. The full application build is not relevant to this standalone HTML study. Feedback persistence and fullscreen are implemented but have not been end-to-end verified.
