using System;
using System.Diagnostics;
using System.Threading;
using GitTabSync.Sync;
using GitTabSync.Tests.TestSupport;
using Xunit;

namespace GitTabSync.Tests
{
    /// <summary>
    /// Timing behaviour of the capture side: how long the editor is left alone before it is read,
    /// and what is allowed to skip that wait.
    /// </summary>
    /// <remarks>
    /// These run against real timers rather than an injected clock. The thing under test *is*
    /// elapsed time, and a fake clock would prove only that the arithmetic is right.
    /// <para>
    /// The assertions are one-sided so a loaded machine is allowed to be slow but never reported as
    /// wrong, and "one-sided" has a specific meaning here. "Eventually" waits far longer than it
    /// should ever need. "Not before" is <em>timed</em> — the moment something happened is compared
    /// against the delay — and never <em>sampled</em>, because sampling asks "has it happened yet?"
    /// from a thread the scheduler can deschedule past the entire delay, and then reports a late
    /// test as an early capture. Where a test cannot be phrased as a measurement, its margin is
    /// made wide enough that a stall of that length would be absurd, and says so.
    /// </para>
    /// </remarks>
    public sealed class CaptureSchedulerTests
    {
        /// <summary>Long enough that an early capture cannot be mistaken for a slow one.</summary>
        private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(400);

        /// <summary>Well inside <see cref="Delay"/>: nothing may have happened yet.</summary>
        private static readonly TimeSpan FarTooEarly = TimeSpan.FromMilliseconds(80);

        /// <summary>Generous, because a busy CI machine is allowed to be slow, not wrong.</summary>
        private static readonly TimeSpan Eventually = TimeSpan.FromSeconds(10);

        /// <summary>
        /// How far ahead of its due time a .NET timer is allowed to fire. The platform's tick is
        /// about 15.6 ms; twice that is slack enough that rounding never fails a "not before"
        /// assertion, and far too little to hide a wait that did not happen.
        /// </summary>
        private static readonly TimeSpan TimerGranularity = TimeSpan.FromMilliseconds(32);

        [Fact]
        public void The_documented_delays_are_what_the_code_actually_uses()
        {
            // The README and CLAUDE.md quote these numbers, and the whole point of the branch
            // debounce is that it outlasts the several writes one checkout makes to HEAD. If a
            // number changes, the prose has to change with it.
            Assert.Equal(TimeSpan.FromMilliseconds(300), CaptureScheduler.DefaultDelay);
            Assert.Equal(TimeSpan.FromMilliseconds(250), Git.BranchMonitor.DefaultDebounce);
            Assert.Equal(TimeSpan.FromSeconds(5), Git.BranchMonitor.DefaultPollInterval);
        }

        [Fact]
        public void A_scheduled_capture_does_not_run_straight_away()
        {
            var captures = new Counter();
            using var scheduler = new CaptureScheduler(captures.Increment, Delay);

            var startedAt = Stopwatch.GetTimestamp();
            scheduler.Schedule();

            Assert.True(captures.WaitForAtLeast(1, Eventually), "The scheduled capture never ran.");

            // Reading the editor the instant a document event arrives is the bug the delay exists
            // to prevent: the document is still half-open at that point.
            //
            // Timed at the moment the capture ran rather than sampled part-way through the delay.
            // Sampling asks "has it happened yet?" from a thread that a loaded machine can
            // deschedule past the whole delay, which reports a late test as an early capture — the
            // one way these tests are not allowed to be wrong. A stall can only push this
            // measurement up, so it fails only if the capture really was early.
            Assert.True(
                captures.FirstRanAfter(startedAt) >= Delay - TimerGranularity,
                "The capture ran " + captures.FirstRanAfter(startedAt).TotalMilliseconds.ToString("F0")
                    + " ms in, inside the " + Delay.TotalMilliseconds.ToString("F0") + " ms delay.");
        }

        [Fact]
        public void A_burst_of_schedules_collapses_into_one_capture()
        {
            var captures = new Counter();
            using var scheduler = new CaptureScheduler(captures.Increment, Delay);

            // Opening one document raises a handful of running-document-table events in a row.
            for (var i = 0; i < 20; i++)
            {
                scheduler.Schedule();
            }

            Assert.True(captures.WaitForAtLeast(1, Eventually), "The scheduled capture never ran.");

            // Long enough that a second, third or twentieth capture would have shown up.
            Thread.Sleep(Delay + Delay);
            Assert.Equal(1, captures.Count);
        }

        [Fact]
        public void Each_schedule_pushes_the_capture_further_out()
        {
            var captures = new Counter();

            // A much longer delay than the other tests use, and it costs nothing: this test never
            // waits for the delay to elapse, it only shows that it does not. The margin is the
            // point — the assertion is "nothing captured while events kept arriving", which a
            // machine that stalls for longer than the delay would break by being slow rather than
            // by being wrong. Five 80 ms gaps cannot stretch past four seconds.
            var delay = TimeSpan.FromSeconds(4);
            using var scheduler = new CaptureScheduler(captures.Increment, delay);

            // Events arriving steadily, none of them further apart than the delay: the editor is
            // never quiet, so it is never read.
            for (var i = 0; i < 5; i++)
            {
                scheduler.Schedule();
                Thread.Sleep(FarTooEarly);
            }

            Assert.Equal(0, captures.Count);
        }

