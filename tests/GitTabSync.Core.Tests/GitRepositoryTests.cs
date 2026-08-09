using System.IO;
using GitTabSync.Git;
using GitTabSync.Tests.TestSupport;
using Xunit;

namespace GitTabSync.Tests
{
    public sealed class GitRepositoryTests
    {
        [Fact]
        public void Discover_finds_the_repository_from_a_nested_directory()
        {
            using var temp = new TempDirectory();
            temp.CreateFile(".git/HEAD", "ref: refs/heads/main\n");
            var nested = temp.CreateDirectory("src/deep/nested");

            var repository = GitRepository.Discover(nested);

            Assert.NotNull(repository);
            Assert.Equal(temp.Path, repository!.WorkingDirectory);
            Assert.Equal(Path.Combine(temp.Path, ".git"), repository.GitDirectory);
        }

        [Fact]
        public void Discover_accepts_a_file_path_inside_the_repository()
        {
            using var temp = new TempDirectory();
            temp.CreateFile(".git/HEAD", "ref: refs/heads/main\n");
            var file = temp.CreateFile("src/Program.cs", "// code");

            var repository = GitRepository.Discover(file);

            Assert.NotNull(repository);
            Assert.Equal(temp.Path, repository!.WorkingDirectory);
        }

        [Fact]
        public void Discover_returns_null_outside_a_repository()
        {
            using var temp = new TempDirectory();
            temp.CreateDirectory("plain");

            Assert.Null(GitRepository.Discover(temp.Combine("plain")));
        }

        [Fact]
        public void Discover_follows_a_git_file_to_a_linked_worktree()
        {
            using var temp = new TempDirectory();

            // A linked worktree: ".git" is a file redirecting to the worktree's own git
            // directory, which is where its HEAD lives.
            var worktreeGitDirectory = temp.CreateDirectory("main/.git/worktrees/feature");
            File.WriteAllText(Path.Combine(worktreeGitDirectory, "HEAD"), "ref: refs/heads/feature\n");

            temp.CreateDirectory("feature-wt");
            temp.CreateFile("feature-wt/.git", "gitdir: " + worktreeGitDirectory + "\n");

            var repository = GitRepository.Discover(temp.Combine("feature-wt"));

            Assert.NotNull(repository);
            Assert.Equal(worktreeGitDirectory, repository!.GitDirectory);
            Assert.Equal("feature", repository.ReadHead()?.Reference);
        }

        [Fact]
        public void Discover_resolves_a_relative_gitdir_redirect()
        {
            using var temp = new TempDirectory();
            var worktreeGitDirectory = temp.CreateDirectory("main/.git/worktrees/feature");
            File.WriteAllText(Path.Combine(worktreeGitDirectory, "HEAD"), "ref: refs/heads/feature\n");

            temp.CreateDirectory("feature-wt");
            temp.CreateFile("feature-wt/.git", "gitdir: ../main/.git/worktrees/feature\n");

            var repository = GitRepository.Discover(temp.Combine("feature-wt"));

            Assert.NotNull(repository);
            Assert.Equal(worktreeGitDirectory, repository!.GitDirectory);
        }

        [Fact]
        public void ReadHead_returns_the_current_branch()
        {
            using var temp = new TempDirectory();
            temp.CreateFile(".git/HEAD", "ref: refs/heads/feature/x\n");

            var repository = GitRepository.Discover(temp.Path);

            Assert.Equal("feature/x", repository!.ReadHead()?.Reference);
        }

        [Fact]
        public void ReadHead_returns_null_when_head_is_missing()
        {
            using var temp = new TempDirectory();
            temp.CreateDirectory(".git");

            var repository = GitRepository.Discover(temp.Path);

            Assert.NotNull(repository);
            Assert.Null(repository!.ReadHead());
        }

        [Fact]
        public void ReadHead_succeeds_while_another_process_holds_head_open_for_writing()
        {
            using var temp = new TempDirectory();
            var headPath = temp.CreateFile(".git/HEAD", "ref: refs/heads/main\n");
            var repository = GitRepository.Discover(temp.Path);

            // Git keeps a write handle on HEAD while it swaps branches. Opening exclusively
            // would fail at exactly the moment detection matters most.
            using var holder = new FileStream(headPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);

            Assert.Equal("main", repository!.ReadHead()?.Reference);
        }

        [Fact]
        public void Discover_returns_null_for_an_empty_path()
        {
            Assert.Null(GitRepository.Discover(string.Empty));
        }
    }
}
