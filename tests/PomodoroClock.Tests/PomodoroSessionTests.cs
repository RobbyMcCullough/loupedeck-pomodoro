namespace Loupedeck.PomodoroClockPlugin.Tests
{
    using System;

    using Xunit;

    public class PomodoroSessionTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        // Presses land faster than the commit delay, so the session stays in Setting throughout.
        private static PomodoroSession Tapped(Int32 presses)
        {
            var session = new PomodoroSession();
            for (var i = 0; i < presses; i++)
            {
                session.Press(T0.AddMilliseconds(i * 200));
            }

            return session;
        }

        private static PomodoroSession Running(Int32 presses, out DateTime startedAt)
        {
            var session = Tapped(presses);
            startedAt = T0.Add(PomodoroSession.CommitDelay).AddSeconds(1);
            session.Tick(startedAt);
            return session;
        }

        [Theory]
        [InlineData(1, 5)]
        [InlineData(2, 10)]
        [InlineData(5, 25)]
        [InlineData(6, 30)]
        [InlineData(7, 5)]   // wraps back round past the maximum
        [InlineData(8, 10)]
        public void EachPressAddsFiveMinutesAndWrapsPastThirty(Int32 presses, Int32 expectedMinutes)
        {
            var snapshot = Tapped(presses).Snapshot(T0);

            Assert.Equal(PomodoroState.Setting, snapshot.State);
            Assert.Equal(expectedMinutes, snapshot.Duration.TotalMinutes, 3);
        }

        [Fact]
        public void StaysInSettingUntilTheCommitDelayHasElapsed()
        {
            var session = Tapped(1);
            var justBefore = T0.Add(PomodoroSession.CommitDelay).AddMilliseconds(-100);

            Assert.False(session.Tick(justBefore));
            Assert.Equal(PomodoroState.Setting, session.Snapshot(justBefore).State);
        }

        [Fact]
        public void StartsCountingDownOnceTheCommitDelayPasses()
        {
            var session = Running(1, out var startedAt);

            Assert.Equal(PomodoroState.Running, session.Snapshot(startedAt).State);
            Assert.Equal(5, session.Snapshot(startedAt).Remaining.TotalMinutes, 3);
        }

        [Fact]
        public void FractionTracksHowMuchOfTheTimerIsLeft()
        {
            var session = Running(5, out var startedAt);

            Assert.Equal(1.0, session.Snapshot(startedAt).Fraction, 2);
            Assert.Equal(0.5, session.Snapshot(startedAt.AddMinutes(12.5)).Fraction, 2);
            Assert.Equal(0.0, session.Snapshot(startedAt.AddMinutes(25)).Fraction, 2);
        }

        [Fact]
        public void FractionNeverGoesNegativeOnceTheTimerIsOverdue()
        {
            var session = Running(1, out var startedAt);
            var snapshot = session.Snapshot(startedAt.AddHours(1));

            Assert.Equal(0.0, snapshot.Fraction, 3);
            Assert.Equal(TimeSpan.Zero, snapshot.Remaining);
        }

        [Fact]
        public void PressingWhileRunningPausesAndFreezesTheClock()
        {
            var session = Running(2, out var startedAt);
            var pausedAt = startedAt.AddMinutes(3);

            session.Press(pausedAt);

            Assert.Equal(PomodoroState.Paused, session.Snapshot(pausedAt).State);
            Assert.Equal(7, session.Snapshot(pausedAt).Remaining.TotalMinutes, 3);

            // Time passing must not move a paused clock.
            Assert.Equal(7, session.Snapshot(pausedAt.AddMinutes(30)).Remaining.TotalMinutes, 3);
        }

        [Fact]
        public void PressingAfterTheDoublePressWindowResumesWithTheTimeThatWasLeft()
        {
            var session = Running(2, out var startedAt);
            var pausedAt = startedAt.AddMinutes(3);
            session.Press(pausedAt);

            var resumedAt = pausedAt.Add(PomodoroSession.DoublePressWindow).AddSeconds(5);
            session.Press(resumedAt);

            Assert.Equal(PomodoroState.Running, session.Snapshot(resumedAt).State);
            Assert.Equal(7, session.Snapshot(resumedAt).Remaining.TotalMinutes, 3);
        }

        [Fact]
        public void SecondPressInsideTheDoublePressWindowCancelsBackToTheClock()
        {
            var session = Running(2, out var startedAt);
            var pausedAt = startedAt.AddMinutes(3);

            session.Press(pausedAt);
            session.Press(pausedAt.AddMilliseconds(200));

            var snapshot = session.Snapshot(pausedAt.AddSeconds(1));
            Assert.Equal(PomodoroState.Clock, snapshot.State);
            Assert.Equal(TimeSpan.Zero, snapshot.Remaining);
        }

        [Fact]
        public void CancelWindowIsAdvertisedOnlyWhilePausedAndOnlyWhileItIsOpen()
        {
            var session = Running(2, out var startedAt);

            Assert.False(session.Snapshot(startedAt).CancelWindowOpen, "not while running");

            var pausedAt = startedAt.AddMinutes(3);
            session.Press(pausedAt);

            Assert.True(session.Snapshot(pausedAt).CancelWindowOpen, "immediately after pausing");
            Assert.True(session.Snapshot(pausedAt.AddMilliseconds(500)).CancelWindowOpen, "part way through");
            Assert.False(session.Snapshot(pausedAt.Add(PomodoroSession.DoublePressWindow).AddMilliseconds(1))
                                .CancelWindowOpen, "once the window has closed");
        }

        [Fact]
        public void CancelWindowIsWideEnoughToBeUsableByHand()
        {
            // A press landing three quarters of a second after the pause is a realistic double tap on
            // a touch panel and must still cancel rather than resume.
            Assert.True(PomodoroSession.DoublePressWindow >= TimeSpan.FromMilliseconds(750));
        }

        [Fact]
        public void ReachingZeroMovesToFinished()
        {
            var session = Running(1, out var startedAt);
            var expiredAt = startedAt.AddMinutes(5).AddSeconds(1);

            Assert.True(session.Tick(expiredAt));
            Assert.Equal(PomodoroState.Finished, session.Snapshot(expiredAt).State);
        }

        [Fact]
        public void PressingWhileFinishedDismissesBackToTheClock()
        {
            var session = Running(1, out var startedAt);
            var expiredAt = startedAt.AddMinutes(5).AddSeconds(1);
            session.Tick(expiredAt);

            session.Press(expiredAt.AddSeconds(2));

            Assert.Equal(PomodoroState.Clock, session.Snapshot(expiredAt.AddSeconds(2)).State);
        }

        [Fact]
        public void FinishedStateGivesUpFlashingAfterTheTimeout()
        {
            var session = Running(1, out var startedAt);
            var expiredAt = startedAt.AddMinutes(5).AddSeconds(1);
            session.Tick(expiredAt);

            var wellAfter = expiredAt.Add(PomodoroSession.FinishedTimeout).AddSeconds(1);
            Assert.True(session.Tick(wellAfter));
            Assert.Equal(PomodoroState.Clock, session.Snapshot(wellAfter).State);
        }

        [Fact]
        public void IdleClockReportsNoTimer()
        {
            var snapshot = new PomodoroSession().Snapshot(T0);

            Assert.Equal(PomodoroState.Clock, snapshot.State);
            Assert.Equal(TimeSpan.Zero, snapshot.Remaining);
            Assert.Equal(0.0, snapshot.Fraction, 3);
        }

        [Fact]
        public void TickDoesNothingWhileIdle()
        {
            var session = new PomodoroSession();

            Assert.False(session.Tick(T0.AddHours(1)));
            Assert.Equal(PomodoroState.Clock, session.Snapshot(T0.AddHours(1)).State);
        }
    }
}
