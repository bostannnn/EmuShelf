---
description: Work the next unchecked milestone in ROADMAP.md end to end
---

Work one EmuShelf milestone this session.

Target: $ARGUMENTS — if empty, open ROADMAP.md and take the first milestone not marked ✅. That milestone, and only that one, is the job.

Setup, before writing any code:

1. Run `git log --oneline -3` and `git status`. If the previous milestone left uncommitted work: verify `dotnet build` and `dotnet test` are green, then commit it as its own "M<N-1>: …" commit before touching anything else.
2. Read DECISIONS.md, and the design-doc sections for the target milestone in docs/design-document.pdf: M3 → §5–6 and §11 · M4 → §6 · M5 → §6 (PS3 parts) · M6 → §8 · M7 → §7 · M8 → §11 and §14.

Rules of engagement:

- Implement the milestone's checklist top to bottom; stay inside its scope. If something seems to need later-milestone work, define an interface in Core and stub it rather than building ahead.
- When you hit a choice the docs don't answer: pick the simplest option consistent with DECISIONS.md, record it there, and keep going. Ask the user only if the choice changes user-visible product behavior.

Done means, in order:

1. Solution builds with zero warnings; `dotnet test` passes.
2. Launch the app on macOS and exercise the new behavior end to end — verify it works, not just that it compiles.
3. Tick the checklist items in ROADMAP.md and mark the milestone heading ✅ with today's date.
4. Append non-obvious choices to DECISIONS.md.
5. Run the code-review skill at medium effort on the working-tree diff and fix confirmed correctness findings.
6. Make exactly one commit, message starting "M<N>: ". Never end the session with the milestone uncommitted.
