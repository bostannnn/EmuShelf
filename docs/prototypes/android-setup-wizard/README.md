# Android setup wizard — A/B prototype (2026-09-05)

Open `index.html`. Space / click flips **Current** (the real Thor screenshot of today's onboarding card,
or the Settings page where that decision lives today) ↔ **Proposed**; ←/→ switch steps.
`index.html?s=N&bare=1` renders one proposed screen bare at 1920×1080 (headless Chrome).

Proposed: one page template for every step — the Settings panel, rail, rows, toggles and legend from the
approved round-4 language. The rail is the progress indicator (done steps show their outcome, steps that
need the booted app are dimmed during phase A). START continues, Y skips an optional step, B goes back.

Phase A (before the app can boot): Storage access, Data folder (with "continue with your existing data"
when a library is found — the pointer mirror from PR #231 makes that possible after a reinstall).
Phase B (inside the composed app, reusing the Settings logic): Second screen (Thor only), Emulator
return (Shizuku), Game folders, Emulators (only systems with games; missing emulator caught up front),
Saves (Drive + per-system save folders). Completion is versioned so a later new step shows once alone.

Assets: `assets/shelf-bg.jpg` + `assets/icons/*` copied from the couch-settings prototypes; the font is
the repo's bundled Exo 2. `current/onboarding.jpg` is the Thor at 1920×1080 with the all-files grant
revoked (the way to make today's card appear on a completed install).
