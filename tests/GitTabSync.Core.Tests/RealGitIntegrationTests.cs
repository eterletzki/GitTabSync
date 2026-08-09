using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using GitTabSync.Git;
using GitTabSync.Tests.TestSupport;
using Xunit;

namespace GitTabSync.Tests
{
    /// <summary>
    /// Exercises the monitor against a real repository driven by the real <c>git</c> executable.
    /// </summary>
    /// <remarks>
    /// The unit tests write HEAD themselves, which proves the parsing but assumes how git updates
    /// the file. Git actually writes <c>HEAD.lock</c> and renames it over the top, so only a real
    /// checkout proves the watcher is subscribed to the events that actually fire.
    /// Skipped when git is not on PATH.
    /// </remarks>
    [Trait("Category", "Integration")]
    public sealed class RealGitIntegrationTests
    {
        [SkippableFact]
        public void Detects_a_real_git_checkout()
        {
            Skip.IfNot(IsGitAvailable(), "git is not on PATH.");

            using var temp = new TempDirectory("realgit");
            InitialiseRepository(temp.Path);

            var repository = GitRepository.Discover(temp.Path);
            Assert.NotNull(repository);
            Assert.Equal("main", repository!.ReadHead()?.Reference);

            using var monitor = new BranchMonitor(
                repository,
                debounce: TimeSpan.FromMilliseconds(50),
                pollInterval: TimeSpan.FromMilliseconds(100));

            using var signal = new ManualResetEventSlim(false);
            GitHead? observed = null;
            monitor.BranchChanged += (_, e) =>
            {
                observed = e.Current;
                signal.Set();
            };
            monitor.Start();

            // Nothing tells the monitor this happened — exactly the "branch changed outside
            // Visual Studio" case the extension exists for.
            Git(temp.Path, "checkout -q -b feature/real");

            Assert.True(signal.Wait(TimeSpan.FromSeconds(15)), "The real checkout was never detected.");
            Assert.Equal("feature/real", observed?.Reference);
        }

        [SkippableFact]
        public void Detects_a_real_detached_checkout()
        {
            Skip.IfNot(IsGitAvailable(), "git is not on PATH.");

            using var temp = new TempDirectory("realgit");
            InitialiseRepository(temp.Path);

            var repository = GitRepository.Discover(temp.Path)!;
            using var monitor = new BranchMonitor(repository, pollInterval: TimeSpan.Zero);
            monitor.Start();

            Git(temp.Path, "checkout -q --detach HEAD");
            monitor.CheckNow();

            Assert.True(monitor.Current?.IsDetached);
        }

        [SkippableFact]
        public void Discovers_a_real_linked_worktree()
        {
            Skip.IfNot(IsGitAvailable(), "git is not on PATH.");

            using var temp = new TempDirectory("realgit");
            var main = Path.Combine(temp.Path, "main");
            Directory.CreateDirectory(main);
            InitialiseRepository(main);

            var worktree = Path.Combine(temp.Path, "wt");
            Git(main, "worktree add -q -b wt-branch \"" + worktree + "\"");

            var repository = GitRepository.Discover(worktree);

            Assert.NotNull(repository);
            // The worktree's HEAD must come from its own git directory, not the main one.
            Assert.Equal("wt-branch", repository!.ReadHead()?.Reference);
            Assert.Equal("main", GitRepository.Discover(main)!.ReadHead()?.Reference);
        }

        private static void InitialiseRepository(string path)
        {
            Git(path, "init -q -b main");
            Git(path, "config user.email test@example.com");
            Git(path, "config user.name Test");
            Git(path, "config commit.gpgsign false");
            File.WriteAllText(Path.Combine(path, "file.txt"), "hello");
            Git(path, "add -A");
            Git(path, "commit -q -m initial");
        }

        private static bool IsGitAvailable()
        {
            try
            {
                return Run(Environment.CurrentDirectory, "--version").exitCode == 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void Git(string workingDirectory, string arguments)
        {
            var (exitCode, output) = Run(workingDirectory, arguments);
            if (exitCode != 0)
            {
                throw new InvalidOperationException($"git {arguments} failed ({exitCode}): {output}");
            }
        }

        private static (int exitCode, string output) Run(string workingDirectory, string arguments)
        {
            var startInfo = new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo)!;
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit(30_000);
            return (process.ExitCode, output);
        }
    }
}
