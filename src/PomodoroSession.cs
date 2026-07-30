namespace Loupedeck.PomodoroClockPlugin
{
    using System;

    public enum PomodoroState
    {
        // Idle: the button is just a clock.
        Clock,

        // The user is tapping out a duration; the countdown has not started yet.
        Setting,

        // Counting down.
        Running,

        // Counting down, but the clock is held.
        Paused,

        // Reached zero and is flashing for attention.
        Finished,
    }

    public readonly struct PomodoroSnapshot
    {
        public PomodoroSnapshot(PomodoroState state, TimeSpan remaining, TimeSpan duration,
                                Boolean cancelWindowOpen = false)
        {
            this.State = state;
            this.Remaining = remaining;
            this.Duration = duration;
            this.CancelWindowOpen = cancelWindowOpen;
        }

        public PomodoroState State { get; }

        // Time left on the clock. Zero unless a timer is set, running, paused or finished.
        public TimeSpan Remaining { get; }

        // The length the user dialled in, which the progress border is measured against.
        public TimeSpan Duration { get; }

        // True while a second press would cancel rather than resume. The face advertises this so the
        // shortcut is discoverable instead of something you have to already know about.
        public Boolean CancelWindowOpen { get; }

        // 1.0 when the timer has just started, 0.0 when it has run out.
        public Double Fraction =>
            this.Duration > TimeSpan.Zero
                ? Math.Clamp(this.Remaining.TotalSeconds / this.Duration.TotalSeconds, 0.0, 1.0)
                : 0.0;
    }

    /// <summary>
    /// The Pomodoro state machine, driven entirely by two inputs: presses and time passing.
    /// Deliberately free of any Loupedeck types so the behaviour can be reasoned about (and tested)
    /// on its own.
    /// </summary>
    public sealed class PomodoroSession
    {
        // Each press adds this much, wrapping back to one step once past the maximum.
        public static readonly TimeSpan Step = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(30);

        // How long the button waits after your last press before it commits and starts counting.
        public static readonly TimeSpan CommitDelay = TimeSpan.FromMilliseconds(1500);

        // A second press landing inside this window cancels instead of resuming. Generous on purpose:
        // you are reacting to the button changing to PAUSED, not tapping blind, and a physical press
        // on the touch panel is slower than a mouse double click.
        public static readonly TimeSpan DoublePressWindow = TimeSpan.FromMilliseconds(1200);

        // Stop flashing eventually, so a timer that finishes while you are away from the desk does
        // not blink at an empty room forever.
        public static readonly TimeSpan FinishedTimeout = TimeSpan.FromMinutes(2);

        private readonly Object _gate = new Object();

        private PomodoroState _state = PomodoroState.Clock;
        private TimeSpan _duration = TimeSpan.Zero;

        // Held while Running; the countdown is derived from this rather than decremented, so a
        // missed or late tick cannot make the timer drift.
        private DateTime _endsAtUtc;

        // Held while Paused or Finished, when there is no end time to count towards.
        private TimeSpan _frozenRemaining = TimeSpan.Zero;

        private DateTime _lastPressUtc = DateTime.MinValue;
        private DateTime _finishedAtUtc = DateTime.MinValue;

        public PomodoroSnapshot Snapshot(DateTime utcNow)
        {
            lock (this._gate)
            {
                var cancelWindowOpen = this._state == PomodoroState.Paused
                                       && utcNow - this._lastPressUtc <= DoublePressWindow;

                return new PomodoroSnapshot(this._state, this.RemainingAt(utcNow), this._duration,
                                            cancelWindowOpen);
            }
        }

        /// <summary>
        /// Routes a button press according to the current state. Returns true if the face changed.
        /// </summary>
        public Boolean Press(DateTime utcNow)
        {
            lock (this._gate)
            {
                var sinceLastPress = utcNow - this._lastPressUtc;
                this._lastPressUtc = utcNow;

                switch (this._state)
                {
                    case PomodoroState.Clock:
                        this._state = PomodoroState.Setting;
                        this._duration = Step;
                        return true;

                    case PomodoroState.Setting:
                        this._duration += Step;
                        if (this._duration > MaxDuration)
                        {
                            this._duration = Step;
                        }

                        return true;

                    case PomodoroState.Running:
                        this._frozenRemaining = this.RemainingAt(utcNow);
                        this._state = PomodoroState.Paused;
                        return true;

                    case PomodoroState.Paused:
                        // Pausing and immediately pressing again means "actually, forget it".
                        if (sinceLastPress <= DoublePressWindow)
                        {
                            this.ResetToClock();
                        }
                        else
                        {
                            this._endsAtUtc = utcNow + this._frozenRemaining;
                            this._state = PomodoroState.Running;
                        }

                        return true;

                    case PomodoroState.Finished:
                        this.ResetToClock();
                        return true;

                    default:
                        return false;
                }
            }
        }

        /// <summary>
        /// Advances any transition that happens on its own rather than on a press. Returns true if
        /// the state changed.
        /// </summary>
        public Boolean Tick(DateTime utcNow)
        {
            lock (this._gate)
            {
                switch (this._state)
                {
                    case PomodoroState.Setting:
                        if (utcNow - this._lastPressUtc < CommitDelay)
                        {
                            return false;
                        }

                        this._endsAtUtc = utcNow + this._duration;
                        this._state = PomodoroState.Running;
                        return true;

                    case PomodoroState.Running:
                        if (utcNow < this._endsAtUtc)
                        {
                            return false;
                        }

                        this._frozenRemaining = TimeSpan.Zero;
                        this._finishedAtUtc = utcNow;
                        this._state = PomodoroState.Finished;
                        return true;

                    case PomodoroState.Finished:
                        if (utcNow - this._finishedAtUtc < FinishedTimeout)
                        {
                            return false;
                        }

                        this.ResetToClock();
                        return true;

                    default:
                        return false;
                }
            }
        }

        private TimeSpan RemainingAt(DateTime utcNow)
        {
            switch (this._state)
            {
                // Nothing is ticking yet, so show the duration being dialled in.
                case PomodoroState.Setting:
                    return this._duration;

                case PomodoroState.Running:
                    var left = this._endsAtUtc - utcNow;
                    return left > TimeSpan.Zero ? left : TimeSpan.Zero;

                case PomodoroState.Paused:
                case PomodoroState.Finished:
                    return this._frozenRemaining;

                default:
                    return TimeSpan.Zero;
            }
        }

        private void ResetToClock()
        {
            this._state = PomodoroState.Clock;
            this._duration = TimeSpan.Zero;
            this._frozenRemaining = TimeSpan.Zero;
            this._endsAtUtc = DateTime.MinValue;
            this._finishedAtUtc = DateTime.MinValue;
        }
    }
}
