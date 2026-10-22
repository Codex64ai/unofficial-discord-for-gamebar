// DiscordBar key helper.
//
// Why this exists: a Game Bar widget's only key path is XboxGameBarHotkeyWatcher, and on the ROG Ally it
// delivers only while Game Bar is open or for a short while after it closes; once Game Bar dozes, paddle
// presses vanish until Game Bar is opened again. This tiny full-trust process runs beside the widget,
// hooks the keyboard itself (WH_KEYBOARD_LL, the same thing Game Bar does), forwards the push-to-talk keys
// to the widget through the package's app service, and swallows them so a paddle mapped to Numpad * never
// types a '*' into a game or chat box.
//
// Lifetime: launched by the widget (FullTrustProcessLauncher). Single instance (named mutex). Exits when
// the widget has been gone for about a minute. Idle cost: a message loop waiting in GetMessage, 0% CPU.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.AppService;
using Windows.Foundation;
using Windows.Foundation.Collections;

namespace DiscordBar.KeyHelper
{
    static class Program
    {
        const string ServiceName = "com.discordbar.keys";

        const int WH_KEYBOARD_LL = 13;
        const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
        const uint LLKHF_INJECTED = 0x10;

        // Virtual-key -> name the widget understands. Numpad operators, F13-F16, Pause, Scroll Lock.
        static readonly Dictionary<int, string> Keys = new Dictionary<int, string>
        {
            { 0x6A, "Multiply" }, { 0x6F, "Divide" }, { 0x6D, "Subtract" }, { 0x6B, "Add" }, { 0x6E, "Decimal" },
            { 0x7C, "F13" }, { 0x7D, "F14" }, { 0x7E, "F15" }, { 0x7F, "F16" },
            { 0x13, "Pause" }, { 0x91, "Scroll" },
        };
        static readonly HashSet<int> held = new HashSet<int>();

        delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
        static HookProc hookProc;          // kept alive: the OS holds a raw pointer to it
        static IntPtr hook;
        static AppServiceConnection conn;  // non-null only while the widget is connected
        static volatile bool connected;
        static SemaphoreSlim closed;        // released when the current connection is closed or found dead
        static uint mainThread;
        static readonly ConcurrentQueue<KeyValuePair<string, bool>> queue = new ConcurrentQueue<KeyValuePair<string, bool>>();
        static readonly AutoResetEvent queued = new AutoResetEvent(false);
        static int rehooks, hookFailures;
        const uint WM_REHOOK = 0x8000 + 1;   // WM_APP + 1: re-install the hook on the hook thread

