using System;
using GitTabSync.Git;

namespace GitTabSync.Settings
{
    /// <summary>
    /// Human-readable names for scopes, for a settings UI.
    /// </summary>
    /// <remarks>
    /// In Core rather than in the WPF layer because it is the one place that has to agree with
    /// <see cref="GitHead.SessionKey"/> about what a head key looks like, and because a label that
    /// says the wrong branch is a bug worth a test.
    /// </remarks>
    public static class SettingScopeLabel
    {
        /// <summary>A detached HEAD's full object id is unreadable; this much identifies it.</summary>
        private const int ShortCommitIdLength = 8;

        public static string For(SettingScope scope)
        {
            if (scope is null)
            {
                throw new ArgumentNullException(nameof(scope));
            }

            switch (scope.Kind)
            {
                case SettingScopeKind.Global:
                    return "Defaults";
                case SettingScopeKind.Repository:
                    return "This repository";
                case SettingScopeKind.Branch:
                    return ForHead(scope.HeadKey);
                case SettingScopeKind.Solution:
                case SettingScopeKind.Project:
                    return FileName(scope.Path) + " on " + ForHead(scope.HeadKey);
                default:
                    return scope.ToString();
            }
        }

        /// <summary>Strips the prefix <see cref="GitHead.SessionKey"/> adds.</summary>
        public static string ForHead(string headKey)
        {
            if (string.IsNullOrEmpty(headKey))
            {
                return "(unknown)";
            }

            if (headKey.StartsWith(GitHead.BranchPrefix, StringComparison.Ordinal))
            {
                return headKey.Substring(GitHead.BranchPrefix.Length);
            }

            if (headKey.StartsWith(GitHead.DetachedPrefix, StringComparison.Ordinal))
            {
                var id = headKey.Substring(GitHead.DetachedPrefix.Length);
                var shortened = id.Length > ShortCommitIdLength ? id.Substring(0, ShortCommitIdLength) : id;
                return shortened + " (detached)";
            }

            return headKey;
        }

        public static string FileName(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            var separator = path.LastIndexOfAny(new[] { '/', '\\' });
            return separator < 0 ? path : path.Substring(separator + 1);
        }
    }
}
