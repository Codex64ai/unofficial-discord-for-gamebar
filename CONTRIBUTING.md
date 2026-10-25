# Contributing

Issues and pull requests are welcome. A few things that keep this project what it is:

- **Light first.** Every change is judged on what it costs while hidden on a handheld. If it adds a
  timer, a poll or a process, say what it costs and why it's worth it.
- **No Discord internals.** The app wraps the official web app like a browser. Nothing that touches
  Discord's API, token or client code will be merged; it would put users' accounts at risk.
- **Keep the desktop user agent.** Discord blocks voice from mobile browsers.
- **Verify on a device.** Game Bar behaves differently from a normal window (focus, keyboard, hotkeys,
  idle shutdown). A change to any of those needs a run on real Game Bar, ideally on a handheld.
- Read `CLAUDE.md` before changing the voice or keyboard paths, or before proposing push-to-talk again
  (it was removed in 1.1.0.0 for performance): it records what was tried
  and why the current design is the way it is.

Build instructions are in the README.
