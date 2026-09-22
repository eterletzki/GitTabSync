using System;

namespace GitTabSync.Release
{
    /// <summary>
    /// Turns the version strings this extension deals in into something comparable.
    /// </summary>
    /// <remarks>
    /// There are three sources and they are written by three different hands: the assembly's
    /// informational version, a heading in CHANGELOG.md, and whatever is in the state file from
    /// whenever the user last saw this page. They have to order consistently or an upgrade shows
    /// the wrong notes, so the normalising happens once, here.
    /// </remarks>
    internal static class ReleaseVersion
    {
        /// <summary>
        /// Parses leniently and normalises. Returns false for anything that is not a version at
        /// all, which every caller treats as "no information" rather than as a version of zero —
        /// the difference matters, because zero would read as "an ancient install" and announce
        /// the entire history.
        /// </summary>
        public static bool TryParse(string? text, out Version version)
        {
            version = null!;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var trimmed = text!.Trim();

            // "[1.1.0]" is how Keep a Changelog writes a heading, and a leading "v" is how most
            // people write a tag. Both mean the version inside them.
            trimmed = trimmed.Trim('[', ']', '(', ')');

            if (trimmed.Length != 0 && (trimmed[0] == 'v' || trimmed[0] == 'V'))
            {
                trimmed = trimmed.Substring(1);
            }

            // A pre-release suffix or build metadata — "1.2.0-beta.1", "1.2.0+abc123". The release
            // it belongs to is what matters here; ordering pre-releases against each other is a
            // problem this extension does not have.
            var cut = trimmed.IndexOfAny(new[] { '-', '+' });
            if (cut > 0)
            {
                trimmed = trimmed.Substring(0, cut);
            }

            if (!Version.TryParse(trimmed, out var parsed))
            {
                return false;
            }

            // Version leaves unspecified components at -1, which sorts below 0 — so "1.1" would
            // compare as older than "1.1.0" and an upgrade from one to the other would announce a
            // release the user already had.
            version = new Version(
                Math.Max(parsed.Major, 0),
                Math.Max(parsed.Minor, 0),
                Math.Max(parsed.Build, 0),
                Math.Max(parsed.Revision, 0));

            return true;
        }
    }
}
