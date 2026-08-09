using GitTabSync.Git;
using Xunit;

namespace GitTabSync.Tests
{
    public sealed class GitHeadTests
    {
        [Theory]
        [InlineData("ref: refs/heads/main\n", "main")]
        [InlineData("ref: refs/heads/feature/nested/name\n", "feature/nested/name")]
        [InlineData("ref: refs/heads/main", "main")]
        [InlineData("  ref: refs/heads/main  \r\n", "main")]
        public void Parse_reads_an_attached_branch(string content, string expected)
        {
            var head = GitHead.Parse(content);

            Assert.NotNull(head);
            Assert.False(head!.IsDetached);
            Assert.Equal(expected, head.Reference);
        }

        [Fact]
        public void Parse_reads_a_detached_sha1_head()
        {
            var sha = new string('a', 40);

            var head = GitHead.Parse(sha + "\n");

            Assert.NotNull(head);
            Assert.True(head!.IsDetached);
            Assert.Equal(sha, head.Reference);
        }

        [Fact]
        public void Parse_reads_a_detached_sha256_head()
        {
            var sha = new string('b', 64);

            var head = GitHead.Parse(sha);

            Assert.NotNull(head);
            Assert.True(head!.IsDetached);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   \r\n")]
        [InlineData("not a head")]
        [InlineData("ref: refs/heads/")]
        [InlineData("ref:")]
        // A truncated object id: git has written part of the file but not all of it.
        [InlineData("abc123")]
        public void Parse_returns_null_for_content_it_cannot_trust(string? content)
        {
            Assert.Null(GitHead.Parse(content));
        }

        [Fact]
        public void Parse_keeps_a_symbolic_ref_outside_refs_heads()
        {
            var head = GitHead.Parse("ref: refs/remotes/origin/main\n");

            Assert.NotNull(head);
            Assert.Equal("refs/remotes/origin/main", head!.Reference);
        }

        [Fact]
        public void SessionKey_separates_a_branch_from_a_detached_head_of_the_same_text()
        {
            var sha = new string('c', 40);

            var branch = GitHead.Branch(sha);
            var detached = GitHead.Detached(sha);

            Assert.NotEqual(branch.SessionKey, detached.SessionKey);
        }

        [Fact]
        public void Branch_names_are_case_sensitive()
        {
            Assert.NotEqual(GitHead.Branch("Feature"), GitHead.Branch("feature"));
        }

        [Fact]
        public void Equal_heads_compare_equal()
        {
            Assert.Equal(GitHead.Branch("main"), GitHead.Branch("main"));
            Assert.Equal(GitHead.Branch("main").GetHashCode(), GitHead.Branch("main").GetHashCode());
        }
    }
}
