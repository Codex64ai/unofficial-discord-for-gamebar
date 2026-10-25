# Privacy policy

**Unofficial Discord for Gamebar** (the "app") is an Xbox Game Bar widget that displays the official
Discord web application (https://discord.com/app) inside a Microsoft Edge WebView2 control.

## What the app collects

Nothing. The app has no servers, no analytics, no crash reporting service and no account system. It
never sends anything to the developer.

## What happens on your device

- **Discord traffic.** Everything you do inside the widget is a direct connection between the embedded
  browser and Discord, governed by [Discord's privacy policy](https://discord.com/privacy). The app does
  not read, store or forward your messages, contacts or credentials. It never uses your Discord token or
  the Discord API.
- **Sign-in data.** The embedded browser keeps Discord's cookies and local storage in the app's own
  isolated profile (`%LOCALAPPDATA%\Packages\<package>\LocalState`) so you stay signed in, exactly like a
  browser profile. Uninstalling the app deletes it.
- **Microphone.** The app asks Windows for microphone access only so Discord voice can work, and only
  grants that permission to discord.com pages. It holds the capture open for the length of a voice call
  (also while you are muted) because a background app cannot reopen the device later; it is released when
  you leave the call. Audio goes only to Discord.
- **Local log.** A 64 KB rolling text file (`power.log` in the folder above) records the app's own state
  changes (shown/hidden, voice start/stop, mic button results, crashes) to help you report bugs. It
  contains no message content and never leaves your device unless you attach it to a bug report yourself.

## Permissions declared

`internetClient` (reach discord.com), `microphone` (voice), `backgroundMediaPlayback` (keep audio
running while the widget is hidden). No restricted capabilities. The app does not watch the keyboard
outside its own window.

## Contact

Open an issue on the GitHub repository.

_Last updated 2026-10-25._
