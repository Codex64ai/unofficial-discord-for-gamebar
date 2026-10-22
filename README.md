<p align="center">
  <img src="docs/images/banner.png" alt="Unofficial Discord for Gamebar" width="100%">
</p>

# Unofficial Discord for Gamebar

Discord's web app as an **Xbox Game Bar widget**, built for Windows handhelds like the ROG Xbox Ally X.
Press the Xbox button, tap Discord, chat, join voice, close Game Bar and keep playing: the call keeps
running, and a controller paddle works as push-to-talk the whole time.

> **Unofficial.** This project is not affiliated with, endorsed by or sponsored by Discord Inc. It embeds
> the official web app at discord.com inside a Microsoft Edge WebView2 control, exactly as a browser
> would. It ships no Discord code, never uses the Discord API and never touches your token.

Website: **https://codex64.net/discordforgamebar** · Download: [Releases](../../releases) ·
Privacy: [PRIVACY.md](PRIVACY.md) · License: [MIT](LICENSE)

## Screenshots

Coming with the public beta.
<!-- Drop the files into docs/images/ with these names and restore the table:
| | |
|---|---|
| ![Discord open in Game Bar](docs/images/widget-open.jpg) <br> *Full screen inside Game Bar, Back / Home / Mic strip on top* | ![Voice call with Game Bar closed](docs/images/voice-hidden.jpg) <br> *In a call with Game Bar closed: the mic badge is Game Bar keeping the audio alive* |
| ![Touch keyboard](docs/images/keyboard.jpg) <br> *The Game Bar touch keyboard typing into Discord* | ![Paddle mapped to Numpad *](docs/images/armoury-m2.jpg) <br> *Armoury Crate: back paddle M2 mapped to Numpad `*`* |
-->

## What you get

- **Discord, full screen, inside Game Bar.** The widget asks Game Bar for the largest window it will grant
  and centers it. The desktop layout is used on purpose (Discord blocks voice on mobile browsers).
- **Voice that survives closing Game Bar.** Join a channel or a call, press the Xbox button to get back
  to the game, and the call keeps going. Mute and unmute from the paddle without opening Game Bar.
- **Push-to-talk on a controller paddle.** Tap to toggle the mic, hold to talk while held. Works whether
  Game Bar is open or closed, and the key never leaks into the game.
- **Touch keyboard** that actually appears when you tap a text field inside Game Bar.
- **Back and Home buttons** on a slim strip above the page, so a Discord help page or a link never traps
  you. Links to anything outside Discord open in your real browser.
- **Light when you're not using it.** Hidden and not in a call, the page is suspended after 10 seconds,
  unloaded after 45 seconds and the browser engine is shut down after 4 minutes. Opening Game Bar
  pre-warms it, so the tap that follows is fast.
- **No pop-ups over games.** Desktop notifications are denied for the page.

## Requirements

- Windows 11 with Xbox Game Bar (the handheld compact UI or the desktop one).
- Microsoft Edge WebView2 Runtime (already present on Windows 11).
- A Discord account. Everything else is Discord's own web app.
- Tested on the ROG Xbox Ally X. Any Game Bar device should work; paddle push-to-talk needs a way to map
  a controller button to a keyboard key (Armoury Crate on ASUS, the equivalent on other handhelds).

## Install

**From a release (today):** download `UnofficialDiscordForGamebar-Setup.exe` from
[Releases](../../releases) and run it. It installs the package and, for this beta, the self-signed
certificate it is signed with (a Microsoft Store build without that step is planned; see
[RELEASE_PLAN.md](RELEASE_PLAN.md)). Upgrades: run the newer installer over the old one.

Then press the **Xbox button** (or `Win+G`), open the widget menu, and pick **Unofficial Discord**.
Pin it or make it a favorite so it sits in the home bar. Sign in with the QR code or your credentials.
Close the desktop Discord app if you have it, so Discord isn't running twice.

## Push-to-talk on a paddle, in detail

### How it works

Game Bar gives widgets exactly one way to receive keys while they're hidden, the
`XboxGameBarHotkeyWatcher` API, and on the Ally it only delivers while Game Bar is open or for a few
seconds after it closes. Once Game Bar dozes, presses vanish until it's opened again. That made the
paddle "hit or miss", which is unusable for push-to-talk.

So the package ships a second, tiny process: **the key helper** (`DiscordBar.KeyHelper.exe`, 9 KB).
The widget launches it on start. It installs a low-level keyboard hook, the same mechanism Game Bar
itself uses, and:

1. watches only the push-to-talk keys listed below,
2. forwards each press and release to the widget over an in-package Windows app service,
3. **swallows the key** while it's connected, so a paddle mapped to Numpad `*` never types a `*` into a
   game, a chat box or a browser,
4. verifies every delivery, drops a dead link immediately and reconnects, re-installs its hook every
   30 s, pings the widget every 30 s, and exits about a minute after the widget is gone.

The widget then reads Discord's **real** mute state from Discord's own mute button and flips it with a
single click of that button. There's no local memory of the state, so muting from Discord's UI, from
the widget's mic button or from the paddle can never get out of sync. A press takes about 50 ms.

Two gestures, both at once:

- **Tap** (released within 700 ms): toggle. Muted becomes live, live becomes muted.
- **Hold** (longer than 700 ms): push-to-talk. The mic opens on press and closes on release, if the
  press is what opened it.

A press that arrives while the previous one is still applying is queued, not dropped, so a quick
double tap is never lost.

The Game Bar hotkey watcher is still registered as a fallback, and so are a key listener inside the
page and one on the widget's own controls; every path goes through the same 120 ms de-duplication, so
a press is never counted twice.

### Keys the widget listens for

| Key | Notes |
|---|---|
| Numpad `*`, `/`, `-`, `+`, `.` | Recommended. Games almost never bind them, and no modifier means nothing leaks. |
| F13, F14, F15, F16 | If your mapper offers them. |
| Pause, Scroll Lock | Rarely bound by games. |
| Ctrl+Alt+M, Ctrl+Alt+V, Ctrl+Alt+T | Game Bar watcher only (not the helper). Avoid: modifier combos leak into games (crouch, menus). |

### Setting it up on the ROG Ally (Armoury Crate SE)

1. Open Armoury Crate SE, go to Settings, Control Mode, Configure.
2. Open the **Gamepad Mode** tab, choose back button **M2**, assign a keyboard key, pick **NumPad \***.
3. Open the **Desktop Mode** tab and do the same. Both tabs must show NumPad `*` for M2.
4. Press the Command Center button and set **Control Mode** to **Gamepad**, not Auto (see below).

### Why the paddle can go dead in some apps (and the fix)

Armoury Crate's **Auto** control mode switches between Gamepad mode and Desktop mode based on the app in
front. Packaged apps (Store games, Windows Terminal, the Start menu) get Gamepad mode, where Armoury's
software injects the key and everything works. Classic windows (Edge, the bare desktop, and any game
Armoury doesn't recognize) get Desktop mode, where the key is produced by the controller firmware,
which on tested units emits nothing for NumPad `*`. The widget can't do anything about a key that was
never sent. Forcing **Control Mode = Gamepad** fixes it everywhere. Inside Armoury Crate's own window
the paddle never fires; Armoury reserves it there.

## About the microphone badge

While you're in a call with Game Bar closed, Game Bar shows a small microphone badge on screen. People
ask for it to be hidden. It can't be, and here's why.

Windows mutes the audio of a UWP app that isn't in the foreground. Game Bar's way to let a hidden
widget keep capturing and playing audio is an `XboxGameBarWidgetActivity` created with Game Bar's
**microphone activity id**. That id is what tells Windows "this widget may keep using audio while
hidden", and the badge is Game Bar's indicator for exactly that permission. A plain activity keeps the
process alive but the call goes silent. So the badge and the working call are the same thing.

Things to know:

- **Don't tap the badge.** Tapping it ends the activity and Windows mutes the call. Drag it to a corner
  instead. If it does get tapped, the widget notices Windows muting it and asks Game Bar for the
  activity again; open Game Bar once if it doesn't come back on its own.
- It appears when you join voice and disappears when you leave.
- The widget keeps the microphone capture open for the whole call, muted or not, because a background
  app is not allowed to reopen the microphone later. Muting still mutes; Discord just sees a silenced
  track instead of a closed one.

## How it stays light

Measured on the ROG Xbox Ally X. Numbers are the whole widget including its WebView2 processes.

| State | What runs | Memory (approx.) |
|---|---|---|
| Open, chatting | Discord's page, one renderer process | ~780 MB |
| Hidden, in a call | Page kept active for audio, low-memory mode | ~400 MB |
| Hidden, idle 10 s | Page suspended by WebView2 | drops with time |
| Hidden, idle 45 s | Page unloaded (about:blank) | ~200 MB |
| Hidden, idle 4 min | Browser engine closed | ~90 MB |

Other choices that matter: one renderer process and one process per site (`--renderer-process-limit=1`),
no SmartScreen lookups, no background networking or component updates from the engine, reduced-motion
signalled to the page so Discord drops its animations, video elements paused while hidden, memory-usage
target set to low whenever the widget is pinned over a game or hidden in a call.

## Other things worth knowing

- **Touch keyboard.** WebView2 can't summon the keyboard inside Game Bar. The widget watches the page for
  a focused text field, moves focus to an invisible text box of its own (which Game Bar does give a
  keyboard), and relays what you type back into the page, suggestions included.
- **Full screen.** The widget requests the whole screen and steps down (94 %, 88 % …) until Game Bar
  accepts a size, then centers.
- **Where you can browse.** Only discord.com (and its ptb/canary/discordapp/discord.gg hosts) load inside
  the widget; everything else opens in your default browser. A Discord page that isn't the app (terms,
  help, blog) shows a "Back to Discord" button.
