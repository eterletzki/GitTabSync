using System;
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
    /// elapsed time, and a fake clock would prove only that the arithmetic is right. The
    /// assertions are one-sided to stay honest on a loaded machine: "not yet, far too early" uses
    /// a fraction of the delay, and "eventually" waits far longer than it should ever need.
    /// </remarks>
    public sealed class CaptureSchedulerTests
    {
        /// <summary>Long enough that an early capture cannot be mistaken for a slow one.</summary>
        private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(400);

        /// <summary>Well inside <see cref="Delay"/>: nothing may have happened yet.</summary>
        private static readonly TimeSpan FarTooEarly = TimeSpan.FromMilliseconds(80);

        /// <summary>Generous, because a busy CI machine is allowed to be slow, not wrong.</summary>
        private static readonly TimeSpan Eventually = TimeSpan.FromSeconds(10);

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

            scheduler.Schedule();

            // Reading the editor the instant a document event arrives is the bug the delay
            // exists to prevent: the document is still half-open at that point.
            Assert.Equal(0, captures.Count);
            Thread.Sleep(FarTooEarly);
            Assert.Equal(0, captures.Count);

            Assert.True(captures.WaitForAtLeast(1, Eventually), "The scheduled capture never ran.");
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
            using var scheduler = new CaptureScheduler(captures.Increment, Delay);

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

            public int Count => Volatile.Read(ref _count);

            public void Increment()
            {
                Interlocked.Increment(ref _count);
                _changed.Set();
            }

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
