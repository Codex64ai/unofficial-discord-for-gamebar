using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Gaming.XboxGameBar;
using Microsoft.Gaming.XboxGameBar.Input;
using Microsoft.Web.WebView2.Core;
using Windows.Foundation;
using Windows.Graphics.Display;
using Windows.Storage;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.ViewManagement.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;

namespace DiscordBar
{
    // Hosts Discord's web app and keeps it as quiet as possible while you play:
    //   shown            -> normal
    //   hidden           -> low memory mode, page hidden so Chromium throttles it
    //   hidden, no voice -> fully suspended after SuspendDelay (resumes when shown)
    public sealed partial class MainPage : Page
    {
        const string Home = "https://discord.com/app";
        static readonly TimeSpan SuspendDelay = TimeSpan.FromSeconds(10);
        // Suspend only freezes Discord's heap (measured ~900 MB on the Ally). After this long hidden and
        // out of voice, unload the page itself; reopening reloads Discord (2-3 s, still logged in).
        static readonly TimeSpan UnloadDelay = TimeSpan.FromSeconds(45);
        // After this long hidden and out of voice, close the browser engine entirely (its fixed cost is
        // ~400 MB of helper processes). Reopening recreates it: a cold start of a few seconds.
        // Game Bar opening pre-warms, so the user rarely pays this on tap.
        static readonly TimeSpan CloseDelay = TimeSpan.FromMinutes(4);

        // One renderer, one process per site, no SmartScreen lookups, no background update/ping traffic.
        const string BrowserArgs =
            "--renderer-process-limit=1 --process-per-site " +
            "--disable-features=msSmartScreenProtection " +
            "--disable-background-networking --disable-component-update --no-pings";

        // NOTE: do not spoof a mobile user agent. Discord's phone layout fits a handheld better, but
        // Discord blocks voice calls from mobile browsers ("Unsupported Browser"), so the widget must
        // present as desktop Edge (WebView2's default).

        // One size only: the widget asks Game Bar for the whole screen when it opens (mini mode was dropped;
        // Discord's desktop layout in a narrow window is not a usable "chat" view).

