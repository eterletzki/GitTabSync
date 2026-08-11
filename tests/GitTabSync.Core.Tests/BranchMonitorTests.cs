using System;
using System.IO;
using System.Threading;
using GitTabSync.Git;
using GitTabSync.Tests.TestSupport;
using Xunit;

namespace GitTabSync.Tests
{
    public sealed class BranchMonitorTests
    {
        private static readonly TimeSpan NoPolling = TimeSpan.Zero;

        [Fact]
        public void Start_takes_the_current_head_as_a_baseline_without_raising()
        {
            using var temp = new TempDirectory();
            temp.CreateFile(".git/HEAD", "ref: refs/heads/main\n");
            var repository = GitRepository.Discover(temp.Path)!;

            using var monitor = new BranchMonitor(repository, pollInterval: NoPolling);
            var raised = 0;
            monitor.BranchChanged += (_, _) => raised++;

            monitor.Start();

            Assert.Equal(0, raised);
            Assert.Equal("main", monitor.Current?.Reference);
        }

        [Fact]
        public void CheckNow_raises_once_head_moves()
        {
            using var temp = new TempDirectory();
            var headPath = temp.CreateFile(".git/HEAD", "ref: refs/heads/main\n");
            var repository = GitRepository.Discover(temp.Path)!;

            using var monitor = new BranchMonitor(repository, pollInterval: NoPolling);
            BranchChangedEventArgs? observed = null;
            monitor.BranchChanged += (_, e) => observed = e;
            monitor.Start();

            File.WriteAllText(headPath, "ref: refs/heads/feature/x\n");
            monitor.CheckNow();

            Assert.NotNull(observed);
            Assert.Equal("main", observed!.Previous?.Reference);
            Assert.Equal("feature/x", observed.Current.Reference);
        }

        [Fact]
        public void CheckNow_is_silent_when_head_has_not_moved()
        {
            using var temp = new TempDirectory();
            var headPath = temp.CreateFile(".git/HEAD", "ref: refs/heads/main\n");
            var repository = GitRepository.Discover(temp.Path)!;

            using var monitor = new BranchMonitor(repository, pollInterval: NoPolling);
            var raised = 0;
            monitor.BranchChanged += (_, _) => raised++;
            monitor.Start();

            // Rewriting the same content is what a no-op checkout looks like on disk.
            File.WriteAllText(headPath, "ref: refs/heads/main\n");
            monitor.CheckNow();
            monitor.CheckNow();

            Assert.Equal(0, raised);
        }

        [Fact]
        public void An_unreadable_head_is_not_reported_as_a_change()
        {
            using var temp = new TempDirectory();
            var headPath = temp.CreateFile(".git/HEAD", "ref: refs/heads/main\n");
            var repository = GitRepository.Discover(temp.Path)!;

            using var monitor = new BranchMonitor(repository, pollInterval: NoPolling);
            var raised = 0;
            monitor.BranchChanged += (_, _) => raised++;
            monitor.Start();

            // Mid-checkout, HEAD can be observed empty. Reporting that as a change would
            // attribute the open tabs to a branch we cannot name.
            File.WriteAllText(headPath, string.Empty);
            monitor.CheckNow();

            Assert.Equal(0, raised);
            Assert.Equal("main", monitor.Current?.Reference);
        }

        [Fact]
        public void Detaching_head_is_reported_as_a_change()
        {
            using var temp = new TempDirectory();
            var headPath = temp.CreateFile(".git/HEAD", "ref: refs/heads/main\n");
            var repository = GitRepository.Discover(temp.Path)!;

            using var monitor = new BranchMonitor(repository, pollInterval: NoPolling);
            BranchChangedEventArgs? observed = null;
            monitor.BranchChanged += (_, e) => observed = e;
            monitor.Start();

            File.WriteAllText(headPath, new string('a', 40) + "\n");
            monitor.CheckNow();

            Assert.NotNull(observed);
            Assert.True(observed!.Current.IsDetached);
        }

        [Fact]
        public void CheckNow_before_Start_does_nothing()
        {
            using var temp = new TempDirectory();
            temp.CreateFile(".git/HEAD", "ref: refs/heads/main\n");
            var repository = GitRepository.Discover(temp.Path)!;

            using var monitor = new BranchMonitor(repository, pollInterval: NoPolling);
            var raised = 0;
            monitor.BranchChanged += (_, _) => raised++;

            monitor.CheckNow();

            Assert.Equal(0, raised);
            Assert.Null(monitor.Current);
        }

        [Fact]
        public void Detects_a_branch_change_on_its_own_without_being_told()
        {
            using var temp = new TempDirectory();
            var headPath = temp.CreateFile(".git/HEAD", "ref: refs/heads/main\n");
            var repository = GitRepository.Discover(temp.Path)!;

            using var monitor = new BranchMonitor(
                repository,
                debounce: TimeSpan.FromMilliseconds(20),
                pollInterval: TimeSpan.FromMilliseconds(50));

            using var signal = new ManualResetEventSlim(false);
            GitHead? observed = null;
            monitor.BranchChanged += (_, e) =>
            {
                observed = e.Current;
                signal.Set();
            };

            monitor.Start();

            // Simulates the branch being switched outside Visual Studio: nothing calls into the
            // monitor, it has to notice by itself. The watcher usually wins; the poll is the
            // guarantee that this terminates.
            File.WriteAllText(headPath, "ref: refs/heads/other\n");

            Assert.True(signal.Wait(TimeSpan.FromSeconds(10)), "The branch change was never detected.");
            Assert.Equal("other", observed?.Reference);
        }

