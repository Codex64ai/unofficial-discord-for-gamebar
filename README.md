# Unofficial Discord for Gamebar

Discord's web app as an Xbox Game Bar widget, tuned to use as little as possible on handhelds such as the ROG Xbox Ally X. Not affiliated with or endorsed by Discord Inc.; it only wraps the official web app at discord.com, like a browser does.

Internal project/package name: DiscordBar (unchanged so sideload upgrades keep working).

## How it saves resources
- Runs Discord's web app in one browser engine. Close desktop Discord so it isn't running twice.
- Widget hidden: page hidden + low-memory mode.
- Hidden and not in voice for 30 s: fully suspended. It wakes when you open it.
- In voice: never suspended, so calls keep working.
- Notifications are blocked, so no pop-ups over games.

## Build (on the Windows 3090 PC)
1. Install Visual Studio 2022 Community with the **Universal Windows Platform development** workload.
2. Open `DiscordBar.sln` and pick **Release | x64**.
3. Right-click the project → **Publish → Create App Packages → Sideloading**. Keep the default signing (VS makes a test certificate).
4. You'll get a folder like `AppPackages\DiscordBar_0.1.0.0_x64_Test\`.

Quick test on the 3090 itself: press F5 (Windows asks to enable Developer Mode), then Win+G → Widget menu → **Discord**.

## Install on the ROG Xbox Ally X
1. Copy the whole `..._Test` folder to the Ally (USB stick or network share).
2. Right-click `Install.ps1` → **Run with PowerShell**. It installs the test certificate and the app.
3. Win+G (or the Xbox button) → Widget menu → **Discord** → log in (the QR login works well) → pin it if you like.

## Lower usage further (Discord settings, inside the widget)
- Accessibility → Reduced Motion: **on**
- Accessibility → autoplay GIFs / animated emoji / stickers: **off**
- Voice & Video → noise suppression: try **None** or **Standard** (Krisp costs CPU)

## Check on device
- Task Manager: watch the "msedgewebview2" processes with Game Bar closed vs open.
- Join voice, close Game Bar, keep talking for 2+ minutes. Make sure the call doesn't drop.
