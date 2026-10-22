# Landing page brief: Unofficial Discord for Gamebar

For the agent building https://codex64.net/discordforgamebar. Everything a page needs to say, in the
order it should say it, plus the facts behind each claim so nothing on the page overstates what the
product does. Assets are in the GitHub repo (`docs/images/`, `installer/`).

## 1. One-paragraph description (what it is)

Unofficial Discord for Gamebar is a free, open-source Xbox Game Bar widget for Windows 11 handhelds
(built and tested on the ROG Xbox Ally X). It puts Discord's official web app inside Game Bar so you
can press the Xbox button, open Discord over your game, chat, join voice, then close Game Bar and keep
playing while the call keeps running. Push-to-talk is the Numpad * key, so any controller button mapped to it
(M1, M2, a back paddle) works as push-to-talk, with Game Bar open or closed. It is not made by Discord and does not touch Discord's API; it displays
discord.com exactly the way a browser does.

Tagline options: "Discord, in your Game Bar." · "Voice chat that survives closing Game Bar." ·
"Any button can be your push-to-talk key."

## 2. The problem it solves (the "why")

On a Windows handheld, Discord's desktop app is heavy (two copies of Chromium if you also play a
browser-based game), it steals focus, it is awkward to reach from a game, and alt-tabbing out of a game
on a 7" screen with a controller is clumsy. Game Bar is the handheld's built-in overlay, one button
away, but it has no Discord widget. This widget fills that gap and is tuned for the device: it idles
down to about 90 MB when you're not using it, and it is designed around the two things that matter in a
voice call on a handheld: audio that keeps working with the overlay closed, and a hardware button to
talk.

## 3. Features (each one is true and verified on the Ally)

1. **Full-screen Discord inside Game Bar.** The widget requests the largest window Game Bar grants and
   centers it. The desktop layout is used because Discord blocks voice on mobile browsers.
2. **Voice keeps running with Game Bar closed.** Join a channel or a call, press the Xbox button, play.
   The widget holds Game Bar's microphone activity for the length of the call so Windows lets a hidden
   app keep using audio. Game Bar shows a small mic badge while this is active (see section 6).
3. **Push-to-talk on any button you choose.** Push-to-talk is Numpad `*`; map M1, M2, a back paddle or
   any button to it. Tap to toggle mute; hold to talk while held. Presses
   are read by a tiny helper process inside the package, so they arrive whether Game Bar is open or not,
   and the key is swallowed so it never reaches the game. A press takes about 50 ms.
4. **Touch keyboard that appears.** Tap a text field inside Game Bar and the keyboard comes up (Game Bar
   normally doesn't give web views one; the widget bridges it).
5. **Back and Home buttons** on a slim strip above the page. A help page or a link can't trap you. Links
   outside Discord open in the real browser.
6. **Idles down when hidden.** Not in a call and hidden: page suspended after 10 s, unloaded after 45 s,
   browser engine closed after 4 minutes (about 90 MB left). Opening Game Bar pre-warms it.
7. **No pop-ups over games.** Desktop notifications are denied.
8. **No account risk.** No Discord API, no token, no bot; the app can't be used to automate Discord.
9. **Free and open source (MIT).** Code on GitHub.

## 4. How it works (for a "How it works" or "Under the hood" section)

- The widget is a Windows UWP app (WinUI 2) that Game Bar hosts as an overlay. Inside it is a Microsoft
  Edge WebView2 control showing https://discord.com/app with a Discord-only navigation policy.
- Two small scripts are injected into the page: one keeps a single microphone capture open for the whole
  call (a background app isn't allowed to reopen the mic later, so mute just silences the track) and
  reports "in a call" to the host; the other bridges the touch keyboard and reads/flips Discord's own
  mute button for push-to-talk.
- A 9 KB helper process (shipped in the same package) installs a keyboard hook, forwards only the
  push-to-talk keys to the widget, and swallows them. Game Bar's own hotkey API is kept as a fallback.
- While in a call the widget holds Game Bar's microphone "activity", which is what keeps a hidden widget
  alive and audible. Out of a call it steps down through three idle tiers.

## 5. Requirements and install (for a "Get it" section)

- Windows 11 with Xbox Game Bar; the WebView2 Runtime (present on Windows 11); a Discord account.
- Tested on ROG Xbox Ally X. Other Game Bar devices should work; push-to-talk needs the device's
  button-mapping software to send a keyboard key (Armoury Crate SE on ASUS).
- **Today: GitHub release.** Download `UnofficialDiscordForGamebar-Setup.exe`, run it. For the beta it
  installs the package and its self-signed certificate (Windows asks for admin once).
- **Planned: Microsoft Store** listing (no certificate step). Leave a placeholder for a Store badge.
- First run: press the Xbox button (or Win+G), open the widget menu, pick "Unofficial Discord", sign in
  (QR code works well), pin or favorite it. Close the desktop Discord app so it isn't running twice.

## 6. Paddle setup guide (a dedicated section; users need this)

1. Armoury Crate SE → Settings → Control Mode → Configure.
2. Gamepad Mode tab → the button you want (M1, M2, …) → keyboard key → **NumPad \***.
3. Desktop Mode tab → same.
4. Command Center button → set **Control Mode to Gamepad** (not Auto).
5. In a call: tap the button to toggle, hold it to talk.

Legion Go and other handhelds: map any button to Numpad `*` in the device's own software (Legion Space
on the Legion Go). Say "should work, not yet tested" until someone confirms.

Why step 4: Armoury's Auto mode uses Gamepad mode for packaged apps and Store games and Desktop mode for
classic windows (Edge, the desktop, unrecognized games). In Desktop mode the controller firmware emits
nothing for NumPad `*` on tested units, so the button goes dead in those apps. Forcing Gamepad mode makes
it work everywhere. (Inside Armoury Crate's own window the button never fires; that's by design.)

Keys the widget accepts: Numpad `* / - + .`, F13–F16, Pause, Scroll Lock. Recommend Numpad `*`.

## 7. The microphone badge (a FAQ entry, worded carefully)

"While you're in a call with Game Bar closed, a small microphone badge stays on screen. It can't be
hidden: it is Game Bar's indicator that the widget is allowed to keep using audio while hidden, and
that permission is what keeps your call alive. Don't tap it (tapping ends the permission and the call
goes silent; open Game Bar once to restore it). Drag it to a corner. It disappears when you leave the
call."