- **Desktop user agent.** On purpose. Discord's phone layout would fit a 7" screen better, but Discord
  refuses voice on mobile browsers ("Unsupported Browser").
- **Standalone window.** The app also runs from the Start menu as a plain window, useful for signing in
  with a mouse and keyboard. The low-power path only exists inside Game Bar.
- **Upgrading while in a call.** Install the new version after leaving voice. If the mic badge refuses to
  come back after an upgrade, terminate Game Bar once (Settings, Apps, Xbox Game Bar, Advanced options,
  Terminate) or reboot; Game Bar holds the old activity until then.

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| Paddle works in some apps, not in Edge or on the desktop | Armoury Crate Auto control mode. Set Control Mode to Gamepad (above). |
| Paddle does nothing anywhere | Check M2 shows NumPad `*` in both Armoury tabs. Then open Game Bar once so the widget relaunches the key helper. |
| Mic badge vanished and the call went silent | The badge was tapped. Open Game Bar, the widget re-requests the activity. |
| "Unsupported Browser" when joining voice | Something changed the user agent; file an issue with your version. |
| Keyboard doesn't appear | Tap the field once more. If it never appears, reinstall: the Game Bar proxy/stub registration in the package manifest is what makes keyboards work. |
| Widget shows a blank page after being idle | Normal: it was unloaded to save memory and is reloading. 2–3 s. |

## Logs and bug reports

The widget keeps a small rolling log of its own state changes (no message content):
`%LOCALAPPDATA%\Packages\DiscordBar.Widget_*\LocalState\power.log`. Paste the path into File Explorer.
Attach the last 100 lines or so to a [bug report](../../issues/new/choose).

## Building from source

Requirements: Visual Studio 2022 with the Universal Windows Platform workload and the Windows 11 SDK
(10.0.22621). The .NET Framework 4.7.2 targeting pack (included with VS) builds the key helper.

```bash
msbuild KeyHelper\DiscordBar.KeyHelper.csproj /p:Configuration=Release /p:Platform=x64
msbuild DiscordBar.sln /p:Configuration=Release /p:Platform=x64 /p:UapAppxPackageBuildMode=SideloadOnly /p:AppxPackageDir=AppPackages\
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
```

Build the helper first; the app project links its exe into the package. The sideload package is signed
with `DiscordBar\DiscordBar_TemporaryKey.pfx` (not in the repo; create your own test certificate in VS or
with `New-SelfSignedCertificate`, publisher `CN=DiscordBarDev`). CI builds an unsigned package on every
push as a check.

Project layout:

| Path | What |
|---|---|
| `DiscordBar/` | The UWP widget (WinUI 2, WebView2). `MainPage.xaml.cs` is the whole state machine. |
| `KeyHelper/` | The push-to-talk key helper (classic .NET Framework, no NuGet). |
| `installer/` | Installer build script, Setup.ps1, logo source and icon generator. |
| `docs/` | Architecture notes, images, landing-page brief. |
| `CLAUDE.md` | The engineering log: every dead end and why the current design is what it is. |

[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) explains the moving parts.

## Credits

Designed, directed and tested on the Ally by **codex64**, built together with Claude (Anthropic's
Claude Code) as pair programmer. Built on Microsoft's Xbox Game Bar SDK, WinUI 2 and WebView2.
"Discord" is a trademark of Discord Inc.; this project is independent of it.

## License

MIT, see [LICENSE](LICENSE).
