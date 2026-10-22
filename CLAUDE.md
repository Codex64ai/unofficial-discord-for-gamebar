# DiscordBar — notes for Claude Code

Xbox Game Bar widget that hosts Discord's official web app (discord.com/app) in WebView2.
Target: ROG Xbox Ally X (Windows 11, x64). Build machine: this Windows PC.

## Hard rules
- Top priority is low CPU/RAM while a game runs. Every change should keep or cut usage.
- Wrap the official web app only. No custom Discord client, no user-token API calls (breaks Discord ToS).
- x64 only. Keep it UWP (Game Bar widgets must be UWP).

## Layout
- `DiscordBar/App.xaml.cs` — Game Bar activation (`ms-gamebarwidget` protocol), creates `XboxGameBarWidget`.
- `DiscordBar/MainPage.xaml.cs` — WebView2 setup and the power logic:
  shown → normal; hidden → collapsed + low memory; hidden and not in voice for 30 s → `TrySuspendAsync`.
  Voice detection = injected script wrapping `getUserMedia` + `IsDocumentPlayingAudio`.
- `DiscordBar/Package.appxmanifest` — widget registration (`microsoft.gameBarUIExtension`), mic capability.
- `Assets/*.png` are plain purple placeholders.

## Status
v0.1 builds clean (Release|x64, 0 warnings) as of 2026-10-20 on the Windows PC. All Game Bar / WebView2 API names
were verified against the restored packages; no source changes were needed. Build fixes that were needed:
- `Package.appxmanifest`: added `mp:PhoneIdentity` (packager error APPX1673 without it).
- `.csproj`: pinned Microsoft.Gaming.XboxGameBar 7.3.2607010 and Microsoft.UI.Xaml 2.8.7; signing uses
  `DiscordBar_TemporaryKey.pfx` (self-signed CN=DiscordBarDev, no password, thumbprint 40F9F516...E158); Prefer32Bit off.
- Installing on a PC that already has the frameworks: run `Add-AppxPackage` on the .msix WITHOUT `-DependencyPath`
  (passing it fails with 0x80073D02 because the frameworks are in use). `Install.ps1` handles this itself.

## v0.2 (2026-10-20)
- (v0.2 to v0.5.2 used a mobile Android user agent for Discord's phone layout. REVERTED in v0.5.3: Discord blocks
  voice on mobile browsers with an "Unsupported Browser" dialog. The widget must present as desktop Edge, i.e. the
  WebView2 default UA. Compact size is therefore 760x600 so the desktop layout fits; min 480x360.)
- Expand/shrink button (top-right, Game Bar mode only): `TryResizeWindowAsync` to the screen's logical size,
  stepping down (1.0, 0.94 ... 0.68) until Game Bar accepts, then `CenterWindowAsync`; persisted in
  `LocalSettings["expanded"]` only on success; restore runs on Page.Loaded. Confirmed working on the Ally in v0.4.2
  (2026-10-20). Do NOT set `widget.MaxWindowSize` at runtime and keep the manifest max at 8000x8000 (same as the
  Store "Gamebar Browser" widget); the earlier runtime cap + 3840x2160 ceiling made every resize request fail.
- Standalone (Start menu) launch never shows the button; it is a plain window for quick testing only.
- `installer\build-installer.ps1` wraps the newest `_Test` package into `AppPackages\DiscordBar-Setup.exe` (IExpress).