## 8. FAQ (suggested entries)

- Is this made by Discord? No. Unofficial, independent, open source. Discord is a trademark of Discord Inc.
- Does it use my Discord token or the API? No. It only shows discord.com, like a browser.
- Why the desktop layout and not the phone layout? Discord blocks voice from mobile browsers.
- How much memory does it use? About 780 MB open, about 400 MB hidden in a call, about 90 MB after four
  idle minutes (engine shut down). Numbers measured on the Ally.
- Does the push-to-talk key reach my game? No. The helper swallows it while connected.
- Can I use a different key? Yes, any in the list above.
- Where are the logs? `%LOCALAPPDATA%\Packages\DiscordBar.Widget_*\LocalState\power.log` (state changes
  only, no messages). Attach to bug reports.
- What data does it collect? None. See the privacy policy.

## 9. Privacy policy page

Publish `PRIVACY.md` from the repo as its own page (needed for the Store submission later). Summary:
no servers, no analytics, no data collected; Discord traffic goes straight to Discord; mic used only
for Discord voice; the key helper watches only the push-to-talk keys; a 64 KB local log, never uploaded.

## 10. Brand and assets

- Name: **Unofficial Discord for Gamebar** (always with "Unofficial"). Short label in the Game Bar menu:
  "Unofficial Discord".
- Mark: the face glyph. Files: `installer/logo-glyph.png` (black glyph, transparent background, master),
  `docs/images/logo.png` (white mark on a dark rounded tile, 256 px), `docs/images/banner.png` (wide
  header), `installer/asset-sheet.png` (overview of the icon set).
- Colors used by the app: page background `#313338`, strip `#2B2D31`, button `#3F4248`, text white.
  Dark theme first; the page should also read fine in light mode.
- Tone: plain, direct, technical when it helps. No Discord brand colors or Discord's own logo.
- Required notice somewhere visible: "Not affiliated with, endorsed by or sponsored by Discord Inc."

## 11. Links

- GitHub: https://github.com/Codex64ai/unofficial-discord-for-gamebar (releases, issues, source)
- Privacy: the repo's PRIVACY.md (or the page built from it)
- Credits line: "By codex64, built with Claude Code."

## 12. Screenshots to use

Take on the Ally (1920×1080 or 1080p screenshots via Game Bar's own capture or the device's
screenshot shortcut):
1. Discord open inside Game Bar, full screen, with the Back/Home/Mic strip visible.
2. A game running with Game Bar closed, the mic badge visible in a corner, a voice call ongoing.
3. The touch keyboard open over a Discord text field inside Game Bar.
4. Armoury Crate's Gamepad Mode mapping page showing a button mapped to NumPad * (already available as a photo).
5. Optional: Task Manager showing the widget's memory after it idled down.
