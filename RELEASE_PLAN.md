# Unofficial Discord for Gamebar — clean-up, re-optimization and release plan

Written 2026-10-21 after v0.9.12 (rebrand). Current state: voice, hidden voice, paddle push-to-talk
(via the in-package key helper), keyboard, full screen and the idle tiers all work on the ROG Xbox Ally X.
The build still carries the diagnostics that got us here. Two phases: make it a clean 1.0, then ship it.

## Phase 1 — clean-up and re-optimization (target: v1.0.0)

**Status 2026-10-22:** items 1, 4 and 6 done in 1.0.0 Beta 1. Item 2 measured on the dev PC: helper
private bytes 21 MB (CLR overhead); C++ port deferred to Beta 2. Items 3 and 5 (Ally measurements and
the full test matrix) are the beta's job: results go into README's table and CHANGELOG.

Goal: same behavior, nothing in the package that exists only for debugging, numbers re-measured on the Ally.

1. **Strip diagnostics from the shipping build** (keep them available for development).
   - Put `PowerLog` and every `PAGE`/`KEY`/`TARGET`/`WS`/`AUDIO` line behind a `DIAG` compile symbol. Release
     keeps only a tiny rolling log of state changes and crashes (the 64 KB `power.log` rollover already exists)
     so a user report still carries something useful.
   - Remove from the page scripts: the 15 s mic-track heartbeat, the once-a-minute DOM cue dump, the
     `diag:` posts. Keep the 5 s call-cue / mic-open poll (it is an input to the idle logic).
   - Remove the CDP `Network.enable` voice-socket tracking: it never fired on the Ally (Discord opens that
     socket from a worker) and it costs an event per network request.
   - Remove the standalone F8/F9 test keys and the Game Bar app-target logging.
   - Installer: stop bundling `Measure-DiscordBar.ps1`; move it, `Send-Log.ps1` and `log-relay-server.py`
     to `tools/` (dev only, documented in CLAUDE.md).
2. **Key helper footprint.** Measure private bytes (not working set) on the Ally. If it is above ~10 MB,
   port the helper to C++ (same hook + app service, ~1 MB, no CLR). The .NET build is 9 KB on disk but
   loads the framework.
3. **Re-measure on the Ally** with the dev Measure script, one row per state, and record them in README:
   shown (Discord visible), hidden in a call, hidden idle after 10 s / 45 s / 4 min, engine closed.
   Acceptance: engine-closed total ≤ 100 MB, hidden-in-call CPU ≤ 3 % on the Z2 Extreme, no growth over
   a 30 min call.
4. **Behavior review before freezing**: voice-gone grace 15 s, suspend 10 s, unload 45 s, close 4 min,
   hotkey watcher refresh 60 s, helper ping 30 s. Adjust only with a measurement behind it.
5. **Ally test matrix** (each pass/fail written down, same build):
   fresh install · upgrade over a running widget · sign in with QR · join voice · paddle tap and hold
   with Game Bar closed for 10 min · leave the call (mic badge gone, mic released) · idle 5 min with Game
   Bar closed (engine closes) · reopen (restored, signed in) · touch keyboard · full screen · Back/Home ·
   sleep and resume during a call · reboot (helper comes back with the widget).
6. **Version 1.0.0.0**, changelog in `CHANGELOG.md`, CLAUDE.md trimmed to the standing facts.

## Phase 2 — release

### A. GitHub (source, issues, sideload releases)
1. Create the public repo `unofficial-discord-for-gamebar` under your account. `.gitignore` already
   excludes `bin/ obj/ AppPackages/ *.pfx *.user .vs/`; the dev signing key must never be committed.
2. Add `LICENSE` (MIT), a README with screenshots, install steps for both channels, the paddle setup
   including the Armoury Crate control-mode finding, FAQ and troubleshooting, and a `PRIVACY.md`.
3. GitHub Actions (`windows-latest`): build the helper, then the solution, upload the `.msix` as an
   artifact on every push; releases attach the installer exe + msix.
4. Sideload builds need a real code-signing certificate or every user has to trust a test cert.
   Options: Azure Trusted Signing (about US$10/month, works with signtool) or SignPath (free for open
   source). Until then, the Store is the clean channel and the GitHub release is for people who can't
   use the Store.

### B. Microsoft Store
1. Partner Center individual developer account (one-time US$19). Reserve the name
   **Unofficial Discord for Gamebar**. Partner Center assigns the package identity (name + publisher);
   the manifest is updated to it, so existing sideload installs become a separate app and users
   reinstall once.
2. Build with `UapAppxPackageBuildMode=StoreUpload` (produces the `.msixupload`), run the Windows App
   Certification Kit locally and fix anything it flags.
3. Submission content: description (state plainly that it wraps discord.com and is not affiliated with
   Discord Inc.), 4–6 screenshots at 1920×1080 from the Ally, age rating questionnaire, privacy policy
   URL (required: microphone + internet), support contact.
4. Restricted capability `runFullTrust`: Partner Center asks for a justification. Text: the key helper
   is a background keyboard hook for push-to-talk because Game Bar's hotkey API does not deliver while
   Game Bar is closed; it reads only the configured push-to-talk keys and sends nothing off-device.
5. Notes to certification testers: how to sign in, that voice needs a second account, which key the
   paddle sends. Certification usually takes 1–3 business days.
6. Known risk: the app name and mark. If certification objects under the IP/branding policy, the plan is
   a second submission under a non-Discord name and the same mark; nothing else changes.

### C. Landing page (your website)
- One static page plus a privacy policy page, e.g. `discord.codex64.net` or `/gamebar-discord`, hosted
  on Cloudflare Pages or the existing droplet behind the tunnel.
- Sections: hero with the logo and one-line pitch · what it is (and is not) · features (voice with Game
  Bar closed, paddle push-to-talk, touch keyboard, full screen, idle memory tiers) · screenshots/GIF ·
  Download: Store badge + GitHub release · requirements (Windows 11, Game Bar, WebView2 runtime) ·
  paddle setup guide · FAQ · privacy · "Not affiliated with Discord Inc."
- I can build it from the repo assets once the Store link exists.

### Order
Phase 1 → measure → 1.0 build → GitHub repo and first release → Store submission → landing page goes
live with the Store link (can be built while the Store reviews).
