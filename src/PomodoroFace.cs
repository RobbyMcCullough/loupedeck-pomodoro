namespace Loupedeck.PomodoroClockPlugin
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using System.Runtime.InteropServices;

    /// <summary>
    /// Draws the button face. Every measurement is expressed against an 80 px reference button and
    /// scaled, so the same layout holds on the 60/90/116 px sizes the service asks for.
    /// </summary>
    internal static class PomodoroFace
    {
        private const Double ReferenceSize = 80.0;

        // A condensed grotesque: clock-like, and narrow enough that the time renders a good deal
        // larger in the same 80px than a default-width face manages. DIN Condensed ships with macOS
        // and Bahnschrift is the equivalent on Windows 10+; if neither resolves, the renderer quietly
        // falls back to the system default, which is a slightly plainer clock rather than a broken one.
        public static readonly String TimeFont =
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "DIN Condensed" : "Bahnschrift";

        // Captions stay in the default face: it is more legible than a condensed one at 11px.
        private const String CaptionFont = null;

        // BitmapBuilder.DrawText does not vertically centre text in the box it is given. It puts the
        // baseline at (y + height / 2 + BaselineDrop) and grows the glyphs upward from there, so the
        // taller the text the further it drifts towards the top of its box -- at 44px that is 19px of
        // drift on an 80px button. Both constants below were measured by rendering sweeps and reading
        // back the glyph bounding boxes; DrawCentred applies the correction.
        private const Double CapHeightRatio = 0.77;
        private const Double BaselineDrop = 6.5;

        // The small clock shown above a running countdown. It is secondary information, but at 15px in
        // dim grey it was the least legible thing on the button, so it gets a size and a little
        // breathing room under the border rather than sitting tight against it.
        private const Double ClockBandHeight = 22;
        private const Double ClockFontSize = 21;

        private static Int32 TopGap(BitmapBuilder bitmap) => Scale(bitmap, 3);

        // How long each half of the finished-state blink lasts.
        private static readonly Int64 FlashPeriodMs = 500;

        private static readonly BitmapColor Background = new BitmapColor(10, 10, 12);
        private static readonly BitmapColor AlarmBackground = new BitmapColor(200, 40, 40);

        private static readonly BitmapColor ClockOnly = new BitmapColor(236, 236, 240);
        // Lifted from 132 grey: still clearly secondary to the white countdown, but readable at a
        // glance rather than something you have to look for.
        private static readonly BitmapColor ClockSecondary = new BitmapColor(178, 178, 186);
        private static readonly BitmapColor Countdown = new BitmapColor(244, 244, 248);
        // Dimmed against the running countdown to read as held, but deliberately kept brighter than
        // ClockSecondary: the countdown is still the primary figure even when it is not moving.
        private static readonly BitmapColor CountdownPaused = new BitmapColor(202, 202, 210);
        private static readonly BitmapColor Status = new BitmapColor(112, 112, 120);

        private static readonly BitmapColor Setting = new BitmapColor(58, 150, 255);
        private static readonly BitmapColor PausedBorder = new BitmapColor(96, 96, 104);

        // Stops for the progress border as the timer drains.
        private static readonly BitmapColor Plenty = new BitmapColor(48, 200, 96);
        private static readonly BitmapColor Middling = new BitmapColor(240, 176, 48);
        private static readonly BitmapColor Nearly = new BitmapColor(232, 56, 56);

        public static void Draw(BitmapBuilder bitmap, PomodoroSnapshot snapshot, DateTime localNow,
                                Boolean use24Hour)
        {
            var alarming = snapshot.State == PomodoroState.Finished && IsFlashOn(localNow);

            bitmap.Clear(alarming ? AlarmBackground : Background);

            switch (snapshot.State)
            {
                case PomodoroState.Clock:
                    DrawClockOnly(bitmap, localNow, use24Hour);
                    break;

                case PomodoroState.Finished:
                    DrawFinished(bitmap, localNow, use24Hour, alarming);
                    break;

                default:
                    DrawCountdown(bitmap, snapshot, localNow, use24Hour);
                    break;
            }
        }

        /// <summary>
        /// A short string identifying everything currently visible. The command compares successive
        /// keys so it only asks the service to repaint when the face would actually differ.
        /// </summary>
        public static String RenderKey(PomodoroSnapshot snapshot, DateTime localNow)
        {
            // Minute of the day rather than the formatted string, so the key stays valid whichever
            // clock format a given button is configured for.
            var clock = (localNow.Hour * 60) + localNow.Minute;

            switch (snapshot.State)
            {
                case PomodoroState.Clock:
                    return $"C|{clock}";

                case PomodoroState.Setting:
                    return $"S|{clock}|{snapshot.Duration.TotalMinutes:F0}";

                case PomodoroState.Finished:
                    return $"F|{clock}|{(IsFlashOn(localNow) ? 1 : 0)}";

                default:
                    // Whole seconds: the border moves continuously but imperceptibly between ticks.
                    // The cancel flag matters too -- a paused clock is otherwise frozen, so without
                    // it the caption would never repaint when the window closes.
                    return $"{(snapshot.State == PomodoroState.Paused ? "P" : "R")}|{clock}|" +
                           $"{WholeSecondsLeft(snapshot.Remaining)}|{(snapshot.CancelWindowOpen ? 1 : 0)}";
            }
        }

        // Nothing competes with the time here, so it gets the whole button, centred. 44 is as large as
        // "15:47" goes in 80px in this face -- it starts clipping at 49 -- and width is the binding
        // constraint, not height.
        private static void DrawClockOnly(BitmapBuilder bitmap, DateTime localNow, Boolean use24Hour) =>
            DrawCentred(bitmap, FormatClock(localNow, use24Hour), 0, 0, bitmap.Width, bitmap.Height,
                        ClockOnly, Scale(bitmap, 44), TimeFont);

        private static void DrawFinished(BitmapBuilder bitmap, DateTime localNow, Boolean use24Hour,
                                         Boolean alarming)
        {
            // Same banding as a running timer, so the two states do not jump around relative to each
            // other when the countdown expires.
            var inset = BorderThickness(bitmap);
            var contentWidth = bitmap.Width - (2 * inset);
            var clockBand = Scale(bitmap, ClockBandHeight);

            var doneBand = bitmap.Height - (2 * inset) - TopGap(bitmap) - clockBand;

            DrawCentred(bitmap, "DONE", inset, inset + TopGap(bitmap), contentWidth, doneBand,
                        alarming ? BitmapColor.White : Nearly, Scale(bitmap, 30), TimeFont);
            DrawCentred(bitmap, FormatClock(localNow, use24Hour), inset, inset + TopGap(bitmap) + doneBand,
                        contentWidth, clockBand, alarming ? BitmapColor.White : ClockSecondary,
                        Scale(bitmap, ClockFontSize), TimeFont);
        }

        private static void DrawCountdown(BitmapBuilder bitmap, PomodoroSnapshot snapshot, DateTime localNow,
                                          Boolean use24Hour)
        {
            var setting = snapshot.State == PomodoroState.Setting;
            var paused = snapshot.State == PomodoroState.Paused;

            var thickness = BorderThickness(bitmap);

            // Inset by exactly the border, which is painted last and would otherwise slice the caption
            // in half along the bottom edge. No extra padding beyond that: the border reads better
            // thick and full-bleed, and the digits want every pixel that is left.
            var inset = thickness;
            var contentWidth = bitmap.Width - (2 * inset);

            // A caption only earns its space when it has something to say. Plainly running is the
            // common case and needs no words -- the border already shows how far along you are -- so
            // the countdown takes the whole button below the clock instead.
            var caption = setting ? "TAP +5"
                        : snapshot.CancelWindowOpen ? "TAP=CLEAR"
                        : paused ? "PAUSED"
                        : null;

            // The countdown leads and the wall clock supports it: while a session is running the time
            // left is what you are actually looking at.
            var clockBand = Scale(bitmap, ClockBandHeight);
            var statusBand = caption != null ? Scale(bitmap, 14) : 0;
            var countdownBand = bitmap.Height - (2 * inset) - TopGap(bitmap) - clockBand - statusBand;

            var countdownY = inset + TopGap(bitmap);
            var clockY = countdownY + countdownBand;
            var captionY = clockY + clockBand;

            // 35 is as large as "15:00" goes inside the border; it starts touching it at 39.
            DrawCentred(bitmap, FormatRemaining(snapshot.Remaining), inset, countdownY, contentWidth,
                        countdownBand, paused ? CountdownPaused : CountdownColor(snapshot),
                        Scale(bitmap, caption != null ? 28 : 35), TimeFont);

            DrawCentred(bitmap, FormatClock(localNow, use24Hour), inset, clockY, contentWidth,
                        clockBand, ClockSecondary, Scale(bitmap, ClockFontSize), TimeFont);

            if (caption != null)
            {
                var captionColor = setting ? Setting
                                 : snapshot.CancelWindowOpen ? Nearly
                                 : Status;

                DrawCentred(bitmap, caption, inset, captionY, contentWidth, statusBand, captionColor,
                            Scale(bitmap, 11), CaptionFont);
            }

            // While setting, the border sits full as a preview of what is about to drain.
            var fraction = setting ? 1.0 : snapshot.Fraction;
            var color = setting ? Setting
                      : paused ? PausedBorder
                      : ProgressColor(snapshot.Fraction);

            DrawRemainingBorder(bitmap, fraction, color, thickness);
        }

        /// <summary>
        /// Lays the remaining fraction of the timer around the edge of the button as a border that
        /// unwinds clockwise from twelve o'clock.
        /// </summary>
        private static void DrawRemainingBorder(BitmapBuilder bitmap, Double fraction, BitmapColor color, Int32 thickness)
        {
            var width = bitmap.Width;
            var height = bitmap.Height;
            var half = width / 2;

            var budget = (2.0 * width + 2.0 * height) * Math.Clamp(fraction, 0.0, 1.0);

            // Top edge, centre to right corner.
            var run = Take(ref budget, half);
            if (run > 0)
            {
                bitmap.FillRectangle(half, 0, run, thickness, color);
            }

            // Right edge, downwards.
            run = Take(ref budget, height);
            if (run > 0)
            {
                bitmap.FillRectangle(width - thickness, 0, thickness, run, color);
            }

            // Bottom edge, right to left.
            run = Take(ref budget, width);
            if (run > 0)
            {
                bitmap.FillRectangle(width - run, height - thickness, run, thickness, color);
            }

            // Left edge, upwards.
            run = Take(ref budget, height);
            if (run > 0)
            {
                bitmap.FillRectangle(0, height - run, thickness, run, color);
            }

            // Top edge, left corner back to centre.
            run = Take(ref budget, width - half);
            if (run > 0)
            {
                bitmap.FillRectangle(0, 0, run, thickness, color);
            }
        }

        // Consumes up to <paramref name="segmentLength"/> of the border budget and reports how many
        // pixels of this segment to draw.
        private static Int32 Take(ref Double budget, Int32 segmentLength)
        {
            var run = Math.Min(budget, segmentLength);
            budget -= run;
            return (Int32)Math.Ceiling(run);
        }

        // Stays green for the first half of the session, then warms through amber and finally goes
        // red for the last few minutes. Weighted this way so a glance reads as "plenty of time"
        // rather than drifting towards a warning colour almost immediately.
        private const Double AmberBelow = 0.5;
        private const Double RedBelow = 0.15;

        // The countdown digits step through three states rather than sweeping like the border does:
        // white while there is time, amber from halfway, red for the closing stretch.
        private const Double CountdownWarmBelow = 0.5;

        // "The last couple of minutes" cannot be purely absolute -- two minutes is 40% of a 5 minute
        // session -- so the red stage is whichever is shorter, two minutes or a fifth of the session.
        // A 30 minute timer turns red with 2:00 left, a 5 minute one with 1:00.
        private static readonly TimeSpan CountdownUrgentBelow = TimeSpan.FromMinutes(2);
        private const Double CountdownUrgentFractionCap = 0.2;

        private static BitmapColor CountdownColor(PomodoroSnapshot snapshot)
        {
            var urgentAt = TimeSpan.FromTicks(Math.Min(CountdownUrgentBelow.Ticks,
                                                       (Int64)(snapshot.Duration.Ticks * CountdownUrgentFractionCap)));

            if (snapshot.Remaining <= urgentAt)
            {
                return Nearly;
            }

            return snapshot.Fraction <= CountdownWarmBelow ? Middling : Countdown;
        }

        private static BitmapColor ProgressColor(Double fraction)
        {
            var f = Math.Clamp(fraction, 0.0, 1.0);

            if (f >= AmberBelow)
            {
                return Plenty;
            }

            return f >= RedBelow
                ? Lerp(Middling, Plenty, (f - RedBelow) / (AmberBelow - RedBelow))
                : Lerp(Nearly, Middling, f / RedBelow);
        }

        private static BitmapColor Lerp(BitmapColor from, BitmapColor to, Double t) =>
            new BitmapColor((Int32)Math.Round(from.R + ((to.R - from.R) * t)),
                            (Int32)Math.Round(from.G + ((to.G - from.G) * t)),
                            (Int32)Math.Round(from.B + ((to.B - from.B) * t)));

        /// <summary>
        /// Draws text genuinely centred, both ways, in the given box. Only sound for capitals and
        /// digits, which is all this face draws -- descenders would sit lower than this assumes.
        /// </summary>
        private static void DrawCentred(BitmapBuilder bitmap, String text, Int32 x, Int32 y, Int32 width,
                                        Int32 height, BitmapColor color, Int32 fontSize, String fontName)
        {
            // Move the box down by however far the glyphs sit above the centre of their own line box.
            var correction = (Int32)Math.Round((fontSize * CapHeightRatio / 2.0) - Scale(bitmap, BaselineDrop));

            bitmap.DrawText(text, x, y + correction, width, height, color, fontSize, 0, 0, fontName);
        }

        // Thick enough to read as a gauge from across the desk rather than as a hairline outline.
        private static Int32 BorderThickness(BitmapBuilder bitmap) => Math.Max(3, Scale(bitmap, 6));

        private static Int32 Scale(BitmapBuilder bitmap, Double referencePixels) =>
            (Int32)Math.Round(referencePixels * bitmap.Height / ReferenceSize);

        private static Boolean IsFlashOn(DateTime localNow) =>
            (localNow.Ticks / TimeSpan.TicksPerMillisecond / FlashPeriodMs) % 2 == 0;

        private static Int32 WholeSecondsLeft(TimeSpan remaining) =>
            remaining > TimeSpan.Zero ? (Int32)Math.Ceiling(remaining.TotalSeconds) : 0;

        private static String FormatRemaining(TimeSpan remaining)
        {
            // Ceiling, so a freshly started 25 minute timer reads 25:00 rather than 24:59.
            var seconds = WholeSecondsLeft(remaining);
            return $"{seconds / 60:D2}:{seconds % 60:D2}";
        }

        // AM/PM is dropped even in 12-hour mode: it costs a third of the width and the hour already
        // tells you which it is.
        private static String FormatClock(DateTime localNow, Boolean use24Hour) =>
            localNow.ToString(use24Hour ? "HH:mm" : "h:mm", CultureInfo.InvariantCulture);

        /// <summary>
        /// Set to true or false to pin the clock format regardless of what the operating system says.
        /// Left null -- the default -- the button follows the system: on macOS that is
        /// System Settings > General > Date &amp; Time > "24-hour time", on Windows the short time
        /// format in Region settings.
        /// </summary>
        private static readonly Boolean? ClockFormatOverride = null;

        private static readonly Object ClockFormatGate = new Object();
        private static readonly TimeSpan ClockFormatCacheTtl = TimeSpan.FromMinutes(5);
        private static Boolean? cachedSystem24Hour;
        private static DateTime cachedAtUtc = DateTime.MinValue;

        /// <summary>
        /// The format the button should actually draw in.
        /// </summary>
        public static Boolean Use24Hour => ClockFormatOverride ?? SystemUses24Hour;

        /// <summary>
        /// Whether this computer is set to a 24-hour clock.
        ///
        /// Cached, because on macOS answering this costs a subprocess. Five minutes is short enough
        /// that flipping the system switch takes effect on its own, without needing a plugin reload.
        /// </summary>
        public static Boolean SystemUses24Hour
        {
            get
            {
                lock (ClockFormatGate)
                {
                    if (cachedSystem24Hour.HasValue && DateTime.UtcNow - cachedAtUtc < ClockFormatCacheTtl)
                    {
                        return cachedSystem24Hour.Value;
                    }

                    // macOS keeps its "24-hour time" switch outside the locale, and .NET does not fold
                    // it into CultureInfo: a Mac set to 24-hour time still reports en-US with an
                    // "h:mm tt" pattern. So read the switch directly there, and fall back to the
                    // culture when it is unset -- which is also the right answer on Windows, where the
                    // preference genuinely does live in the short time pattern.
                    var resolved = TryReadMacOsForce24Hour(out var forced)
                        ? forced
                        : CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains("H");

                    cachedSystem24Hour = resolved;
                    cachedAtUtc = DateTime.UtcNow;
                    return resolved;
                }
            }
        }

        /// <summary>
        /// Reads macOS's AppleICUForce24HourTime preference. Returns false if the preference is not
        /// set, or on any other platform, in which case the caller should ask the culture instead.
        /// </summary>
        private static Boolean TryReadMacOsForce24Hour(out Boolean force24Hour)
        {
            force24Hour = false;

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return false;
            }

            try
            {
                var startInfo = new ProcessStartInfo("/usr/bin/defaults", "read -g AppleICUForce24HourTime")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        return false;
                    }

                    var output = process.StandardOutput.ReadToEnd().Trim();

                    if (!process.WaitForExit(2000))
                    {
                        return false;
                    }

                    // A non-zero exit means the key is simply absent, so the user has not overridden
                    // anything and the culture should decide.
                    if (process.ExitCode != 0)
                    {
                        return false;
                    }

                    force24Hour = output == "1" || output.Equals("true", StringComparison.OrdinalIgnoreCase);
                    return true;
                }
            }
            catch (Exception e)
            {
                PluginLog.Error(e, "Could not read the macOS 24-hour time preference");
                return false;
            }
        }
    }
}