        [StructLayout(LayoutKind.Sequential)]
        struct KBDLLHOOKSTRUCT { public uint vkCode; public uint scanCode; public uint flags; public uint time; public UIntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)]
        struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int x; public int y; }

        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc fn, IntPtr hMod, uint threadId);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);
        [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG msg);
        [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG msg);
        [DllImport("user32.dll")] static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        const uint WM_QUIT = 0x0012;

        [STAThread]
        static void Main()
        {
            // Newest helper wins: an older one (previous package version, or one holding a dead link) would keep
            // the key swallowed and block this one through the mutex, so retire it first.
            int me = Process.GetCurrentProcess().Id;
            foreach (var p in Process.GetProcessesByName("DiscordBar.KeyHelper"))
            {
                if (p.Id == me) continue;
                try { p.Kill(); p.WaitForExit(3000); } catch (Exception) { }
            }
            bool first;
            using (new Mutex(true, @"Local\DiscordBar.KeyHelper", out first))
            {
                if (!first) return;   // lost a race with another fresh helper: it does the same job
                mainThread = GetCurrentThreadId();
                hookProc = OnKey;
                hook = SetWindowsHookEx(WH_KEYBOARD_LL, hookProc, GetModuleHandle(null), 0);
                if (hook == IntPtr.Zero) return;
                var sender = new Thread(SendLoop) { IsBackground = true, Name = "send" };
                sender.Start();
                Task.Run(ConnectLoop);
                Task.Run(Watchdog);
                MSG msg;
                while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    if (msg.message == WM_REHOOK) { Rehook(); continue; }
                    TranslateMessage(ref msg); DispatchMessage(ref msg);
                }
                UnhookWindowsHookEx(hook);
            }
        }

        // Runs on the hook thread inside the input pipeline: do nothing but classify and enqueue. Windows drops
        // a low-level hook that stalls, so no cross-process calls here.
        static IntPtr OnKey(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && connected)
            {
                var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                string name;
                if (Keys.TryGetValue((int)k.vkCode, out name))
                {
                    int m = (int)wParam;
                    bool down = m == WM_KEYDOWN || m == WM_SYSKEYDOWN;
                    bool up = m == WM_KEYUP || m == WM_SYSKEYUP;
                    if (down && held.Add((int)k.vkCode)) { queue.Enqueue(new KeyValuePair<string, bool>(name, true)); queued.Set(); }   // first down only (auto-repeat dropped)
                    else if (up && held.Remove((int)k.vkCode)) { queue.Enqueue(new KeyValuePair<string, bool>(name, false)); queued.Set(); }
                    return (IntPtr)1;   // swallowed: nobody else (games, chat box) sees the paddle key
                }
            }
            return CallNextHookEx(hook, nCode, wParam, lParam);
        }

        static void SendLoop()
        {
            while (true)
            {
                queued.WaitOne();
                KeyValuePair<string, bool> e;
                while (queue.TryDequeue(out e)) Send(e.Key, e.Value);
            }
        }

        static void Rehook()
        {
            try { if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook); } catch (Exception) { }
            hook = SetWindowsHookEx(WH_KEYBOARD_LL, hookProc, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) hookFailures++; else rehooks++;
        }

        // Every 30 s: refresh the hook (a hook Windows silently removed comes back) and ping the widget so a dead
        // link is noticed even when no key is pressed, and the widget's log shows the helper is alive and hooked.
        static async Task Watchdog()
        {
            while (true)
            {
                await Task.Delay(30000);
                PostThreadMessage(mainThread, WM_REHOOK, IntPtr.Zero, IntPtr.Zero);
                await Task.Delay(500);
                var c = conn;
                if (c == null) continue;
                try
                {
                    var vs = new ValueSet { { "ping", true }, { "hook", hook != IntPtr.Zero }, { "rehooks", rehooks }, { "hookFailures", hookFailures } };
                    var r = await c.SendMessageAsync(vs);
                    if (r.Status != AppServiceResponseStatus.Success || r.Message == null) Dead(c);
                }
                catch (Exception) { Dead(c); }
            }
        }

        static void Send(string key, bool down)
        {
            var c = conn;
            if (c == null) return;
            var vs = new ValueSet { { "key", key }, { "down", down }, { "t", Environment.TickCount } };
            try
            {
                // Fire from the hook thread, verify on completion: the widget answers every message, so a
                // missing/failed answer means the link is dead (widget restarted, connection collected) and we
                // must stop swallowing keys and reconnect.
                var op = c.SendMessageAsync(vs);
                op.Completed = (a, st) =>
                {
                    bool ok = false;
                    try { ok = st == AsyncStatus.Completed && a.GetResults().Status == AppServiceResponseStatus.Success && a.GetResults().Message != null; }
                    catch (Exception) { }
                    if (!ok) Dead(c);
                };
            }
            catch (Exception) { Dead(c); }
        }

        static void Dead(AppServiceConnection c)
        {
            if (conn != c) return;
            connected = false; conn = null;
            try { closed?.Release(); } catch (Exception) { }
        }

        static bool WidgetRunning()
        {
            try { return Process.GetProcessesByName("DiscordBar").Length > 0; } catch (Exception) { return false; }
        }

        static async Task ConnectLoop()
        {
            int failures = 0;
            while (true)
            {
                // Opening the app service would start the widget's process if it isn't running (background
                // activation). Never do that: only connect to a widget that is already up, and leave ~12 s after
                // the last one is gone (the widget launches a fresh helper when it comes back).
                if (!WidgetRunning())
                {
                    if (++failures >= 4) break;
                    await Task.Delay(3000);
                    continue;
                }
                var c = new AppServiceConnection { AppServiceName = ServiceName, PackageFamilyName = Package.Current.Id.FamilyName };
                var gate = new SemaphoreSlim(0);
                closed = gate;
                c.ServiceClosed += (s, e) => { try { gate.Release(); } catch (Exception) { } };
                AppServiceConnectionStatus status;
                try { status = await c.OpenAsync(); } catch (Exception) { status = AppServiceConnectionStatus.Unknown; }
                if (status == AppServiceConnectionStatus.Success)
                {
                    int opened = Environment.TickCount;
                    conn = c; connected = true;
                    await gate.WaitAsync();            // widget went away (closed, upgraded, crashed) or link found dead
                    connected = false; conn = null;
                    try { c.Dispose(); } catch (Exception) { }
                    held.Clear();
                    // A link that died within 3 s was refused (a widget process without UI, e.g. one Windows
                    // started just to answer us): back off instead of re-activating it every second.
                    if (Environment.TickCount - opened < 3000) { if (++failures >= 20) break; await Task.Delay(5000); }
                    else { failures = 0; await Task.Delay(1000); }
                }
                else
                {
                    try { c.Dispose(); } catch (Exception) { }
                    if (++failures >= 20) break;      // ~1 min without a widget: leave
                    await Task.Delay(3000);
                }
            }
            PostThreadMessage(mainThread, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }
    }
}
