using System;
using System.IO;
using System.Threading;

namespace GitTabSync.Git
{
    /// <summary>
    /// Locates a git repository from a path inside it and reads HEAD directly from disk.
    /// </summary>
    /// <remarks>
    /// Reading the plumbing files rather than shelling out to <c>git.exe</c> keeps branch
    /// detection cheap enough to poll, and avoids spawning a process on the UI thread.
    /// </remarks>
    public sealed class GitRepository
    {
        private GitRepository(string workingDirectory, string gitDirectory)
        {
            WorkingDirectory = workingDirectory;
            GitDirectory = gitDirectory;
            HeadFilePath = Path.Combine(gitDirectory, "HEAD");
        }

        /// <summary>The directory containing the <c>.git</c> entry.</summary>
        public string WorkingDirectory { get; }

        /// <summary>
        /// The real git directory. For a linked worktree this is
        /// <c>&lt;main&gt;/.git/worktrees/&lt;name&gt;</c>, not the main repository's <c>.git</c>,
        /// because that is where the worktree's own HEAD lives.
        /// </summary>
        public string GitDirectory { get; }

        public string HeadFilePath { get; }

        /// <summary>
        /// Walks up from <paramref name="startPath"/> looking for a repository.
        /// Returns <c>null</c> when the path is not inside one.
        /// </summary>
        public static GitRepository? Discover(string startPath)
        {
            if (string.IsNullOrWhiteSpace(startPath))
            {
                return null;
            }

            DirectoryInfo? current;
            try
            {
                var full = Path.GetFullPath(startPath);
                current = File.Exists(full) ? new FileInfo(full).Directory : new DirectoryInfo(full);
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return null;
            }

            while (current is not null)
            {
                var candidate = Path.Combine(current.FullName, ".git");

                if (Directory.Exists(candidate))
                {
                    return new GitRepository(current.FullName, candidate);
                }

                if (File.Exists(candidate))
                {
                    // A ".git" *file* means a linked worktree or a submodule; it redirects
                    // to the real git directory.
                    var redirected = ResolveGitFile(candidate, current.FullName);
                    if (redirected is not null)
                    {
                        return new GitRepository(current.FullName, redirected);
                    }

                    return null;
                }

                current = current.Parent;
            }

            return null;
        }

        private static string? ResolveGitFile(string gitFilePath, string containingDirectory)
        {
            string content;
            try
            {
                content = File.ReadAllText(gitFilePath);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return null;
            }

            const string GitDirPrefix = "gitdir:";
            var text = content.Trim();
            if (!text.StartsWith(GitDirPrefix, StringComparison.Ordinal))
            {
                return null;
            }

            var target = text.Substring(GitDirPrefix.Length).Trim();
            if (target.Length == 0)
            {
                return null;
            }

            try
            {
                // The redirect is commonly relative to the directory holding the .git file.
                if (!Path.IsPathRooted(target))
                {
                    target = Path.Combine(containingDirectory, target);
                }

                var resolved = Path.GetFullPath(target);
                return Directory.Exists(resolved) ? resolved : null;
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return null;
            }
        }

        /// <summary>
        /// Reads and parses HEAD, retrying briefly to ride out the window in which git has the
        /// file locked or half-written during a checkout.
        /// </summary>
        /// <returns>The current HEAD, or <c>null</c> if it could not be read.</returns>
        public GitHead? ReadHead()
        {
            const int MaxAttempts = 5;
            const int RetryDelayMilliseconds = 20;

            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                string? content = null;
                try
                {
                    // FileShare.ReadWrite: git holds a write handle while it swaps HEAD, and an
                    // exclusive open would fail exactly when a switch is happening.
                    using var stream = new FileStream(
                        HeadFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(stream);
                    content = reader.ReadToEnd();
                }
                catch (FileNotFoundException)
                {
                    return null;
                }
                catch (DirectoryNotFoundException)
                {
                    return null;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // Locked; fall through to the retry.
                }

                var head = GitHead.Parse(content);
                if (head is not null)
                {
                    return head;
                }

                if (attempt < MaxAttempts - 1)
                {
                    Thread.Sleep(RetryDelayMilliseconds);
                }
            }

            return null;
        }
    }
}
