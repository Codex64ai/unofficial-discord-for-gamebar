using System;
using Microsoft.Gaming.XboxGameBar;
using Windows.ApplicationModel.Activation;
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
