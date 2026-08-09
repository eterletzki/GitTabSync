using System;

namespace GitTabSync.Git
{
    /// <summary>
    /// A resolved snapshot of what HEAD pointed at when it was read.
    /// </summary>
    /// <remarks>
    /// Detached HEAD is a first-class state rather than an error: rebases, bisects and
    /// "checkout &lt;sha&gt;" all park the repository there, and tab sessions still need a
    /// stable key while it lasts.
    /// </remarks>
    public sealed class GitHead : IEquatable<GitHead?>
    {
        private GitHead(string reference, bool isDetached)
        {
            Reference = reference;
            IsDetached = isDetached;
        }

        /// <summary>
        /// The short branch name (<c>feature/foo</c>) when attached, or the commit id when detached.
        /// </summary>
        public string Reference { get; }

        public bool IsDetached { get; }

        /// <summary>
        /// Stable identity used to key stored sessions. Prefixed so that a branch literally
        /// named after a commit id cannot collide with the detached state at that commit.
        /// </summary>
        public string SessionKey => IsDetached ? "detached/" + Reference : "branch/" + Reference;

        public static GitHead Branch(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Branch name must not be empty.", nameof(name));
            }

            return new GitHead(name, isDetached: false);
        }

        public static GitHead Detached(string commitId)
        {
            if (string.IsNullOrWhiteSpace(commitId))
            {
                throw new ArgumentException("Commit id must not be empty.", nameof(commitId));
            }

            return new GitHead(commitId, isDetached: true);
        }

        /// <summary>
        /// Parses the raw contents of a <c>HEAD</c> file. Returns <c>null</c> for content that is
        /// empty or not yet recognisable, which is the expected result of reading HEAD in the
        /// instant git has truncated it mid-checkout.
        /// </summary>
        public static GitHead? Parse(string? headFileContent)
        {
            if (headFileContent is null)
            {
                return null;
            }

            var text = headFileContent.Trim();
            if (text.Length == 0)
            {
                return null;
            }

            const string SymbolicPrefix = "ref:";
            if (text.StartsWith(SymbolicPrefix, StringComparison.Ordinal))
            {
                var target = text.Substring(SymbolicPrefix.Length).Trim();

                const string HeadsPrefix = "refs/heads/";
                if (target.StartsWith(HeadsPrefix, StringComparison.Ordinal))
                {
                    var name = target.Substring(HeadsPrefix.Length);
                    return name.Length == 0 ? null : Branch(name);
                }

                // A symbolic ref outside refs/heads/ (an unusual but legal HEAD). Keep the full
                // ref as the identity rather than discarding it.
                return target.Length == 0 ? null : Branch(target);
            }

            return IsCommitId(text) ? Detached(text) : null;
        }

        private static bool IsCommitId(string text)
        {
            // Accept both SHA-1 (40) and SHA-256 (64) object ids.
            if (text.Length != 40 && text.Length != 64)
            {
                return false;
            }

            foreach (var c in text)
            {
                var isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!isHex)
                {
                    return false;
                }
            }

            return true;
        }

        public bool Equals(GitHead? other)
        {
            return other is not null
                && IsDetached == other.IsDetached
                // Git ref names are case-sensitive, including on Windows.
                && string.Equals(Reference, other.Reference, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj) => Equals(obj as GitHead);

        public override int GetHashCode()
        {
            unchecked
            {
                return (StringComparer.Ordinal.GetHashCode(Reference) * 397) ^ IsDetached.GetHashCode();
            }
        }

        public override string ToString() => IsDetached ? Reference + " (detached)" : Reference;
    }
}