        [Fact]
        public void Dispose_stops_further_notifications()
        {
            using var temp = new TempDirectory();
            var headPath = temp.CreateFile(".git/HEAD", "ref: refs/heads/main\n");
            var repository = GitRepository.Discover(temp.Path)!;

            var monitor = new BranchMonitor(repository, pollInterval: NoPolling);
            var raised = 0;
            monitor.BranchChanged += (_, _) => raised++;
            monitor.Start();
            monitor.Dispose();

            File.WriteAllText(headPath, "ref: refs/heads/other\n");
            monitor.CheckNow();

            Assert.Equal(0, raised);
        }

        [Fact]
        public void Dispose_is_idempotent()
        {
            using var temp = new TempDirectory();
            temp.CreateFile(".git/HEAD", "ref: refs/heads/main\n");
            var repository = GitRepository.Discover(temp.Path)!;

            var monitor = new BranchMonitor(repository, pollInterval: NoPolling);
            monitor.Start();

            monitor.Dispose();
            monitor.Dispose();
        }

        /// <summary>
        /// The two detectors and the debounce between them and <see cref="BranchMonitor.CheckNow"/>.
        /// </summary>
        /// <remarks>
        /// Each test disables one path so the other has to do the work — the README claims the
        /// watcher and the poll are independent, and a test that leaves both running proves only
        /// that at least one of them works. Real timers and real file writes, because a fake
        /// watcher would not reproduce the thing being relied on.
        /// </remarks>
        public sealed class Timing
        {
            /// <summary>Generous: a busy machine may be slow, but it may not be wrong.</summary>
            private static readonly TimeSpan Eventually = TimeSpan.FromSeconds(10);

            [Fact]
            public void A_burst_of_head_writes_produces_a_single_change()
            {
                using var temp = new TempDirectory();
                var headPath = temp.CreateFile(".git/HEAD", "ref: refs/heads/main\n");
                var repository = GitRepository.Discover(temp.Path)!;

                using var monitor = new BranchMonitor(
                    repository,
                    debounce: TimeSpan.FromMilliseconds(500),
                    pollInterval: NoPolling);

                using var signal = new ManualResetEventSlim(false);
                var raised = 0;
                monitor.BranchChanged += (_, _) =>
                {
                    Interlocked.Increment(ref raised);
                    signal.Set();
                };
                monitor.Start();

                // One checkout touches HEAD several times. The intermediate values are real but
                // meaningless; reacting to them would save the tabs under a branch nobody was on.
                // The gaps are long enough for each write to be seen separately, and the whole
                // burst still far shorter than the debounce.
                foreach (var branch in new[] { "step-one", "step-two", "final" })
                {
                    File.WriteAllText(headPath, "ref: refs/heads/" + branch + "\n");
                    Thread.Sleep(TimeSpan.FromMilliseconds(60));
                }

                Assert.True(signal.Wait(Eventually), "The branch change was never detected.");

                // Long enough that a second and third change would have arrived by now.
                Thread.Sleep(TimeSpan.FromSeconds(1));

                Assert.Equal(1, Volatile.Read(ref raised));
                Assert.Equal("final", monitor.Current?.Reference);
            }

            [Fact]
            public void A_change_is_not_reported_before_the_debounce_has_elapsed()
            {
                using var temp = new TempDirectory();
                var headPath = temp.CreateFile(".git/HEAD", "ref: refs/heads/main\n");
                var repository = GitRepository.Discover(temp.Path)!;

                using var monitor = new BranchMonitor(
                    repository,
                    debounce: TimeSpan.FromMilliseconds(600),
                    pollInterval: NoPolling);

                using var signal = new ManualResetEventSlim(false);
                monitor.BranchChanged += (_, _) => signal.Set();
                monitor.Start();

                File.WriteAllText(headPath, "ref: refs/heads/other\n");

                // Well inside the debounce: HEAD may still be mid-checkout, and reading it now is
                // what the wait exists to avoid.
                Assert.False(signal.Wait(TimeSpan.FromMilliseconds(120)), "The change was reported early.");
                Assert.True(signal.Wait(Eventually), "The branch change was never detected.");
            }

            [Fact]
            public void The_watcher_detects_a_change_with_polling_switched_off()
            {
                using var temp = new TempDirectory();
                var headPath = temp.CreateFile(".git/HEAD", "ref: refs/heads/main\n");
                var repository = GitRepository.Discover(temp.Path)!;

                using var monitor = new BranchMonitor(
                    repository,
                    debounce: TimeSpan.FromMilliseconds(20),
                    pollInterval: NoPolling);

                using var signal = new ManualResetEventSlim(false);
                monitor.BranchChanged += (_, _) => signal.Set();
                monitor.Start();

                File.WriteAllText(headPath, "ref: refs/heads/other\n");

                // Nothing else can fire: only the file watcher can end this test.
                Assert.True(signal.Wait(Eventually), "The watcher never reported the change.");
            }

            [Fact]
            public void The_poll_detects_a_change_the_debounce_has_not_acted_on_yet()
            {
                using var temp = new TempDirectory();
                var headPath = temp.CreateFile(".git/HEAD", "ref: refs/heads/main\n");
                var repository = GitRepository.Discover(temp.Path)!;

                // A debounce far longer than the test parks whatever the watcher saw, leaving the
                // poll — which checks directly, without the debounce — as the only way through.
                // That is the real-world case the poll exists for: a watcher whose events were
                // dropped or never arrived.
                using var monitor = new BranchMonitor(
                    repository,
                    debounce: TimeSpan.FromMinutes(5),
                    pollInterval: TimeSpan.FromMilliseconds(50));

                using var signal = new ManualResetEventSlim(false);
                monitor.BranchChanged += (_, _) => signal.Set();
                monitor.Start();

                File.WriteAllText(headPath, "ref: refs/heads/other\n");

                Assert.True(signal.Wait(Eventually), "The poll never reported the change.");
            }
        }
    }
}
