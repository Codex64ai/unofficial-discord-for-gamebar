# Architecture

Two moving parts, one state machine.

```
┌──────────────────────── Xbox Game Bar ────────────────────────┐
│  widget window (XboxGameBarWidget)                    activity │
└───────────┬───────────────────────────────────────────────┬────┘
            │ hosts                                         │ mic activity (keeps audio alive while hidden)
┌───────────▼───────────────────────────────────────────────────┐
│  DiscordBar.exe  (UWP, WinUI 2)        MainPage.xaml.cs       │
│   ├─ TopBar: Back / Home / Mic                                 │
│   ├─ WebView2 ──► discord.com/app  (+ two injected scripts)    │
│   └─ KeyBox: invisible TextBox = touch-keyboard bridge         │
└───────────────────────────────────────────────────────────────┘
```

## The widget (`DiscordBar/`)

`App.xaml.cs` creates the `XboxGameBarWidget` on Game Bar activation (`ms-gamebarwidget` protocol) or a
plain window on a Start-menu launch, releases the voice activity on suspend, and writes crashes to the
log.

`MainPage.xaml.cs` owns everything else:

- **Browser setup.** WebView2 with `--renderer-process-limit=1 --process-per-site`, SmartScreen,
  background networking, component updates and pings off; autofill off; DevTools off in release;
  reduced motion pushed through CDP `Emulation.setEmulatedMedia`; microphone permission granted only to
  discord.com; notifications denied; navigation restricted to Discord's hosts, everything else goes to
  the default browser.
- **Injected scripts.** `MicWatchScript` wraps `getUserMedia` so one mic capture lives for the whole call
  (Discord's `track.stop()` becomes "disable"; the next audio-only `getUserMedia` returns the same tracks;
  the real stop happens on `__dbReleaseMic` or after 30 min of silence with no call cue). It also posts
  the in-call signals: `mic:1/0` (live tracks), `micopen:1/0` (capture held), `call:1/0` (Discord's
  Disconnect button or "Voice Connected" text, polled every 5 s), `rtc:1/0` (wrapped RTCPeerConnection;
  Discord usually gets a pristine constructor, so this rarely fires). `KeyboardWatchScript` reports
  focus on editable elements (`kb:1/0`), exposes `__dbInsert/__dbBackspace/__dbEnter/__dbBlur` for the
  keyboard bridge, exposes `__dbSetMicState` (set Discord's mute toggle, verified by polling the button
  state every 50 ms), and pauses `<video>` elements while hidden.
- **State machine (`ApplyState`).** Inputs: `IsShown` (widget visible and not minimized), `InCall`
  (any of the page signals), `IsDocumentPlayingAudio`. Shown: resume/reload if needed, normal memory
  target (low when pinned over a game). Hidden in a call: page kept "visible" to the engine, low memory
  target, no timers. Hidden and idle: page collapsed, suspend after 10 s (`TrySuspendAsync`), unload
  after 45 s (`about:blank` then suspend), close the engine after 4 min (`Web.Close()`, control
  recreated on show). Game Bar entering the foreground pre-warms (recreate/reload).
- **Voice keep-alive.** While `InCall`, hold `XboxGameBarWidgetActivity(widget,
  GetMicrophoneInputActivityId())`. Game Bar then keeps the widget alive and Windows allows audio while
  hidden; the on-screen mic badge is the indicator. Release after 15 s of "no call" (Discord reconnects
  drop and re-create its connection). A failed start (Game Bar refuses while hidden, or still holds the
  previous process's activity after an upgrade) is retried every 10 s and on show. A 20 s keep-alive tick
  re-requests when Windows reports our capture as muted. Suspending/shutdown releases the activity so an
  upgrade doesn't leave Game Bar holding it.
- **Mic button.** The TopBar's Mic button reads Discord's real mute state from its toggle, flips it
  through `__dbSetMicState` with one click, and updates its icon; the icon is also refreshed on show.
  There is no controller push-to-talk (removed in 1.1.0.0, see CLAUDE.md).
- **Touch keyboard bridge.** On `kb:1`, wait 500 ms; if no keyboard appeared, focus the invisible
  `KeyBox` and ask for the gamepad keyboard view. Text typed there is relayed into the page (whole
  compositions at once, so suggestions don't double characters); Backspace, Enter and Escape are
  forwarded as actions. The package manifest's Game Bar proxy/stub registration is required for any of
  this: without it Game Bar's input/focus IPC is dead and no control can summon a keyboard.

## Package manifest essentials

`mp:PhoneIdentity`; `Windows.Universal` + `Windows.Desktop` device families; capabilities
`internetClient`, `backgroundMediaPlayback`, `microphone` (no restricted capabilities); the Game Bar
`microsoft.gameBarUIExtension` app extension (home-menu visible, pinning, favorite after install,
compact-mode priority placement, 1280×720 default, min 480×360, max 8000×8000); and the package-level
`windows.activatableClass.proxyStub` block
with the private Game Bar interfaces (the SDK requires it; NuGet doesn't add it; everything input-related
breaks without it). Game Bar home-bar icons live in `GameBar\Icons` as monochrome `icon.targetsize-*.png`
(white) and `icon.light.targetsize-*.png` (black).

## Known constraints

- Discord blocks voice from mobile browsers: the desktop layout is unavoidable.
- The microphone badge is Game Bar's and cannot be suppressed by the widget.
- A background UWP app cannot start a microphone capture; hence the single long-lived capture per call.
- No controller push-to-talk: Game Bar's hotkey watcher stops delivering once Game Bar dozes, and the
  full-trust keyboard-hook helper that worked around it cost performance on handhelds, so both were
  removed in 1.1.0.0.