        // Tracks live microphone tracks AND live voice connections so we never suspend mid-call.
        // Discord stops the mic track while self-muted, so the mic alone is not enough: a muted user in a
        // quiet channel is still in the call. The WebRTC connection is the reliable "in a voice channel" signal.
        const string MicWatchScript = @"(() => {
  const post = m => window.chrome.webview.postMessage(m);
  // Microphone: keep ONE capture open for the whole call. Discord stops the mic on self-mute and starts a
  // new capture on unmute, but Windows refuses to START a capture for a UWP app that isn't in the foreground
  // (a hidden widget), so unmuting from the paddle with Game Bar closed went silent. Here 'stop' only
  // silences the track and the next getUserMedia hands the same live track back; the device is released
  // for real when the voice connection closes.
  const md = navigator.mediaDevices;
  let micTracks = [];
  const releaseMic = () => {
    if (!micTracks.length) return;
    for (const t of micTracks) if (t.__dbRealStop) t.__dbRealStop(); micTracks = [];
  };
  window.__dbReleaseMic = () => releaseMic();
  if (md && md.getUserMedia) {
    const live = new Set();
    const report = () => post(live.size > 0 ? 'mic:1' : 'mic:0');
    const orig = md.getUserMedia.bind(md);
    md.getUserMedia = async (constraints) => {
      const audioOnly = constraints && constraints.audio && !constraints.video;
      if (audioOnly && micTracks.length && micTracks.every(t => t.readyState === 'live')) {
        for (const t of micTracks) t.enabled = true;
        report();
        return new MediaStream(micTracks);
      }
      const stream = await orig(constraints);
      if (audioOnly) {
        releaseMic();
        micTracks = stream.getAudioTracks();
        for (const t of micTracks) {
          live.add(t);
          const realStop = t.stop.bind(t);
          t.__dbRealStop = () => { realStop(); live.delete(t); report(); };
          t.stop = () => { t.enabled = false; };   // Discord's 'stop' = silence, keep the device open
          t.addEventListener('ended', () => { live.delete(t); report(); });
        }
      }
      report();
      return stream;
    };
  }
  // In-call signals polled every 5 s and posted on change: Discord's own voice-connected cue (Disconnect
  // button / Voice Connected text), and whether a mic capture is held open (muted or not = in a call).
  let lastCall = null, lastMicOpen = null, silencedAt = 0;
  const callCues = () => {
    if (document.querySelector('button[aria-label*=""isconnect"" i], button[aria-label*=""leave call"" i], button[aria-label*=""end call"" i]')) return 'button';
    for (const e of document.querySelectorAll('div, span, h3')) {
      if (e.childElementCount === 0 && /^(voice|rtc) connected$/i.test((e.textContent || '').trim())) return 'text';
    }
    return '';
  };
  setInterval(() => {
    const cue = callCues();
    const inCall = !!cue;
    if (inCall !== lastCall) { lastCall = inCall; post(inCall ? 'call:1' : 'call:0'); }
    const micOpen = micTracks.some(t => t.readyState === 'live');
    if (micOpen !== lastMicOpen) { lastMicOpen = micOpen; post(micOpen ? 'micopen:1' : 'micopen:0'); }
    // Fallback only: Discord has had every track 'stopped' (silenced) for 30 min with no call cue -> let go.
    const allSilenced = micTracks.length && micTracks.every(t => !t.enabled);
    if (allSilenced && !inCall) { if (!silencedAt) silencedAt = Date.now(); else if (Date.now() - silencedAt > 1800000) releaseMic(); }
    else silencedAt = 0;
  }, 5000);
  const RPC = window.RTCPeerConnection;
  if (RPC) {
    const open = new Set();
    let lastRtc = null;
    const report = () => {
      const now = open.size > 0;
      if (now !== lastRtc) { lastRtc = now; post(now ? 'rtc:1' : 'rtc:0'); }   // on change only (no 5 s spam)
    };
    const prune = () => { for (const pc of open) if (pc.connectionState === 'closed' || pc.signalingState === 'closed') open.delete(pc); report(); };
    const Wrapped = function (...args) {
      const pc = new RPC(...args);
      open.add(pc);
      pc.addEventListener('connectionstatechange', prune);
      pc.addEventListener('signalingstatechange', prune);
      const close = pc.close.bind(pc);
      pc.close = () => { close(); open.delete(pc); report(); };
      report();
      return pc;
    };
    Wrapped.prototype = RPC.prototype;
    Object.setPrototypeOf(Wrapped, RPC);
    window.RTCPeerConnection = Wrapped;
    if (window.webkitRTCPeerConnection) window.webkitRTCPeerConnection = Wrapped;
    setInterval(prune, 5000);
  }
})();";

        // Touch keyboard bridge.
        // Inside Game Bar, WebView2 never asks Windows for the touch keyboard (its foreground check
        // fails under Game Bar's overlay hosting), but a plain XAML TextBox does get one, exactly like
        // every native widget. So: the page reports when an editable element gains/loses focus, the
        // host moves XAML focus to an invisible TextBox (KeyBox) which brings the keyboard up, and
        // everything typed into KeyBox is relayed straight back into the page.
        const string KeyboardWatchScript = @"(() => {
  const skip = new Set(['button','checkbox','radio','submit','reset','range','file','color','image','hidden']);
  const editable = el => !!el && (
    (el.tagName === 'INPUT' && !skip.has((el.type || 'text').toLowerCase())) ||
    el.tagName === 'TEXTAREA' || el.isContentEditable);
  const post = m => window.chrome.webview.postMessage(m);
  let hideTimer;
  document.addEventListener('focusin', e => {
    if (!editable(e.target)) return;
    clearTimeout(hideTimer);
    post('kb:1');
  }, true);
  document.addEventListener('focusout', e => {
    if (!editable(e.target)) return;
    clearTimeout(hideTimer);
    hideTimer = setTimeout(() => { if (!editable(document.activeElement)) post('kb:0'); }, 200);
  }, true);
  // Push-to-talk fallback: Game Bar's hotkey watcher has been seen to stop delivering until the widget
  // has keyboard focus. When it does have focus, the paddle key reaches the page itself, so catch it here
  // (host de-duplicates against the watcher). Swallowed so it never types a '*' into chat.
  const pttCodes = new Set(['NumpadDivide','NumpadMultiply','NumpadSubtract','NumpadAdd','NumpadDecimal','F13','F14','F15','F16','Pause','ScrollLock']);
  const pttKey = e => pttCodes.has(e.code) || ((e.ctrlKey && e.altKey) && /^Key[MVT]$/.test(e.code));
  window.addEventListener('keydown', e => { if (!pttKey(e)) return; e.preventDefault(); e.stopPropagation(); if (!e.repeat) post('ptt:down:' + e.code); }, true);
  window.addEventListener('keyup', e => { if (!pttKey(e)) return; e.preventDefault(); e.stopPropagation(); post('ptt:up:' + e.code); }, true);
  // Tapping a field that already has focus (keyboard was dismissed) should bring it back.
  document.addEventListener('pointerup', e => {
    if (editable(e.target) && document.activeElement === e.target) post('kb:1');
  }, true);

  // Relay targets, called by the host with what the user typed into the XAML box.
  const target = () => { const el = document.activeElement; return editable(el) ? el : null; };
  window.__dbInsert = t => { if (target()) document.execCommand('insertText', false, t); };
  window.__dbBackspace = () => { if (target()) document.execCommand('delete'); };
  window.__dbEnter = shift => {
    const el = target(); if (!el) return;
    const init = { key: 'Enter', code: 'Enter', keyCode: 13, which: 13, bubbles: true, cancelable: true, shiftKey: !!shift };
    const handled = !el.dispatchEvent(new KeyboardEvent('keydown', init));
    el.dispatchEvent(new KeyboardEvent('keyup', init));
    if (!handled && !shift && el.form && el.form.requestSubmit) el.form.requestSubmit();
  };
  window.__dbBlur = () => { const el = target(); if (el) el.blur(); };

  // Push-to-talk: open = unmute, !open = mute. Uses Discord's own mic button (aria-label is
  // 'Mute' when live, 'Unmute' when muted); falls back to the Ctrl+Shift+M toggle shortcut.
  window.__dbSetMic = open => {
    // Discord has used both 'Mute'/'Unmute' and 'Turn Off Microphone'/'Turn On Microphone'.
    const isMuted = l => /^(unmute|turn on microphone)$/i.test(l);
    const isLive  = l => /^(mute|turn off microphone)$/i.test(l);
    let btn = null, label = '';
    // Prefer the mic button in the user's own panel (bottom-left); fall back to any matching button.
    const scopes = [document.querySelector('section[aria-label=""User area""]'), document];
    for (const scope of scopes) {
      if (!scope) continue;
      for (const b of scope.querySelectorAll('button[aria-label], [role=""button""][aria-label]')) {
        const l = b.getAttribute('aria-label') || '';
        if (isMuted(l) || isLive(l)) { btn = b; label = l; break; }
      }
      if (btn) break;
    }
    if (btn) {
      const muted = isMuted(label);
      if (open === muted) { btn.click(); return 'clicked ""' + label + '""'; }
      return 'already ' + (open ? 'open' : 'closed') + ' (""' + label + '"")';
    }
    const init = { key: 'm', code: 'KeyM', keyCode: 77, which: 77, ctrlKey: true, shiftKey: true, bubbles: true, cancelable: true };
    document.dispatchEvent(new KeyboardEvent('keydown', init));
    document.dispatchEvent(new KeyboardEvent('keyup', init));
    return 'no mic button found; sent Ctrl+Shift+M';
  };
  // Discord's self-mute is a toggle button: aria-label 'Mute'/'Unmute' with aria-checked (true = muted).
  // Discord re-renders it after a click, so state is always read from a FRESH lookup.
  const findMic = () => {
    const isSelfMute = l => /^(mute|unmute|turn (on|off) microphone)$/i.test(l);
    let fallback = null;
    for (const b of document.querySelectorAll('button[aria-label], [role=""button""][aria-label]')) {
      const l = b.getAttribute('aria-label') || '';
      if (!isSelfMute(l)) continue;
      if (b.hasAttribute('aria-checked')) return b;   // the real toggle
      if (!fallback) fallback = b;
    }
    return fallback;
  };
  const micState = b => {
    if (!b) return 'none';
    const c = b.getAttribute('aria-checked');
    if (c !== null) return c === 'true' ? 'muted' : 'live';
    return /^(unmute|turn on microphone)$/i.test(b.getAttribute('aria-label') || '') ? 'muted' : 'live';
  };
  // Push-to-talk: make the mic match `open`. Idempotent, verified, retried: Discord occasionally drops a
  // click (seen as muted -> muted), so the state is re-read and the click repeated up to twice.
  window.__dbSetMicState = async open => {
    const want = open ? 'live' : 'muted';
    let b = findMic();
    if (!b) return 'no mic button';
    const before = micState(b);
    if (before === want) return 'already ' + want;
    // ONE click, no retry: Discord updates the button state asynchronously, and re-reading too early
    // made a retry double-toggle it (Ally log: close -> muted -> live (2 clicks)).
    b.click();
    // Poll for the re-render instead of a fixed wait: done as soon as Discord shows the new state.
    for (let i = 1; i <= 12; i++) {
      await new Promise(r => setTimeout(r, 50));
      const now = micState(findMic());
      if (now === want) return before + ' -> ' + now + ' (' + (i * 50) + ' ms)';
    }
    return before + ' -> ' + micState(findMic()) + ' (no change after 600 ms)';
  };
  window.__dbMicState = () => micState(findMic());

  // Hidden widget: stop decoding video (streams, clips, GIF-videos). Audio elements and the
  // WebRTC voice path are left alone, so a call keeps going. Resumes only what we paused.
  window.__dbSetHidden = hidden => {
    document.querySelectorAll('video').forEach(v => {
      if (hidden) { if (!v.paused) { v.pause(); v.dataset.dbPaused = '1'; } }
      else if (v.dataset.dbPaused) { delete v.dataset.dbPaused; v.play().catch(() => {}); }
    });
  };
})();";

        XboxGameBarWidget widget;
        static MainPage current;
        static DateTime lastHelperLaunch;

        public static void Log(string line) => PowerLog(line);
        public static bool HasUi => current != null;

        // Key edge from the full-trust helper (any thread). Dedupe against Game Bar's own watcher, then the
        // same press/release logic as every other path.
        public static bool OnHelperKey(string key, bool down)
        {
            var page = current;
            if (page == null) return false;
            _ = page.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (page.AcceptKeyEdge(down, "helper " + key)) page.OnPushToTalk(down);
            });
            return true;
        }

        static void EnsureKeyHelper(string why)
        {
            if (App.KeyHelperConnected || DateTime.UtcNow - lastHelperLaunch < TimeSpan.FromSeconds(15)) return;
            lastHelperLaunch = DateTime.UtcNow;
            App.LaunchKeyHelper(why);
        }

        // One press may arrive from up to three paths (Game Bar watcher, the page, XAML focus); the first wins.
        readonly DateTime[] lastKeyEdge = new DateTime[2];
        bool AcceptKeyEdge(bool down, string source)
        {
            var now = DateTime.UtcNow;
            int i = down ? 0 : 1;
            if (now - lastKeyEdge[i] < TimeSpan.FromMilliseconds(120)) return false;
            lastKeyEdge[i] = now;
            lastKeySeen = now;
            PowerLog($"KEY {source} {(down ? "down" : "up")}");
            return true;
        }

        readonly DispatcherTimer suspendTimer = new DispatcherTimer { Interval = SuspendDelay };
        readonly DispatcherTimer unloadTimer = new DispatcherTimer { Interval = UnloadDelay };
        readonly DispatcherTimer closeTimer = new DispatcherTimer { Interval = CloseDelay };
        bool unloaded;      // page replaced by about:blank while hidden
        bool webClosed;     // browser engine closed while hidden; recreated on show
        XboxGameBarWidgetActivity voiceActivity;   // tells Game Bar "voice chat in progress, don't idle-shutdown us"

        // Push-to-talk. Game Bar delivers these key presses even while dismissed. Map a controller back
        // button to any one of these keys (Armoury Crate / Command Center) and hold it to talk.
        // Single keys for mappers that offer them, plus Ctrl+Alt combos for Armoury Crate's "Combine Keys"
        // (its keyboard only goes up to F12).
        static readonly VirtualKey[][] PushToTalkCombos =
        {
            // Preferred: numpad keys. Armoury Crate offers them (Numpad tab), games almost never bind them,
            // and no modifier means nothing leaks into the game (Ctrl+Alt+M made games crouch / open menus).
            new[] { VirtualKey.Divide }, new[] { VirtualKey.Multiply }, new[] { VirtualKey.Subtract },
            new[] { VirtualKey.Add }, new[] { VirtualKey.Decimal },
            new[] { VirtualKey.F13 }, new[] { VirtualKey.F14 }, new[] { VirtualKey.F15 }, new[] { VirtualKey.F16 },
            new[] { VirtualKey.Pause }, new[] { VirtualKey.Scroll },
            new[] { VirtualKey.Control, VirtualKey.Menu, VirtualKey.M },
            new[] { VirtualKey.Control, VirtualKey.Menu, VirtualKey.V },
            new[] { VirtualKey.Control, VirtualKey.Menu, VirtualKey.T },
        };
        readonly List<XboxGameBarHotkeyWatcher> pttWatchers = new List<XboxGameBarHotkeyWatcher>();
        bool micLive;
        bool rtcLive;       // a WebRTC voice connection is open (in a voice channel, muted or not)
        bool callLive;      // Discord's page shows a voice-connected cue (DOM-based fallback signal)
        bool micOpen;       // the page holds an open microphone capture (muted or not) = in a call
        bool relaying;      // guard: we are clearing KeyBox ourselves, ignore that TextChanged
        bool composing;     // touch keyboard is mid-composition; relay only the final text
        bool keyboardUp;    // Windows reports the input pane as showing
        int kbRequest;      // serial for the fallback delay

        public MainPage()
        {
            InitializeComponent();
            suspendTimer.Tick += SuspendTimer_Tick;
            unloadTimer.Tick += UnloadTimer_Tick;
            voiceGoneTimer.Tick += VoiceGoneTimer_Tick;
            keepAliveTimer.Tick += KeepAliveTimer_Tick;
            keepAliveTimer.Start();
            closeTimer.Tick += (s, e) => CloseBrowser();
            var pane = InputPane.GetForCurrentView();
            pane.Showing += (s, a) => keyboardUp = true;
            pane.Hiding += InputPane_Hiding;
            StartAudioMonitor();
        }

        // Windows' own view of our audio session. "Muted" during a call means the Game Bar microphone
        // activity is gone (badge tapped, or dropped by Game Bar); the watchdog below re-requests it.
        Windows.Media.Audio.AudioStateMonitor captureMon, renderMon;

        void StartAudioMonitor()
        {
            try
            {
                captureMon = Windows.Media.Audio.AudioStateMonitor.CreateForCaptureMonitoring();
                captureMon.SoundLevelChanged += (s, a) => OnWindowsAudioLevel(s.SoundLevel);
                renderMon = Windows.Media.Audio.AudioStateMonitor.CreateForRenderMonitoring();
                renderMon.SoundLevelChanged += (s, a) => OnWindowsAudioLevel(s.SoundLevel);
            }
            catch (Exception ex) { PowerLog("audio monitor unavailable: " + ex.GetType().Name); }
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            current = this;
            widget = e.Parameter as XboxGameBarWidget;
            EnsureKeyHelper("start");
            if (widget != null)
            {
                widget.VisibleChanged += Widget_StateChanged;
                widget.WindowStateChanged += Widget_StateChanged;
                widget.PinnedChanged += Widget_StateChanged;
                widget.GameBarDisplayModeChanged += Widget_DisplayModeChanged;

                // Sizing is Game Bar's: the manifest max is large (like the Store browser widget) and we
                // never cap it at runtime, so Game Bar is free to grant the largest size that fits.
                TopBar.Visibility = Visibility.Visible;
                SetupPushToTalk();
                // XAML-side fallback: a paddle key that lands while a XAML control (KeyBox, buttons) has focus.
                Window.Current.CoreWindow.Dispatcher.AcceleratorKeyActivated += (s, a) =>
                {
                    if (!IsPttKey(a.VirtualKey)) return;
                    if (a.EventType == CoreAcceleratorKeyEventType.KeyDown || a.EventType == CoreAcceleratorKeyEventType.SystemKeyDown)
                    { a.Handled = true; if (!a.KeyStatus.WasKeyDown && AcceptKeyEdge(true, "xaml " + a.VirtualKey)) OnPushToTalk(true); }
                    else if (a.EventType == CoreAcceleratorKeyEventType.KeyUp || a.EventType == CoreAcceleratorKeyEventType.SystemKeyUp)
                    { a.Handled = true; if (AcceptKeyEdge(false, "xaml " + a.VirtualKey)) OnPushToTalk(false); }
                };
                // Resize requests only succeed once the window is up.
                Loaded += async (s, a) => await GoFullSizeAsync();
            }

            else
            {
                // Standalone (Start menu) run: a hidden window follows the same low-power path as a hidden widget.
                Window.Current.VisibilityChanged += (s, a) => { standaloneVisible = a.Visible; ApplyState(); };
            }

            Environment.SetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", BrowserArgs);
            await InitWebViewAsync();
            ApplyState();
        }

        // Creates the browser engine inside the current WebView2 control, applies settings and scripts,
        // and navigates to Discord. Called at startup and again after the close tier.
        async System.Threading.Tasks.Task InitWebViewAsync()
        {
            await Web.EnsureCoreWebView2Async();
            var core = Web.CoreWebView2;

            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsPinchZoomEnabled = false;          // no accidental zoom on touch
            core.Settings.IsSwipeNavigationEnabled = false;    // no back/forward swipes while scrolling chat
            core.Settings.IsReputationCheckingRequired = false; // no SmartScreen lookups per navigation
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
#if !DEBUG
            core.Settings.AreDevToolsEnabled = false;
#endif
            core.PermissionRequested += Core_PermissionRequested;
            core.IsDocumentPlayingAudioChanged += (s, a) => ApplyState();   // voice detection input
            core.NavigationCompleted += (s, a) =>
            {
                // Pages on the app host that aren't the app (terms, blog, safety...) get an escape hatch.
                bool offApp = Uri.TryCreate(s.Source, UriKind.Absolute, out var u) && u.Scheme == "https" && !IsAppPath(u);
                BackHomeButton.Visibility = offApp ? Visibility.Visible : Visibility.Collapsed;
            };
            core.NavigationStarting += Core_NavigationStarting;
            core.NewWindowRequested += Core_NewWindowRequested;
            core.WebMessageReceived += Core_WebMessageReceived;
            await core.AddScriptToExecuteOnDocumentCreatedAsync(MicWatchScript);
            await core.AddScriptToExecuteOnDocumentCreatedAsync(KeyboardWatchScript);

            // Tell the page the user prefers reduced motion. Discord honors it (with "sync with computer"),
            // which removes most of its continuous animations.
            try
            {
                await core.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia",
                    "{\"features\":[{\"name\":\"prefers-reduced-motion\",\"value\":\"reduce\"}]}");
            }
            catch (Exception) { }

            core.Navigate(Home);
        }


        public void Shutdown()
        {
            ReleaseVoiceActivity("shutdown");
            suspendTimer.Stop(); unloadTimer.Stop(); closeTimer.Stop();
            if (!webClosed) Web.Close();
        }

        // Close tier: tear the browser engine down. All msedgewebview2 helper processes exit.
        void CloseBrowser()
        {
            closeTimer.Stop();
            if (IsShown || InVoice || webClosed) return;
            suspendTimer.Stop(); unloadTimer.Stop();
            Web.Close();
            Root.Children.Remove(Web);
            webClosed = true;
            unloaded = false;
            PowerLog("closed browser engine");
        }

        async System.Threading.Tasks.Task RecreateBrowserAsync()
        {
            Web = new Microsoft.UI.Xaml.Controls.WebView2();
            Grid.SetRow(Web, 1);
            Root.Children.Insert(0, Web);
            PowerLog("recreating browser engine");
            await InitWebViewAsync();
            ApplyState();
        }

        bool standaloneVisible = true;

        bool IsShown => widget == null
            ? standaloneVisible
            : widget.Visible && widget.WindowState != XboxGameBarWidgetWindowState.Minimized;

        // ---- Window size -----------------------------------------------------

        static Size ScreenSize()
        {
            var di = DisplayInformation.GetForCurrentView();
            return new Size(di.ScreenWidthInRawPixels / di.RawPixelsPerViewPixel,
                            di.ScreenHeightInRawPixels / di.RawPixelsPerViewPixel);
        }

        // Ask Game Bar for the whole screen. It refuses a size that doesn't fit beside its home bar and the
        // widget's title bar, so step down until it accepts one, then center.
        async System.Threading.Tasks.Task GoFullSizeAsync()
        {
            if (widget == null) return;
            var s = ScreenSize();
            foreach (var f in new[] { 1.0, 0.94, 0.88, 0.82, 0.75, 0.68 })
            {
                var target = new Size(Math.Floor(s.Width * f), Math.Floor(s.Height * f));
                if (await widget.TryResizeWindowAsync(target))
                {
                    await widget.CenterWindowAsync();
                    return;
                }
            }
            var b = widget.WindowBounds;
            PowerLog($"full size refused by Game Bar (window is {b.Width:0}x{b.Height:0})");
        }

        async System.Threading.Tasks.Task ShowNoticeAsync(string text)
        {
            Notice.Text = text;
            NoticeHost.Visibility = Visibility.Visible;
            await System.Threading.Tasks.Task.Delay(2500);
            NoticeHost.Visibility = Visibility.Collapsed;
        }

        // ---- Power state -----------------------------------------------------

        bool InCall => callLive || rtcLive || micLive || micOpen;
        bool InVoice => InCall || (Web.CoreWebView2?.IsDocumentPlayingAudio ?? false);

        bool lastShown = true;

        async void Widget_StateChanged(XboxGameBarWidget sender, object args)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                ApplyState();
                bool shown = IsShown;
                if (shown != lastShown) { lastShown = shown; SetupPushToTalk(); }
            });
        }

        // Game Bar opening is the earliest hint the user may tap the widget: start bringing Discord back
        // now (pre-warm) so the reload cost is paid before the tap, not after.
        async void Widget_DisplayModeChanged(XboxGameBarWidget sender, object args)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (widget.GameBarDisplayMode == XboxGameBarDisplayMode.Foreground && !IsShown) PreWarm();
                ApplyState();
            });
        }

        void PreWarm()
        {
            if (webClosed) { webClosed = false; _ = RecreateBrowserAsync(); PowerLog("pre-warm: recreating engine"); return; }
            if (!unloaded) return;
            var core = Web.CoreWebView2;
            if (core == null) return;
            if (core.IsSuspended) core.Resume();
            unloaded = false;
            core.Navigate(Home);
            PowerLog("pre-warm: reloading Discord");
        }

        static bool IsPttKey(VirtualKey k) =>
            k == VirtualKey.Divide || k == VirtualKey.Multiply || k == VirtualKey.Subtract || k == VirtualKey.Add || k == VirtualKey.Decimal ||
            k == VirtualKey.F13 || k == VirtualKey.F14 || k == VirtualKey.F15 || k == VirtualKey.F16 || k == VirtualKey.Pause || k == VirtualKey.Scroll;

        void SetupPushToTalk()
        {
            // Re-registered on every show/hide transition in case Game Bar drops watchers silently.
            foreach (var w in pttWatchers) { try { w.Stop(); } catch (Exception) { } }
            pttWatchers.Clear();
            int ok = 0;
            foreach (var combo in PushToTalkCombos)
            {
                try
                {
                    var keys = string.Join("+", combo);
                    var watcher = XboxGameBarHotkeyWatcher.CreateWatcher(widget, new List<VirtualKey>(combo));
                    watcher.HotkeySetStateChanged += async (s, a) =>
                    {
                        await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => { if (AcceptKeyEdge(a.HotkeySetDown, keys)) OnPushToTalk(a.HotkeySetDown); });
                    };
                    watcher.Start();
                    pttWatchers.Add(watcher);
                    ok++;
                }
                catch (Exception ex) { PowerLog($"push-to-talk combo {string.Join("+", combo)} unavailable: {ex.GetType().Name}"); }
            }
            lastPttSetup = DateTime.UtcNow;
            if (ok < PushToTalkCombos.Length) PowerLog($"push-to-talk watchers registered: {ok}/{PushToTalkCombos.Length}");
        }

        // Paddle semantics, both at once:
        //   hold (released after TapWindow)  -> push-to-talk: live while held, muted on release
        //   tap  (released within TapWindow) -> toggle: stays live; the next tap mutes
        // Every change reads Discord's actual toggle state first, so it never double-flips.
        static readonly TimeSpan TapWindow = TimeSpan.FromMilliseconds(700);
        DateTime pttDownAt;
        bool pttOpenedByPress;   // this press opened the mic (so a long hold closes it on release)

        // No local memory of the mic state: every press reads Discord's real state and flips it, so muting
        // from Discord's own button or the corner button can't desync the paddle.
        // Serialized: one mic change at a time. A press that arrives while a change is in flight is queued
        // (one slot) and applied right after, so a quick second tap is never lost and never interleaves.
        bool pttBusy, pttQueued, pttCloseQueued;

        async void OnPushToTalk(bool down)
        {
            if (down)
            {
                pttDownAt = DateTime.UtcNow;
                if (pttBusy) { pttQueued = true; PowerLog("push-to-talk: press queued (previous change still applying)"); return; }
                pttBusy = true;
                try
                {
                    do
                    {
                        pttQueued = false; pttCloseQueued = false;
                        var state = await ReadMicStateAsync();
                        pttOpenedByPress = state != "live";
                        await SetMicStateAsync(pttOpenedByPress);   // muted -> open, live -> close
                        if (pttCloseQueued && pttOpenedByPress) { PowerLog("push-to-talk: queued hold release, closing"); await SetMicStateAsync(false); }
                    } while (pttQueued);
                }
                finally { pttBusy = false; pttQueued = false; pttCloseQueued = false; }
            }
            else
            {
                var held = DateTime.UtcNow - pttDownAt;
                if (pttOpenedByPress && held >= TapWindow)
                {
                    PowerLog($"push-to-talk: hold ({held.TotalMilliseconds:0} ms), closing");
                    if (pttBusy) { pttCloseQueued = true; return; }
                    pttBusy = true;
                    try { await SetMicStateAsync(false); } finally { pttBusy = false; }
                }
                else PowerLog($"push-to-talk: tap ({held.TotalMilliseconds:0} ms)");
            }
        }

        async System.Threading.Tasks.Task<string> ReadMicStateAsync()
        {
            var core = Web.CoreWebView2;
            if (core == null || webClosed || unloaded) return "none";
            try { return (await core.ExecuteScriptAsync("window.__dbMicState ? window.__dbMicState() : 'none'")).Trim('"'); }
            catch (Exception) { return "none"; }
        }

        const string MicOnGlyph = "";    // Segoe MDL2 "Microphone"
        const string MicOffGlyph = "";   // Segoe MDL2 "MicOff"

        void SetMicState(bool open) => _ = SetMicStateAsync(open);

        async System.Threading.Tasks.Task SetMicStateAsync(bool open)
        {
            if (webClosed || unloaded) { PowerLog("push-to-talk ignored: page not loaded"); return; }
            var core = Web.CoreWebView2;
            if (core == null) return;
            try
            {
                // ExecuteScript returns the Promise object, not its value; stash the result on window and read it back.
                await core.ExecuteScriptAsync("window.__dbLastMic = 'pending'; window.__dbSetMicState(" + (open ? "true" : "false") + ").then(r => { window.__dbLastMic = r; })");
                string result = "\"pending\"";
                for (int i = 0; i < 14 && result == "\"pending\""; i++)   // up to ~840 ms, usually done in one or two polls
                {
                    await System.Threading.Tasks.Task.Delay(60);
                    result = await core.ExecuteScriptAsync("window.__dbLastMic || 'pending'");
                }
                PowerLog($"push-to-talk {(open ? "open" : "close")} -> {result}");
                MicIcon.Glyph = open ? MicOnGlyph : MicOffGlyph;
            }
            catch (Exception ex) { PowerLog("push-to-talk script error: " + ex.GetType().Name); }
        }

        // On-widget mic button: same toggle as a paddle tap, and it refreshes the icon from Discord's real state.
        async void MicButton_Click(object sender, RoutedEventArgs e)
        {
            var core = Web.CoreWebView2;
            if (core == null || webClosed || unloaded) return;
            try
            {
                var state = await core.ExecuteScriptAsync("(() => { for (const b of document.querySelectorAll('button[aria-label][aria-checked]')) { if (/^(mute|unmute|turn (on|off) microphone)$/i.test(b.getAttribute('aria-label') || '')) return b.getAttribute('aria-checked') === 'true' ? 'muted' : 'live'; } return 'none'; })()");
                bool muted = state.Contains("muted");
                SetMicState(muted);
            }
            catch (Exception) { }
        }

        async void RefreshMicIcon()
        {
            var core = Web.CoreWebView2;
            if (core == null || webClosed || unloaded) return;
            try
            {
                var state = await core.ExecuteScriptAsync("(() => { for (const b of document.querySelectorAll('button[aria-label][aria-checked]')) { if (/^(mute|unmute|turn (on|off) microphone)$/i.test(b.getAttribute('aria-label') || '')) return b.getAttribute('aria-checked') === 'true' ? 'muted' : 'live'; } return 'none'; })()");
                MicIcon.Glyph = state.Contains("live") ? MicOnGlyph : MicOffGlyph;
            }
            catch (Exception) { }
        }

        // Game Bar idle-shutdown protection. While a call is live the widget holds a microphone activity;
        // Game Bar then keeps the widget alive with Game Bar dismissed and shows its mic indicator.
        // The MICROPHONE activity id is not cosmetic: it is how Game Bar lets Windows know the widget may keep
        // capturing audio while hidden (hidden voice worked on 0.6.0 with it and broke on 0.6.1 with a plain id).
        // Its on-screen mic badge is the price. Held while a voice connection is open, released on leaving.
        // Re-creating the id too soon throws (the old object must be finalized first) and Restart() after
        // Complete() fails on the Ally, so: drop the old object, collect, then create a fresh one.
        // Discord drops and re-creates its voice connection on reconnects, so "voice gone" is only believed
        // after VoiceGoneGrace. Game Bar refuses to START an activity while the widget is hidden ("parameter
        // is incorrect" on the Ally), so a failed start is retried whenever the widget becomes visible.
        static readonly TimeSpan VoiceGoneGrace = TimeSpan.FromSeconds(15);
        readonly DispatcherTimer voiceGoneTimer = new DispatcherTimer { Interval = VoiceGoneGrace };
        bool voiceActivityPending;

        // Keep-alive sentinel: every 20 s during a call, make sure the activity is held and Windows has not
        // muted our capture; otherwise re-request. (The badge was observed vanishing during idle stretches,
        // and a re-request while hidden does sometimes succeed, so keep trying rather than waiting for show.)
        readonly DispatcherTimer keepAliveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };

        DateTime lastPttSetup;
        DateTime lastKeySeen;

        void KeepAliveTimer_Tick(object sender, object e)
        {
            if (widget != null) EnsureKeyHelper("keep-alive: not connected");
            if (widget == null || !InCall) return;
            // Hotkey delivery has been seen to stop silently mid-call (keys resume only after focusing the
            // widget). Re-registering restored it before, so refresh the watchers every minute while in a call.
            if (DateTime.UtcNow - lastPttSetup > TimeSpan.FromSeconds(60) && DateTime.UtcNow - lastKeySeen > TimeSpan.FromSeconds(5))
                SetupPushToTalk();
            bool muted = false;
            try { muted = captureMon != null && captureMon.SoundLevel == Windows.Media.SoundLevel.Muted; } catch (Exception) { }
            if (voiceActivity != null && !muted) return;
            PowerLog($"keep-alive check: activity={(voiceActivity != null ? "held" : "none")} captureMuted={muted} -> re-requesting");
            if (voiceActivity != null) { try { voiceActivity.Complete(); } catch (Exception) { } voiceActivity = null; GC.Collect(); GC.WaitForPendingFinalizers(); }
            UpdateVoiceActivity();
        }

        public void ReleaseVoiceActivity(string why)
        {
            if (voiceActivity == null) return;
            try { voiceActivity.Complete(); PowerLog("voice activity completed (" + why + ")"); } catch (Exception) { }
            voiceActivity = null;
        }

        DateTime lastActivityFail;

        void UpdateVoiceActivity()
        {
            if (widget == null) return;
            bool wanted = InCall;
            if (wanted)
            {
                voiceGoneTimer.Stop();
                if (voiceActivity != null) return;
                // Game Bar refuses with "parameter is incorrect" while it still holds a previous instance of the
                // activity (e.g. the old process was killed by an upgrade). Hammering it doesn't help: retry at
                // most every 10 s and on each show.
                if (DateTime.UtcNow - lastActivityFail < TimeSpan.FromSeconds(10)) return;
                try
                {
                    voiceActivity = new XboxGameBarWidgetActivity(widget, XboxGameBarWidgetActivity.GetMicrophoneInputActivityId());
                    voiceActivityPending = false;
                    PowerLog("voice activity started (microphone, Game Bar keep-alive)");
                }
                catch (Exception ex)
                {
                    voiceActivityPending = true;
                    lastActivityFail = DateTime.UtcNow;
                    PowerLog($"voice activity start failed ({ex.GetType().Name}: {ex.Message.Replace("\r", " ").Replace("\n", " ").Trim()}); retry in 10 s / on show");
                }
            }
            else if (voiceActivity != null && !voiceGoneTimer.IsEnabled)
            {
                voiceGoneTimer.Start();   // confirm the call is really over before releasing the keep-alive
            }
        }

        // Watchdog: Windows muting our audio during a call means the Game Bar activity is gone (the user
        // tapped Game Bar's mic badge, or Game Bar dropped it). Drop the stale object and ask for a new one;
        // if Game Bar refuses while hidden, the pending flag retries as soon as the widget is shown.
        async void OnWindowsAudioLevel(Windows.Media.SoundLevel level)
        {
            if (level != Windows.Media.SoundLevel.Muted) return;
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (widget == null || !InCall || voiceActivity == null) return;
                PowerLog("voice activity lost (Windows muted us mid-call); re-requesting");
                try { voiceActivity.Complete(); } catch (Exception) { }
                voiceActivity = null;
                GC.Collect(); GC.WaitForPendingFinalizers();
                UpdateVoiceActivity();
            });
        }

        void VoiceGoneTimer_Tick(object sender, object e)
        {
            voiceGoneTimer.Stop();
            if (InCall || voiceActivity == null) return;
            try { voiceActivity.Complete(); } catch (Exception) { }
            voiceActivity = null;
            GC.Collect(); GC.WaitForPendingFinalizers();
            PowerLog("voice activity completed (voice gone for 15 s)");
        }

        void ApplyState()
        {
            UpdateVoiceActivity();
            if (webClosed)
            {
                if (IsShown) { webClosed = false; _ = RecreateBrowserAsync(); }
                return;
            }
            var core = Web.CoreWebView2;
            if (core == null) return;

            if (IsShown)
            {
                suspendTimer.Stop();
                unloadTimer.Stop();
                closeTimer.Stop();
                bool resumed = core.IsSuspended;
                if (resumed) core.Resume();
                if (unloaded) { unloaded = false; core.Navigate(Home); PowerLog("reloading Discord"); }
                // Pinned over a game (Game Bar dismissed): visible, but keep Chromium's caches small.
                bool pinnedOverGame = widget != null && widget.GameBarDisplayMode == XboxGameBarDisplayMode.PinnedOnly;
                core.MemoryUsageTargetLevel = pinnedOverGame
                    ? CoreWebView2MemoryUsageTargetLevel.Low
                    : CoreWebView2MemoryUsageTargetLevel.Normal;
                Web.Visibility = Visibility.Visible;
                _ = RunInPage("window.__dbSetHidden(false)");
                RefreshMicIcon();
                if (voiceActivityPending) { lastActivityFail = DateTime.MinValue; UpdateVoiceActivity(); }   // Game Bar accepts the start while visible
                PowerLog(resumed ? "shown (resumed from suspend)" : "shown");
            }
            else
            {
                CloseKeyboard();                       // never leave the touch keyboard over the game
                _ = RunInPage("window.__dbSetHidden(true)");
                if (InVoice)
                {
                    // In a call the page stays "visible" to the browser engine: Game Bar already keeps the
                    // window off screen, and a page Chromium considers hidden may stop delivering mic audio.
                    // Can't suspend a call, so use the "low memory target" tool on its own here.
                    // (Microsoft: don't combine it with TrySuspend; suspend/resume manage the level themselves.)
                    Web.Visibility = Visibility.Visible;
                    suspendTimer.Stop(); unloadTimer.Stop(); closeTimer.Stop();
                    core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
                    PowerLog("hidden, in voice: low memory, page kept visible, no suspend");
                }
                else if (!suspendTimer.IsEnabled && !unloadTimer.IsEnabled && !closeTimer.IsEnabled && !unloaded && !core.IsSuspended)
                {
                    // Hidden page = Chromium stops painting and throttles timers.
                    Web.Visibility = Visibility.Collapsed;
                    // Game Bar raises several events per transition; start the countdown once.
                    suspendTimer.Start(); unloadTimer.Start(); closeTimer.Start();
                    PowerLog($"hidden: low memory, suspend in {SuspendDelay.TotalSeconds:0}s, unload in {UnloadDelay.TotalMinutes:0}m, close in {CloseDelay.TotalMinutes:0}m");
                }
            }
        }

        async void UnloadTimer_Tick(object sender, object e)
        {
            unloadTimer.Stop();
            var core = Web.CoreWebView2;
            if (core == null || IsShown || InVoice || unloaded) return;
            if (core.IsSuspended) core.Resume();   // a suspended page can't navigate
            unloaded = true;
            core.Navigate("about:blank");
            PowerLog("unloaded page (about:blank)");
            await System.Threading.Tasks.Task.Delay(1500);
            if (!IsShown && !core.IsSuspended) { await core.TrySuspendAsync(); PowerLog("suspended (blank)"); }
        }

        async void SuspendTimer_Tick(object sender, object e)
        {
            suspendTimer.Stop();
            var core = Web.CoreWebView2;
            if (core == null || IsShown || InVoice || core.IsSuspended) return;
            bool ok = await core.TrySuspendAsync();
            PowerLog(ok ? "suspended" : "suspend refused by WebView2");
            // The suspend completes asynchronously; if the widget was reopened meanwhile, undo it at once.
            if (ok && IsShown && core.IsSuspended)
            {
                core.Resume();
                PowerLog("resumed (widget reopened during suspend)");
            }
        }

        // Small rolling log (64 KB) of state changes, push-to-talk results and crashes, for bug reports:
        //   %LOCALAPPDATA%\Packages\<pfn>\LocalState\power.log
        static readonly object logLock = new object();
        static void PowerLog(string what)
        {
            try
            {
                var path = System.IO.Path.Combine(ApplicationData.Current.LocalFolder.Path, "power.log");
                lock (logLock)
                {
                    var fi = new System.IO.FileInfo(path);
                    if (fi.Exists && fi.Length > 64 * 1024) fi.Delete();
                    System.IO.File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss}  {what}\r\n");
                }
            }
            catch (Exception) { }
        }

        void Core_WebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            var msg = args.TryGetWebMessageAsString();
            switch (msg)
            {
                case "mic:1": micLive = true; ApplyState(); break;
                case "mic:0": micLive = false; ApplyState(); break;
                case "rtc:1": rtcLive = true; ApplyState(); break;
                case "rtc:0": rtcLive = false; ApplyState(); break;
                case "call:1": callLive = true; ApplyState(); break;
                case "call:0": callLive = false; ApplyState(); break;
                case "micopen:1": micOpen = true; ApplyState(); break;
                case "micopen:0": micOpen = false; ApplyState(); break;
                case "kb:1": OpenKeyboard(); break;
                case string p when p.StartsWith("ptt:down:"): if (AcceptKeyEdge(true, "page " + p.Substring(9))) OnPushToTalk(true); break;
                case string p when p.StartsWith("ptt:up:"): if (AcceptKeyEdge(false, "page " + p.Substring(7))) OnPushToTalk(false); break;
                case "kb:0": CloseKeyboard(); break;
            }
        }

        // ---- Touch keyboard bridge ------------------------------------------

        // Xbox handheld guidance: ask for the gamepad keyboard view (CoreInputViewKind.Gamepad = 7, which
        // only exists in SDK 26100.3624+, so cast the value). Falls back to the default view.
        const CoreInputViewKind GamepadKeyboard = (CoreInputViewKind)7;

        // With the Game Bar proxy/stub registration in place WebView2 normally brings the keyboard up
        // itself, and typing goes straight into the page. The KeyBox relay is only a fallback: engage it
        // when, half a second after the page reported focus, no keyboard has appeared.
        async void OpenKeyboard()
        {
            int serial = ++kbRequest;
            await System.Threading.Tasks.Task.Delay(500);
            if (serial != kbRequest || keyboardUp) return;

            KeyBox.Focus(FocusState.Pointer);
            try
            {
                var view = CoreInputView.GetForCurrentView();
                if (!view.TryShow(GamepadKeyboard)) view.TryShow();
            }
            catch (Exception) { }
        }

        void CloseKeyboard()
        {
            kbRequest++;   // cancel a pending fallback
            if (FocusManager.GetFocusedElement() == KeyBox)
            {
                try { CoreInputView.GetForCurrentView().TryHide(); } catch (Exception) { }
                Web.Focus(FocusState.Programmatic);
            }
        }

        // User dismissed the keyboard: give focus back to the page so taps and the gamepad work.
        void InputPane_Hiding(InputPane sender, InputPaneVisibilityEventArgs args)
        {
            keyboardUp = false;
            if (FocusManager.GetFocusedElement() == KeyBox) Web.Focus(FocusState.Programmatic);
        }

        // Touch keyboards deliver suggestions/autocorrect as a composition; relay once, at the end.
        void KeyBox_CompositionStarted(TextBox sender, TextCompositionStartedEventArgs args) => composing = true;

        async void KeyBox_CompositionEnded(TextBox sender, TextCompositionEndedEventArgs args)
        {
            composing = false;
            await FlushKeyBox();
        }

        async System.Threading.Tasks.Task FlushKeyBox()
        {
            var text = KeyBox.Text;
            if (text.Length == 0) return;
            relaying = true;
            KeyBox.Text = string.Empty;
            relaying = false;
            await RunInPage("window.__dbInsert(" + JsString(text) + ")");
        }

        // Every character typed into the invisible box goes to the page, then the box is cleared.
        async void KeyBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (relaying || composing) return;
            await FlushKeyBox();
        }

        async void KeyBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            switch (e.Key)
            {
                case VirtualKey.Back:
                    if (KeyBox.Text.Length == 0) { e.Handled = true; await RunInPage("window.__dbBackspace()"); }
                    break;
                case VirtualKey.Enter:
                    e.Handled = true;
                    bool shift = (CoreWindow.GetForCurrentThread().GetKeyState(VirtualKey.Shift) & CoreVirtualKeyStates.Down) != 0;
                    await RunInPage("window.__dbEnter(" + (shift ? "true" : "false") + ")");
                    break;
                case VirtualKey.Escape:
                    e.Handled = true;
                    await RunInPage("window.__dbBlur()");
                    CloseKeyboard();
                    break;
            }
        }

        async System.Threading.Tasks.Task RunInPage(string script)
        {
            var core = Web.CoreWebView2;
            if (core == null) return;
            try { await core.ExecuteScriptAsync(script); } catch (Exception) { }
        }

        static string JsString(string s)
        {
            var sb = new StringBuilder(s.Length + 2).Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': break;
                    case (char)0x2028: sb.Append("\\u2028"); break;
                    case (char)0x2029: sb.Append("\\u2029"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        // ---- Navigation policy ------------------------------------------------

        // Mic for Discord only. Notifications denied so no toasts pop over the game.
        void Core_PermissionRequested(CoreWebView2 sender, CoreWebView2PermissionRequestedEventArgs args)
        {
            bool allow = args.PermissionKind == CoreWebView2PermissionKind.Microphone && IsDiscord(args.Uri);
            args.State = allow ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
        }

        // Keep the widget on Discord; anything else opens in the normal browser.
        async void Core_NavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
        {
            if (IsDiscord(args.Uri)) return;
            args.Cancel = true;
            await OpenExternal(args.Uri);
        }

        async void Core_NewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
        {
            args.Handled = true;
            if (IsDiscord(args.Uri)) sender.Navigate(args.Uri);
            else await OpenExternal(args.Uri);
        }

        static async System.Threading.Tasks.Task OpenExternal(string uri)
        {
            if (Uri.TryCreate(uri, UriKind.Absolute, out var u) && (u.Scheme == "https" || u.Scheme == "http"))
                await Launcher.LaunchUriAsync(u);
        }

        // Only the app itself stays inside the widget. Help center, status page, blog, dev portal etc.
        // live on other discord.com subdomains and have no way back, so they open in the browser.
        static readonly string[] AppHosts = { "discord.com", "ptb.discord.com", "canary.discord.com", "discordapp.com", "discord.gg" };

        static readonly string[] AppPaths = { "/app", "/channels", "/login", "/register", "/invite", "/oauth2", "/activity", "/store", "/library", "/discovery", "/guild-discovery", "/shop", "/quests", "/message-requests", "/settings", "/mfa", "/reset", "/verify", "/handoff", "/authorize", "/connections" };

        static bool IsAppPath(Uri u)
        {
            if (u.Host != "discord.com" && u.Host != "ptb.discord.com" && u.Host != "canary.discord.com") return true; // other allowed hosts (invites etc.) are fine
            var p = u.AbsolutePath;
            if (p == "/" ) return true;
            foreach (var ap in AppPaths) if (p == ap || p.StartsWith(ap + "/")) return true;
            return false;
        }

        void BackHomeButton_Click(object sender, RoutedEventArgs e)
        {
            BackHomeButton.Visibility = Visibility.Collapsed;
            Web.CoreWebView2?.Navigate(Home);
        }

        // Back: previous page if there is one, otherwise Discord's home. Discord is a single-page app, so
        // most in-app moves are history entries and GoBack works like the browser's back button.
        void BackButton_Click(object sender, RoutedEventArgs e)
        {
            var core = Web.CoreWebView2;
            if (core == null) return;
            if (core.CanGoBack) core.GoBack();
            else core.Navigate(Home);
        }

        static bool IsDiscord(string uri)
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var u)) return false;
            if (u.Scheme == "about" || u.Scheme == "data" || u.Scheme == "blob") return true;
            var h = u.Host.ToLowerInvariant();
            foreach (var d in AppHosts) if (h == d) return true;
            return false;
        }
    }
}