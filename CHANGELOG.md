# Changelog

## 1.1.0 — 2026-10-25

Package version 1.1.0.0, the no push-to-talk version.

- Removed controller push-to-talk and its key helper: the helper's system-wide keyboard hook cost
  performance on handhelds. Mute from the widget's Mic button or Discord's own controls. No restricted
  capabilities remain.

## 1.0.1 (Beta 1) — 2026-10-22

First public build (package version 1.0.1.0; 1.0.0.0 was the internal pre-check of the same code). Everything below was built and verified on a ROG Xbox Ally X (Windows 11, Game Bar
compact/handheld UI).

- Discord's web app as a full-screen Game Bar widget, with Back and Home buttons on the widget's own strip.
- Voice chat keeps working with Game Bar closed: the widget holds Game Bar's microphone activity for the
  length of a call and keeps the mic capture open across mutes.
- Push-to-talk on any controller button mapped to Numpad `*`: tap to toggle, hold to talk. Keys are read by a small in-package
  helper so presses arrive whether Game Bar is open or not, and never reach the game.
- Touch keyboard works inside Game Bar (invisible text-box bridge).
- Three idle tiers when hidden and not in a call: suspend after 10 s, unload the page after 45 s, close the
  browser engine after 4 minutes; Game Bar opening pre-warms the page.
- Rebranded to Unofficial Discord for Gamebar; new mark.
- Development diagnostics removed from the shipping build (dev tools live in `tools/`).

### Pre-release history (0.1 – 0.9.12)
Internal iterations on the Ally covering the Game Bar proxy/stub registration, touch keyboard, full-screen
sizing, hidden-widget audio, the key helper and the Armoury Crate control-mode finding. Details in
`CLAUDE.md`.
