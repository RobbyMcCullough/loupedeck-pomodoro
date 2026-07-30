namespace Loupedeck.PomodoroClockPlugin
{
    using System;
    using System.Threading;

    /// <summary>
    /// A clock that turns into a Pomodoro timer. Tap once for 5 minutes, again for 10, and so on up
    /// to 30; a second and a half after the last tap it starts counting down, with the button's border
    /// draining as the time goes.
    ///
    /// Deliberately a single action with no parameters and no Action Editor, so assigning it is one
    /// drag with nothing to fill in. The clock format follows the operating system; see
    /// PomodoroFace.ClockFormatOverride to pin it instead.
    /// </summary>
    public class PomodoroClockCommand : PluginDynamicCommand
    {
        // Fast enough that the finished-state flash and the commit delay feel immediate, while the
        // render-key check below keeps this from actually repainting more than once a second.
        private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(200);

        private readonly PomodoroSession _session = new PomodoroSession();

        private Timer _timer;
        private String _lastRenderKey;

        // Only used to report the gap between presses in the log.
        private DateTime _lastPressUtc = DateTime.MinValue;

        public PomodoroClockCommand()
            : base(displayName: "Pomodoro Clock",
                   description: "Shows the time. Tap to set a countdown in 5 minute steps, up to 30.",
                   groupName: "Pomodoro")
        {
        }

        protected override Boolean OnLoad()
        {
            this._timer = new Timer(this.OnTick, null, TickInterval, TickInterval);
            return base.OnLoad();
        }

        protected override Boolean OnUnload()
        {
            this._timer?.Dispose();
            this._timer = null;
            return base.OnUnload();
        }

        protected override void RunCommand(String actionParameter)
        {
            var utcNow = DateTime.UtcNow;

            var before = this._session.Snapshot(utcNow).State;
            this._session.Press(utcNow);
            var after = this._session.Snapshot(utcNow).State;

            // Logged because press timing is the one thing that cannot be checked away from the
            // hardware: it shows whether the device delivered both halves of a double press and how
            // far apart they landed. See plugin_logs/PomodoroClock.log.
            var gap = this._lastPressUtc == DateTime.MinValue
                ? "first"
                : $"{(utcNow - this._lastPressUtc).TotalMilliseconds:F0} ms since last";
            this._lastPressUtc = utcNow;

            PluginLog.Info($"Press: {before} -> {after} ({gap})");

            this.RedrawIfChanged();
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var utcNow = DateTime.UtcNow;
            using (var bitmap = new BitmapBuilder(imageSize))
            {
                PomodoroFace.Draw(bitmap, this._session.Snapshot(utcNow), utcNow.ToLocalTime(),
                                  PomodoroFace.Use24Hour);
                return bitmap.ToImage();
            }
        }

        // The face already draws the time. Returning nothing here stops the service printing the
        // action name over the top of it, which would cost most of the button for no information.
        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) =>
            String.Empty;

        private void OnTick(Object state)
        {
            try
            {
                this._session.Tick(DateTime.UtcNow);
                this.RedrawIfChanged();
            }
            catch (Exception e)
            {
                // A throw here would kill the timer thread and freeze the face at its last frame.
                PluginLog.Error(e, "Pomodoro tick failed");
            }
        }

        /// <summary>
        /// Asks the service to repaint only when the face would look different. ActionImageChanged
        /// redraws every button currently on the device, so calling it on every 200 ms tick would
        /// have the whole console re-rendering continuously for no visible gain.
        /// </summary>
        private void RedrawIfChanged()
        {
            var utcNow = DateTime.UtcNow;
            var key = PomodoroFace.RenderKey(this._session.Snapshot(utcNow), utcNow.ToLocalTime());

            if (key != Interlocked.Exchange(ref this._lastRenderKey, key))
            {
                this.ActionImageChanged();
            }
        }
    }
}
