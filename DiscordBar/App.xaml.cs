using System;
using Microsoft.Gaming.XboxGameBar;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.AppService;
using Windows.ApplicationModel.Background;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace DiscordBar
{
    sealed partial class App : Application
    {
        XboxGameBarWidget widget;

        public App()
        {
            InitializeComponent();
            // Any crash goes to power.log next to the state transitions, so a crash report is one command away.
            UnhandledException += (s, e) =>
            {
                try
                {
                    var path = System.IO.Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "power.log");
                    System.IO.File.AppendAllText(path, $"{System.DateTime.Now:HH:mm:ss}  CRASH {e.Exception?.GetType().Name}: {e.Message}\r\n{e.Exception}\r\n");
                }
                catch (System.Exception) { }
            };
        }

        // Game Bar launches the widget through the ms-gamebarwidget protocol.
        protected override void OnActivated(IActivatedEventArgs args)
        {
            if (args.Kind != ActivationKind.Protocol) return;
            if (!(args is IProtocolActivatedEventArgs protocolArgs) || protocolArgs.Uri.Scheme != "ms-gamebarwidget") return;
            if (!(args is XboxGameBarWidgetActivatedEventArgs widgetArgs) || !widgetArgs.IsLaunchActivation) return;

            var frame = new Frame();
            Window.Current.Content = frame;
            widget = new XboxGameBarWidget(widgetArgs, Window.Current.CoreWindow, frame);
            frame.Navigate(typeof(MainPage), widget);

            Window.Current.Closed += (s, e) =>
            {
                (frame.Content as MainPage)?.Shutdown();
                widget = null;
            };
            // Process being suspended/terminated (upgrade install, Game Bar closing the widget): give the
            // microphone activity back first, or Game Bar keeps it and refuses the next one with
            // "The parameter is incorrect" until Game Bar itself restarts.
            Suspending += (s, e) =>
            {
                var d = e.SuspendingOperation.GetDeferral();
                try { (frame.Content as MainPage)?.ReleaseVoiceActivity("app suspending"); } catch (System.Exception) { }
                d.Complete();
            };
            Window.Current.Activate();
        }

        // The key helper connects here (same package, in-process app service). Every message is one key edge.
        BackgroundTaskDeferral keyServiceDeferral;
        AppServiceConnection keyConnection;   // held on purpose: an unreferenced connection gets collected and goes silent
        bool pingSeen;
        public static bool KeyHelperConnected { get; private set; }

        protected override void OnBackgroundActivated(BackgroundActivatedEventArgs args)
        {
            base.OnBackgroundActivated(args);
            if (!(args.TaskInstance.TriggerDetails is AppServiceTriggerDetails details) || details.Name != "com.discordbar.keys") return;
            if (!MainPage.HasUi) { MainPage.Log("key helper connection refused: no widget UI in this process"); return; }
            keyServiceDeferral?.Complete();
            keyServiceDeferral = args.TaskInstance.GetDeferral();
            var connection = details.AppServiceConnection;
            keyConnection = connection;
            args.TaskInstance.Canceled += (s, e) =>
            {
                if (keyConnection == connection) { KeyHelperConnected = false; keyConnection = null; }
                MainPage.Log("key helper service canceled: " + e);
                keyServiceDeferral?.Complete(); keyServiceDeferral = null;
            };
            connection.ServiceClosed += (s, e) =>
            {
                if (keyConnection == connection) { KeyHelperConnected = false; keyConnection = null; }
                MainPage.Log("key helper disconnected: " + e.Status);
            };
            connection.RequestReceived += async (s, e) =>
            {
                var d = e.GetDeferral();
                try
                {
                    var m = e.Request.Message;
                    if (m.ContainsKey("ping"))
                    {
                        bool hooked = m.ContainsKey("hook") && (bool)m["hook"];
                        if (!hooked || !pingSeen) MainPage.Log($"key helper ping: hook={hooked} rehooks={(m.ContainsKey("rehooks") ? m["rehooks"] : "?")} failures={(m.ContainsKey("hookFailures") ? m["hookFailures"] : "?")}");
                        pingSeen = true;
                        await e.Request.SendResponseAsync(new Windows.Foundation.Collections.ValueSet { { "ok", true } });
                        return;
                    }
                    string key = m.ContainsKey("key") ? m["key"] as string : null;
                    bool down = m.ContainsKey("down") && (bool)m["down"];
                    bool delivered = key != null && MainPage.OnHelperKey(key, down);
                    await e.Request.SendResponseAsync(new Windows.Foundation.Collections.ValueSet { { "ok", delivered } });
                }
                catch (System.Exception ex) { MainPage.Log("key helper message error: " + ex.GetType().Name); }
                finally { d.Complete(); }
            };
            KeyHelperConnected = true;
            pingSeen = false;
            MainPage.Log("key helper connected");
        }

        public static async void LaunchKeyHelper(string why)
        {
            try
            {
                await FullTrustProcessLauncher.LaunchFullTrustProcessForCurrentAppAsync();
                MainPage.Log("key helper launched (" + why + ")");
            }
            catch (System.Exception ex) { MainPage.Log("key helper launch failed (" + why + "): " + ex.GetType().Name + " " + ex.Message); }
        }

        // Started from the Start menu instead of Game Bar: run as a normal window (handy for testing).
        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            if (!(Window.Current.Content is Frame frame))
            {
                frame = new Frame();
                Window.Current.Content = frame;
            }
            if (frame.Content == null) frame.Navigate(typeof(MainPage), null);
            Window.Current.Activate();
        }
    }
}
