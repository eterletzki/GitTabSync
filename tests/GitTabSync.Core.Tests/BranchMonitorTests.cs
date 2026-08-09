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
    }
}