        [Fact]
        public void CaptureNow_does_not_wait_for_the_delay()
        {
            var captures = new Counter();
            using var scheduler = new CaptureScheduler(captures.Increment, TimeSpan.FromMinutes(5));

            scheduler.CaptureNow();

            // This is the pin path. A pinned tab is reported by one command, after the fact, and
            // waiting is precisely what loses the pin to a branch switch made a moment later.
            Assert.Equal(1, captures.Count);
        }

        [Fact]
        public void CaptureNow_drops_a_pending_scheduled_capture()
        {
            var captures = new Counter();
            using var scheduler = new CaptureScheduler(captures.Increment, Delay);

            scheduler.Schedule();
            scheduler.CaptureNow();

            Assert.Equal(1, captures.Count);

            // What the pending capture would have read has just been read.
            Thread.Sleep(Delay + Delay);
            Assert.Equal(1, captures.Count);
        }

        [Fact]
        public void A_capture_that_throws_does_not_escape_the_timer_thread()
        {
            var log = new RecordingTabSyncLog();
            var attempts = new Counter();
            using var scheduler = new CaptureScheduler(
                () =>
                {
                    attempts.Increment();
                    throw new InvalidOperationException("the editor said no");
                },
                Delay,
                log);

            scheduler.Schedule();

            Assert.True(attempts.WaitForAtLeast(1, Eventually), "The scheduled capture never ran.");

            // An exception escaping a timer callback takes the whole process down — Visual Studio,
            // with the user's unsaved work in it. Reaching this line at all is most of the test.
            Thread.Sleep(FarTooEarly);
            Assert.Single(log.Errors);
            Assert.IsType<InvalidOperationException>(log.Errors[0]);
        }

        [Fact]
        public void A_capture_that_throws_does_not_stop_later_ones()
        {
            var attempts = new Counter();
            using var scheduler = new CaptureScheduler(
                () =>
                {
                    attempts.Increment();
                    throw new InvalidOperationException("the editor said no");
                },
                TimeSpan.FromMilliseconds(30),
                new RecordingTabSyncLog());

            scheduler.Schedule();
            Assert.True(attempts.WaitForAtLeast(1, Eventually), "The first capture never ran.");

            scheduler.Schedule();
            Assert.True(attempts.WaitForAtLeast(2, Eventually), "One failure disabled the scheduler.");
        }

        [Fact]
        public void Dispose_cancels_a_pending_capture()
        {
            var captures = new Counter();
            var scheduler = new CaptureScheduler(captures.Increment, Delay);

            scheduler.Schedule();
            scheduler.Dispose();

            // Solution close disposes the session while document events are still settling.
            Thread.Sleep(Delay + Delay);
            Assert.Equal(0, captures.Count);
        }

        [Fact]
        public void Scheduling_after_dispose_is_ignored()
        {
            var captures = new Counter();
            var scheduler = new CaptureScheduler(captures.Increment, TimeSpan.FromMilliseconds(20));
            scheduler.Dispose();

            scheduler.Schedule();
            scheduler.CaptureNow();

            Thread.Sleep(FarTooEarly);
            Assert.Equal(0, captures.Count);
        }

        [Fact]
        public void Dispose_is_idempotent()
        {
            var scheduler = new CaptureScheduler(() => { }, Delay);

            scheduler.Dispose();
            scheduler.Dispose();
        }

        /// <summary>Counts captures across threads and lets the test wait for one.</summary>
        private sealed class Counter
        {
            private readonly ManualResetEventSlim _changed = new ManualResetEventSlim(false);
            private int _count;
            private long _firstAt;

            public int Count => Volatile.Read(ref _count);

            public void Increment()
            {
                // Stamped before the count is published, so a reader that has seen the count has
                // seen the timestamp too.
                if (Count == 0)
                {
                    Volatile.Write(ref _firstAt, Stopwatch.GetTimestamp());
                }

                Interlocked.Increment(ref _count);
                _changed.Set();
            }

            /// <summary>
            /// How long after <paramref name="startedAt"/> the first capture ran. Lets a test time
            /// what happened instead of sampling whether it has happened yet, which is the
            /// difference between an assertion a loaded machine can only make slower and one it can
            /// make fail.
            /// </summary>
            public TimeSpan FirstRanAfter(long startedAt) =>
                TimeSpan.FromSeconds((Volatile.Read(ref _firstAt) - startedAt) / (double)Stopwatch.Frequency);

            public bool WaitForAtLeast(int count, TimeSpan timeout)
            {
                var deadline = DateTime.UtcNow + timeout;
                while (Count < count)
                {
                    var remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                    {
                        return false;
                    }

                    _changed.Reset();
                    if (Count >= count)
                    {
                        return true;
                    }

                    _changed.Wait(remaining);
                }

                return true;
            }
        }
    }
}