## v0.3 (2026-10-20) touch keyboard inside Game Bar
Problem: tapping a Discord field in the widget never showed the touch keyboard (native widgets and the Store
"Gamebar Browser" widget do). Findings, all verified:
- WebView2 only requests the keyboard after an internal foreground-window check that fails under Game Bar's
  overlay hosting (see react-native-windows#10508). Calling CoreInputView/InputPane.TryShow from the host alone
  did NOT help (0.2.1).
- Edge Game Assist (same WinUI2+WebView2 stack) works because it runs mediumIL + runFullTrust. A UWP app at
  mediumIL needs the Microsoft-only `Microsoft.coreAppActivation` custom capability; install fails with
  0x80073D2A otherwise. Dead end for us.
- Gamebar Browser (45012KrimmerStudios) uses the old EdgeHTML `Windows.UI.Xaml.Controls.WebView`. Discord's
  12 MB main bundle has >11k optional-chaining uses, EdgeHTML cannot parse it. Dead end.
- A plain XAML TextBox inside a sandboxed third-party widget DOES get the keyboard (Gamebar Browser's address bar).
Solution (MainPage.xaml.cs "Touch keyboard bridge"): invisible 1x1 TextBox `KeyBox`. Page script posts kb:1/kb:0
on editable focus/blur; host focuses KeyBox (+CoreInputView.TryShow). Typed text -> `window.__dbInsert`
(execCommand insertText), Backspace -> execCommand delete, Enter -> synthetic keydown + form.requestSubmit,
Esc -> blur. InputPane.Hiding returns focus to the WebView2. Relay verified on the desktop PC.
GOTCHA (cost three test rounds): KeyBox must keep IsTabStop="True". In UWP, IsTabStop=false makes a control
unfocusable, Focus() returns false, and nothing downstream happens. On-device diag line showed it:
"focusOk=False focused=WebView2". With it fixed: "focusOk=True focused=TextBox gamepad=True" and the keyboard shows.
The temporary DiagPanel was removed in v0.4.0 after the Ally confirmed the keyboard works (2026-10-20).
OpenKeyboard also calls CoreInputView.TryShow((CoreInputViewKind)7 = Gamepad) per the Xbox Handheld Compatibility
guideline (https://learn.microsoft.com/gaming/gdk/docs/gdk-dev/pc-dev/handheld/handheld-guidance), then TryShow().

## v0.3.5 (2026-10-20) THE keyboard root cause: missing Game Bar proxy/stub registration
On-device diag (0.3.4) proved a plain visible XAML TextBox could not summon the keyboard in our widget while the
same control in the Store "Gamebar Browser" widget could. Diffing manifests: both working widgets carry the
package-level `<Extension Category="windows.activatableClass.proxyStub">` block that the Game Bar SDK docs
require (https://learn.microsoft.com/xbox/game-bar/guide/pkg-manifest#add-proxystub-registration) and that the
NuGet package does NOT generate. Our manifest lacked it, so the private Game Bar<->widget IPC ("ensures the proper
input/focus transitions") was broken. Added the block (Edge Game Assist's list incl. Private5) after
</Applications>. Also removed our self-declared `ms-gamebarwidget` protocol extension: no working widget declares
it and the docs never ask for it (Game Bar activates via that scheme regardless).

## v0.5 (2026-10-20) power path verified + optimizations
- Standalone run now maps window minimize -> "hidden", so the identical low-power path can be measured on the PC.
  Measured on the 3090 PC (logged-out page): shown 689 MB / renderer 304 MB; hidden after 8 s 376 MB / 86 MB
  (MemoryUsageTargetLevel.Low acts immediately); CPU ~0% hidden; restored 647 MB.
- Bug fixed: TrySuspendAsync completes asynchronously and could land AFTER the widget was reopened (power.log showed
  "suspended" after "shown"). SuspendTimer_Tick now resumes immediately if IsShown when the await returns.
- Hidden also closes the touch keyboard. Extra WebView2 settings: no pinch zoom, no swipe nav, no reputation
  (SmartScreen) checks, no autofill/password save.
- power.log in LocalState records state transitions; `installer\Measure-DiscordBar.ps1` (copied next to the
  installer) samples CPU/RAM of DiscordBar + its WebView2 processes and tails the log.
- Manifest: FavoriteAfterInstall + CompactModePriorityPlacement so Discord sits first on the handheld home bar.
- v0.5.3: browser args now `--renderer-process-limit=1 --process-per-site --disable-features=msSmartScreenProtection
  --disable-background-networking --disable-component-update --no-pings`; CDP Emulation.setEmulatedMedia sets
  prefers-reduced-motion=reduce; MemoryUsageTargetLevel.Low also while visible in GameBarDisplayMode.PinnedOnly.
- v0.5.4-0.5.6: three idle tiers when hidden and out of voice (Game Bar raises several events per transition, so
  the timers start once). 15 s: TrySuspendAsync (freezes heap only; Ally measured 907 MB still resident).
  2 min: Navigate(about:blank) then suspend (PC: renderer 348 -> 42 MB, total 756 -> 451 MB). 10 min: Web.Close(),
  control removed from `Root`; on show a new WebView2 is created and InitWebViewAsync() reruns (PC: 1 process,
  91 MB; reopen = cold start, no crash). Standalone debug keys: F9 toggles hidden, F8 forces the close tier
  (F10 is a Windows system key and never reaches AcceleratorKeyActivated).
- v0.6.0 (research-driven): Game Bar has its OWN idle shutdown of hidden widgets; the SDK's
  `XboxGameBarWidgetActivity` (microphone activity id "AudioStatus") is the keep-alive. Held while InVoice,
  Complete()d when voice ends (also shows Game Bar's mic indicator). `IsDocumentPlayingAudioChanged` now feeds
  the voice check. Pre-warm: GameBarDisplayModeChanged -> Foreground reloads/recreates while still hidden so a
  tap finds Discord ready. Per Microsoft WebView2 perf docs, TrySuspend and MemoryUsageTargetLevel are not mixed
  any more: Low is set only on the in-voice (no-suspend) path. Timings: suspend 10 s, unload 45 s, close 4 min.
- v0.6.1: voice activity uses a plain id ("DiscordVoiceChat") so Game Bar's mic indicator does not sit over the
  game; keep-alive confirmed on the Ally (indicator appeared with the mic id in 0.6.0 and cleared on leaving voice).
- v0.6.2 push-to-talk: `XboxGameBarHotkeyWatcher` (keyboard keys only, delivers down AND up while dismissed) on
  F13-F16, Pause, Scroll Lock, and (v0.6.3, because Armoury Crate's keyboard stops at F12) Ctrl+Alt+M / Ctrl+Alt+V /
  Ctrl+Alt+T via its "Combine Keys" tab. Down -> `window.__dbSetMic(true)` clicks Discord's mic button
  (`button[aria-label="Unmute"|"Mute"]`, fallback Ctrl+Shift+M synthetic keydown); up -> mute. The user maps a
  back button (M1/M2) to one of those keys in Armoury Crate. Discord web PTT itself only works with page focus.
- v0.6.4: Game Bar home-bar icons per https://learn.microsoft.com/xbox/game-bar/guide/icons: `GameBar\Icons\icon.targetsize-{16,20,24,32,44,256}.png`
  (white Clyde glyph) + `icon.light.targetsize-N.png` (dark glyph), generated from installer\logo-master.png by the
  scratch script; without them Game Bar falls back to the app logo and the white bubble vanished on the selected tile.
  Navigation: only discord.com / ptb / canary / discordapp.com / discord.gg hosts stay in-widget (support.discord.com
  trapped the user with no back button); off-app paths on discord.com show a "Back to Discord" button.
  App.UnhandledException writes "CRASH ..." + stack to power.log.
- v0.6.7: push-to-talk is now a pure TOGGLE on key-down (releases ignored, 150 ms debounce) via
  `window.__dbToggleMic()` which reads Discord's current button state and clicks it; the 0.6.5 tap/hold timing
  heuristic was hit-or-miss on the Ally because macro release timing varies. Prefers buttons inside
  `section[aria-label="User area"]`.
- v0.6.8: Ally log (0.6.6) showed "clicked Mute" followed by the next press still finding "Mute": the label-matched
  button is NOT the self-mute. `__dbToggleMic` is now async: clicks, waits 250 ms, verifies the label flipped,
  otherwise dispatches Ctrl+Shift+M; returns candidates ("area:"/"doc:" aria-labels matching mute|microphone|mic)
  so the next log reveals Discord's real labels. Host reads the result via window.__dbLastToggle after 400 ms
  (ExecuteScriptAsync returns the Promise object, not its value).
- v0.6.9 is classic HOLD push-to-talk again (down -> live, release -> muted) via `__dbSetMicState(open)`, which is
  idempotent and reads aria-checked first. The paddle macro DOES deliver a real hold (0.6.6 log: down/release 2 s
  apart); the earlier "tap" diagnosis was wrong, the real bug was stale state reading + shortcut double-flip.
- v0.6.8/0.6.9 mic button findings, from the Ally 0.6.8 log: Discord's self-mute is `button[aria-label=Mute|Unmute]` WITH
  `aria-checked` (true = muted); there is no `section[aria-label="User area"]`; other mic-ish buttons are "More
  Microphone Options" and "<name>, Muted". Discord re-renders the button after a click, so the 0.6.8 stale-element
  check always said "did not flip" and the Ctrl+Shift+M fallback toggled it straight back. Now: findMic() prefers
  the aria-checked button, click, wait 400 ms, re-find, report "muted -> live". Shortcut only if no button. Debounce 300 ms.
- v0.6.5 push-to-talk semantics (superseded): Armoury Crate macros fire press+release instantly (Ally log showed open/closed in
  the same second), so OnPushToTalk treats a release within 300 ms as a TAP = toggle (latched open; next tap closes)
  and a longer hold as classic PTT. `__dbSetMic` matches aria-labels Mute/Unmute and Turn Off/On Microphone and
  returns a status string that is logged ("clicked ...", "already ...", "no mic button found; sent Ctrl+Shift+M").
- v0.7.0: Ally log showed "voice activity error: ArgumentException" after every push-to-talk cycle: Discord STOPS
  the mic track while self-muted, so micLive flips, the activity was Complete()d and re-creating the same id throws
  (SDK: "Throws if provided activityId already exists"). Now one XboxGameBarWidgetActivity for the page lifetime,
  Restart()ed after Complete(). InVoice also tracks WebRTC: MicWatchScript wraps window.RTCPeerConnection and posts
  rtc:1/rtc:0 while a connection is open, so muted-in-channel never looks idle and never starts the suspend timers.
- v0.7.1: paddle unmute with Game Bar closed flipped Discord's button but the other side heard nothing, while
  the same unmute with the widget open worked. Cause: Discord stops the mic track on self-mute and calls
  getUserMedia again on unmute; Windows lets a UWP app CONTINUE capturing in the background but not START a new
  capture while not in the foreground. MicWatchScript now keeps one audio capture per call: track.stop() only
  sets enabled=false, a later audio-only getUserMedia returns the same live tracks (re-enabled), and the device
  is really released (__dbRealStop) when the last RTCPeerConnection closes.
- v0.7.2: (a) paddle = tap-to-toggle AND hold-to-talk (350 ms window) now that state reads are reliable;
  (b) HYPOTHESIS under test: while hidden AND in voice the WebView2 stays Visibility=Visible (Game Bar keeps the
  window off screen anyway) because a page Chromium considers hidden may stop delivering mic audio; the
  Collapsed state is applied only on the no-voice path. Ally test to run: unmute with Discord's own button while
  open, close Game Bar, talk -> phone hears? (decides whether hidden capture itself is the problem).
- v0.7.3: on-widget mic button (MicButton, left of the expand button, Game Bar mode only) toggles via the same
  `__dbSetMicState` path; MicIcon shows E720 (live) / EC54 (muted), refreshed on show and after each change.
  Mic state is read only from buttons whose aria-label matches Mute/Unmute/Turn On|Off Microphone AND have
  aria-checked (Discord's deafen toggle also has aria-checked).
- v0.7.4 ROOT CAUSE of "mic dies when Game Bar closes" (Ally test: unmuted with Discord's own button, closed Game
  Bar -> phone hears nothing; reopen -> works): Windows MUTES a UWP app's audio capture and render streams when the
  app goes to the background (docs: "Detect and respond to audio level changes by the system"). Fix: the general-use
  `<uap3:Capability Name="backgroundMediaPlayback"/>` ("all active audio streams will no longer mute ... app
  lifetime extended"), the same capability Microsoft.GamingApp (Xbox party chat) declares. Not the paddle, not
  WebView2 visibility, not getUserMedia; those earlier fixes were layered on the wrong diagnosis but are harmless.
- v0.7.5: backgroundMediaPlayback alone did NOT fix hidden audio on the Ally. Added diagnostics to power.log:
  Windows.Media.Audio.AudioStateMonitor (capture + render SoundLevel changes = OS muting), CoreApplication
  Entered/LeavingBackground, Window.VisibilityChanged, page visibilitychange, mic track mute/unmute events and a
  15 s track-state heartbeat ("PAGE ..." lines). Also: XboxGameBarWidgetActivity.Restart() after Complete() throws
  ArgumentException("parameter is incorrect") on the Ally, so the activity is now created once and never completed.
- v0.7.6 THE hidden-voice answer (user: "it was working before"): hidden voice worked on 0.6.0 and broke on 0.6.1,
  the only change being the activity id. `XboxGameBarWidgetActivity.GetMicrophoneInputActivityId()` ("AudioStatus")
  is what lets a hidden widget keep capturing audio; a plain id only prevents idle shutdown. Its mic badge over
  the game is unavoidable and only shows while in a call. Held while rtcLive||micLive, Complete()d on leaving;
  re-creation after Complete() needs the old object finalized (GC.Collect + WaitForPendingFinalizers on
  ArgumentException). backgroundMediaPlayback stays declared (harmless, may help playback).
- v0.7.7: tap window 350 -> 700 ms (Ally taps through the Armoury Crate macro arrive slower than a human tap);
  release now logs the measured press length ("tap (N ms)" / "hold (N ms)") for tuning.
- v0.7.8 two modes: MINI (420x620, default) and LARGE (full screen). Mini hides Discord's server rail, channel/DM
  list and member list via injected CSS (MiniCss, keyed on aria-labels "Servers sidebar", "...(channels)",
  "Private channels", "Members" plus class-prefix fallbacks guilds_/sidebar_/membersWrap) toggled by the
  `db-mini` class on <html> through `window.__dbSetMini`. Switch channels in large, then shrink. Discord's phone
  layout is unavailable for good (same UA switch that blocks voice). Manifest default 420x620, min 380x360.
- v0.7.9: mini mode is now STRUCTURAL (0.7.8 label-only CSS showed no effect on the Ally): hide the server rail
  (`nav[aria-label="Servers sidebar"]` or `[class*="guilds_"]`) and its next sibling column unless that column
  holds the chat (`[role=textbox]`, `form`, `[class*="messagesWrapper"]`), plus the member list; re-applied via a
  throttled MutationObserver while mini; inline styles restored on large.
- v0.8.0: Ally log on 0.7.9 showed the microphone activity CONSTRUCTOR failing with ArgumentException "parameter
  is incorrect" while hidden -> Game Bar refuses to START an activity for a non-visible widget. Discord's voice
  reconnects make rtcLive blip, which had Complete()d the activity and then re-created it while hidden. Now:
  voice is considered gone only after 15 s (voiceGoneTimer); a failed start sets voiceActivityPending and is retried
  in the shown branch of ApplyState. Shrink falls back through 420/480/520/600 wide because Game Bar may keep an
  older registration's minimum until reboot ("refused the resize (window is 760x528)").
- v0.8.1: mini mode logs one "PAGE mini rail=... navs=... cols=..." line (nav/aside aria-labels + first class,
  and the rail's sibling columns with a "(chat)" marker) so the Ally's real Discord DOM can be targeted; the
  0.7.9 structural hide still left the desktop sidebars visible there. RTC state is posted on change only
  (the 5 s prune was spamming rtc:0 -> ApplyState -> "shown" every 5 s). Ally log on 0.8.0 confirmed Windows
  logging "capture level: Muted" / "render level: Muted" the moment the widget is hidden without the activity.
- v0.8.2: MINI MODE REMOVED at the user's request (a narrow desktop layout is not the phone-style chat view they
  wanted, and that view is impossible without the mobile UA that blocks voice). One size: manifest default
  1280x720 (min 480x360) and GoFullSizeAsync() on Page.Loaded steps down from the screen size until Game Bar
  accepts. SizeButton, MiniCss, ExpandedSetting are gone; MicButton sits at the top-right alone.
- v0.8.3: push-to-talk keys now also Numpad / * - + . (single keys, no modifiers): Ctrl+Alt+M leaked into games
  (Ctrl = crouch, Alt/M = menus/map). Recommend mapping M2 -> Numpad * in Armoury Crate's Numpad tab, in BOTH
  Gamepad and Desktop modes and any per-game profile (profiles switch with the foreground app, which caused the
  "works then stops for minutes" dead periods on the Ally). Log labels are now "open"/"close".
- v0.8.4 paddle reliability: no local latch state any more. Each press reads Discord's real mic state
  (`window.__dbMicState`) and flips it; a release after a hold >= 700 ms closes only if that press opened the mic.
  `__dbSetMicState` verifies after each click and retries up to 3 clicks (Discord sometimes drops one:
  "muted -> muted" in an Ally log). Muting via Discord's button or the corner MicButton can no longer desync it.
- v0.8.5: MicButton moved out of the page into our own 40 px `TopBar` strip (Grid row 0, #2B2D31, shown only in
  Game Bar mode) under Game Bar's title bar; the WebView2 is row 1 (RecreateBrowserAsync sets Grid.Row=1).
  Game Bar's own title bar cannot host widget controls.
- v0.8.6: TopBar gains Back (CoreWebView2.GoBack, else Navigate(Home)) and Home buttons on the left; MicButton
  stays on the right. The old overlay "Back to Discord" escape hatch remains for off-app paths.
- v0.8.7 keep-alive watchdog: tapping Game Bar's mic badge ends the widget's activity (audio dies, widget still
  thinks it holds it). AudioStateMonitor SoundLevel==Muted while in voice -> Complete() the stale object, GC,
  UpdateVoiceActivity() (immediate retry; if Game Bar refuses while hidden, voiceActivityPending retries on show).
  User guidance: drag the badge, never tap it.
- v0.8.8 push-to-talk diagnostics: every HotkeySetStateChanged logs "KEY <combo> down|up" before any logic, and
  watchers are re-registered (Stop + CreateWatcher) on every shown<->hidden transition in case Game Bar drops them.
  "push-to-talk watchers registered: N/M" at each registration. If a dead period shows no KEY lines, the key never
  reached Game Bar (Armoury Crate profile/macro); if KEY lines exist, the fault is downstream.
- v0.8.9: third in-call signal `callLive` from the page (polls every 5 s for Discord's `[aria-label="Disconnect"]`
  button, posts call:1/call:0 on change). InVoice, the keep-alive wanted flag, the 15 s voice-gone check and the
  watchdog all use callLive || rtcLive || micLive, so a missed WebRTC reconnect can no longer end the activity
  mid-call (user observed the badge vanishing on its own and not returning even with Game Bar open).
- v0.9.0 paddle, from the Ally 0.8.4 log ("close -> muted -> live (2 clicks)", "(3 clicks)"): Discord updates
  the mic button's aria-checked asynchronously, so the 0.8.4 verify-and-retry re-read a stale state and
  double-toggled. Now ONE click per press, never a retry, and presses are serialized through `pttBusy` (presses
  arriving mid-change are dropped and logged). MicIcon follows the intended state, not the read-back.
- v0.9.1 keep-alive sentinel: `keepAliveTimer` every 20 s while in a call re-requests the microphone activity if
  it is not held or Windows reports capture SoundLevel == Muted. User observed the badge vanishing during idle
  and returning "by chance" when spamming the paddle, i.e. hidden re-requests CAN succeed, so retry continuously
  rather than only on show.
Target device: ROG Xbox Ally X (AMD Ryzen AI Z2 Extreme, 24 GB). Remaining cost is Chromium itself; the user-side
levers are Discord settings (reduced motion, no GIF/emoji autoplay, Krisp off).

## Build / package
- VS 2022 + "Universal Windows Platform development" workload.
- Command line: `msbuild DiscordBar.sln /restore /p:Configuration=Release /p:Platform=x64 /p:UapAppxPackageBuildMode=SideloadOnly /p:AppxPackageDir=AppPackages\`
- Output: `DiscordBar\AppPackages\DiscordBar_*_x64_Test\` (copy that folder to the Ally, run `Install.ps1`).

## v0.9.2 (2026-10-21) — muted call no longer reads as "voice gone"
Live log from the Ally (relay) showed the real failure: paddle mute -> `micLive` false -> InVoice false
(`rtcLive`/`callLive` were never true on the Ally) -> suspend 10 s -> activity Complete() after 15 s ->
Windows muted the app. Spamming the paddle unmuted the mic, which revived everything.
Fix: new page signal `micopen:1/0` (a live mic track held open, muted or not) feeds a new `InCall`
(= callLive || rtcLive || micLive || micOpen); voice activity, grace timer, keep-alive and watchdog
all use `InCall`. Call-cue poll widened (any `*isconnect*` / leave/end call button, or a
"Voice Connected" text node) and logs `diag:no call cue; buttons=...` once a minute while the mic is
open but no cue matches, so the selector can be fixed from the log.

## v0.9.3 (2026-10-21) — the mic was being killed by our OWN RTC watcher
Live log: `mic capture started` -> exactly 5 s later `mic capture released`, every time. Cause: the
RTCPeerConnection wrapper never fires on the Ally (Discord gets a pristine constructor, `rtc:1` never
posted), so its 5 s `prune()` saw zero connections and called `releaseMic()` -> real `track.stop()`.
Mute then meant "no mic" -> InVoice false -> idle tiers + activity release. Fixes:
- The page never releases the mic on its own now (30 min all-silenced + no cue fallback only).
- Primary in-call signal is host-side: CDP `Network.webSocketCreated/Closed`; a socket to `*.discord.media`
  (or `<region>.discord.gg`) = in voice (`voiceSocket`, part of `InCall`). On the last voice socket
  closing the host tells the page `__dbReleaseMic('voice socket closed')`.
- Every WebSocket host is logged (`WS open ...`), plus a once-a-minute `dom cue=... buttons=... texts=...`
  dump while a capture exists, to pin down Discord's current voice-panel labels.
- `webkitRTCPeerConnection` alias wrapped too (belt and braces).

## v0.9.4 (2026-10-21) — paddle responsiveness + hotkey delivery gaps
Live log: a press took ~1 s to show its result (fixed 500 ms page wait + fixed 700 ms host wait) and every
tap inside that window was dropped ("press ignored"), which the user saw as "nothing happens". Now the page
polls Discord's button state every 50 ms after the click (done in ~50-100 ms), the host polls the result
every 60 ms, and a press that lands while busy is queued (one slot) and applied after. Hold-release while
busy is queued too.
Separate issue, still open: KEY lines stop arriving entirely for minutes (22:09:54 -> 22:12:07) while the
log relay keeps flowing, then resume after the user focused a text field in the widget. Mitigation: watchers
are re-registered every 60 s while in a call (and still on every show/hide). Cause not yet known: could be
Armoury Crate's per-app profile switching or Game Bar's hotkey delivery.
Also learned: Discord's voice panel in the Ally's layout exposes NO aria-label matching connect/call/leave
and no leaf text "Voice Connected" (dom cue dumps show only Mute | Deafen), and NO WebSocket to
*.discord.media was reported by CDP (only gateway.discord.gg) -> Discord's voice gateway socket is
probably opened from a Web Worker, which the page target's Network domain doesn't see. The reliable
in-call signal remains the open mic capture (`micOpen`), which v0.9.3 stopped killing.

## v0.9.5 (2026-10-21) — paddle key fallbacks + foreground-app logging
User confirmed the paddle types `*` into a text field during a "dead" period, so Armoury Crate is fine and
Game Bar's XboxGameBarHotkeyWatcher is what stops delivering (it resumed once the widget had keyboard
focus). Added: (1) page-side keydown/keyup capture for the PTT keys (posts `ptt:down/up:<code>`, swallows
the key so no `*` is typed), (2) XAML AcceleratorKeyActivated for the same keys when KeyBox/buttons have
focus, (3) `AcceptKeyEdge` de-dupes the three sources within 120 ms, (4) XboxGameBarAppTargetTracker logs
`TARGET "<app>" game= fullscreen=` on change and on each watcher refresh, to correlate with dead periods.

## v0.9.6 (2026-10-21) — stuck microphone activity after an upgrade
After installing 0.9.5 over a running 0.9.4 that held the microphone activity, Game Bar refused EVERY
`new XboxGameBarWidgetActivity(widget, GetMicrophoneInputActivityId())` with ArgumentException "The
parameter is incorrect", shown or hidden, for the whole session -> no audio while hidden. Game Bar keeps the
old process's activity until Game Bar itself restarts (Settings > Apps > Xbox Game Bar > Advanced options >
Terminate, or reboot). Fixes: `ReleaseVoiceActivity()` (Complete) on App.Suspending (with deferral) and in
Shutdown(); failed starts retry at most every 10 s and on show instead of on every ApplyState (the log was
50% failure spam). Same-version reinstall still needs a Game Bar restart if the old process was killed.
Also seen: the DOM cue DOES work in the DM-call layout (Disconnect button + "Voice Connected" text) — it
was only missing while the call UI hadn't rendered yet.

## v0.9.7 (2026-10-21) — push-to-talk no longer depends on Game Bar's hotkey watcher
Root cause of "hit or miss" paddle, from the live log + user test: Armoury Crate delivers the key fine
(`*` types into a text box), and XboxGameBarHotkeyWatcher fires only while Game Bar is open or for a few
seconds after it closes; once Game Bar dozes, presses vanish until Game Bar is opened again (re-registering
the watchers every 60 s did not help). Fix: `KeyHelper\DiscordBar.KeyHelper.exe`, a 9 KB .NET Framework
4.7.2 full-trust process inside the package (`desktop:Extension windows.fullTrustProcess`, `rescap:Capability
runFullTrust`, `TargetDeviceFamily Windows.Desktop` added, `SDKReference WindowsDesktop` for
FullTrustProcessLauncher). It installs WH_KEYBOARD_LL, forwards Numpad */÷/-/+/. , F13-F16, Pause, Scroll
as `{key, down}` over the in-process app service `com.discordbar.keys` (App.OnBackgroundActivated ->
MainPage.OnHelperKey -> AcceptKeyEdge dedupe -> OnPushToTalk), and SWALLOWS the key while connected so it
never leaks into games or the chat box. Single instance (Local\DiscordBar.KeyHelper mutex); exits ~1 min
after the widget disappears; relaunched by EnsureKeyHelper on start and from the keep-alive tick when not
connected. Verified on the dev PC standalone: "key helper launched/connected", injected Numpad * -> "KEY
helper Multiply down/up", tap and 900 ms hold both handled. Working set ~30 MB (mostly shared CLR pages),
0% idle CPU. Build order: `MSBuild KeyHelper\DiscordBar.KeyHelper.csproj /p:Configuration=Release
/p:Platform=x64` FIRST, then the solution (the UWP csproj links the exe as Content).
Manifest gotcha: `rescap:Capability` must come before `DeviceCapability` (schema order) or APPX0501.

## v0.9.8-v0.9.10 (2026-10-21) — key helper hardening (all three found by testing, not guessing)
- 0.9.8: opening the app service from the helper LAUNCHES a headless widget process if none is running
  (background activation). Helper now connects only while a `DiscordBar` process exists and exits ~12 s after
  the last one is gone; the widget refuses a connection when it has no UI (`MainPage.HasUi`, no deferral).
- 0.9.9: on the Ally the first install over a running widget left the helper "connected" to a process that
  never saw its messages, swallowing every paddle press ("doesn't even type * anymore"). Two causes fixed:
  the widget never held a reference to `AppServiceConnection` (collectable -> handlers gone, silent), and
  the helper never checked delivery. Now the widget stores the connection and answers every message
  `{ok}`; the helper verifies each response in `op.Completed`, drops a dead link at once (`Dead()` ->
  stops swallowing, reconnects), and `EnsureKeyHelper` runs on every 20 s keep-alive tick (15 s min gap).
- 0.9.10: newest helper wins (kills other `DiscordBar.KeyHelper` processes at start, so an older package's
  helper can't hold the mutex and the key), and a link that dies within 3 s counts as a refusal (5 s
  back-off, exit after 20) so a refused headless process isn't re-activated every second.
Verified on the dev PC (standalone): start 0.9.9 -> install 0.9.10 over it (old helper replaced, one helper)
-> tap + 900 ms hold delivered via "KEY helper Multiply" -> kill widget (no ghost process, helper gone in
<16 s) -> relaunch (helper launched/connected) -> tap delivered. Windows re-activates the widget after a
servicing update ("key helper service canceled: ServicingUpdate" is normal in the log).

## v0.9.11 (2026-10-21) + the Armoury Crate control-mode finding
Helper: the hook callback now only enqueues (a worker thread does the app-service call: Windows silently
drops a low-level hook that stalls), the hook is re-installed every 30 s (WM_APP+1 to the hook thread), and
a 30 s ping carries hook health; the widget logs `key helper ping: hook=... rehooks=... failures=...` on
the first ping and whenever the hook is missing. Verified on the dev PC: tap before and after the refresh
(rehooks=1), 900 ms hold, clean exit after the widget is killed.
FIELD RESULT with the live log: with v0.9.10 every press reached the widget in 50 ms (Game Bar open or
closed) when Windows Terminal or the Start menu was in front, and NO key event existed at all (helper hook
saw nothing, no `*` typed) when Edge or the bare desktop was in front. Both Armoury modes showed M2 =
NumPad*. Cause: Armoury Crate SE's Auto control mode puts packaged apps (Terminal, Start, Store games) in
Gamepad mode, where Armoury's software injects the key, and classic windows (Edge, Explorer desktop,
unrecognised games) in Desktop mode, where the controller firmware is supposed to emit it and emits nothing
for NumPad* on this unit. Forcing Control Mode = Gamepad in Command Center made the desktop deliver.
Desktop-mode options to try: M2 = Macro(NumPad*) or M2 = F13/Pause (widget + helper accept those too).
This is outside the widget: nothing in DiscordBar can create a key that Armoury never sends.

## v0.9.12 (2026-10-21) — rebrand to "Unofficial Discord for Gamebar"
User's own AI-made mark (installer\logo-source.jpg); all icons are generated by
`installer\make-assets.py` (Pillow, run with the ComfyUI venv python): tiles black-on-white, Game Bar
home-bar icons monochrome (white for dark theme, black `icon.light.*` for light), unplated taskbar badge,
white splash glyph on #313338. Manifest display names changed; widget menu label "Unofficial Discord".
Package identity `DiscordBar.Widget` and code namespaces deliberately unchanged (sideload upgrades).
Installer output is now `UnofficialDiscordForGamebar-Setup.exe`. Plans for clean-up and release are in
RELEASE_PLAN.md. The user reaffirmed name and mark after the trademark caveat; proceed with them.

## Source control (2026-10-21)
Git repo initialised at v0.9.12; remote `origin` = https://github.com/Codex64ai/unofficial-discord-for-gamebar
(PRIVATE until the public beta; default branch `main`; commits are authored as codex64, repo-local config).
`*.pfx`, `bin/`, `obj/`, `AppPackages/`, `.vs/` are ignored; the dev signing key is never committed.
Landing page target: https://codex64.net/discordforgamebar (apex is served through Cloudflare by an app
that returns 404 for that path today; needs a path route or a static route in whatever serves the apex).

## 1.0.0 Beta 1 (2026-10-22) — Phase 1 done
Stripped from the shipping build: page `diag:` posts, the 15 s mic-track heartbeat, the DOM cue dump,
CDP Network/voice-socket tracking (never fired on the Ally), the app-target tracker, AUDIO/APP/WINDOW
lifecycle logging (the AudioStateMonitor itself stays: the keep-alive and watchdog use it), standalone
F8/F9 keys, PAGE lines for call/micopen. Kept: state transitions, voice activity lines, KEY lines and
push-to-talk results (needed to triage user reports), key helper connect/launch/ping-health lines,
CRASH. Dev tools (Measure, Send-Log, log relay) now live OUTSIDE the repo at C:\dev\discordbar-devtools and are
not shipped; the public repo contains only the product.
Measured on the dev PC standalone: DiscordBar.exe private 62 MB / helper private 21 MB (working set
34 MB). The helper's 21 MB is CLR overhead; a C++ port (~1 MB) is the Beta 2 candidate if it matters.
Verified after the strip: helper launch/connect, tap + 900 ms hold delivered, no ghost process, log is
six lines at startup instead of dozens.
Repo build-out: README (push-to-talk, mic badge, idle tiers, troubleshooting, build, credits),
docs/ARCHITECTURE.md, docs/LANDING_PAGE_BRIEF.md (for the user's other agent), PRIVACY.md, CHANGELOG.md,
LICENSE (MIT, codex64), CONTRIBUTING.md, CI workflow (unsigned msix artifact), issue templates,
docs/images (banner, logo, asset sheet; screenshots still to be dropped in by the user).
