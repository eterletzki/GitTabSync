using System;
using System.Collections.Generic;

namespace GitTabSync.Release
{
    /// <summary>Why the What's New page is being shown, if it is.</summary>
    public enum LandingPageKind
    {
        /// <summary>Nothing to say. The page does not open.</summary>
        None = 0,

        /// <summary>
        /// Nothing has been recorded for this user, so this is a first install. The page
        /// introduces the extension; what is in the current release is the smaller half of it.
        /// </summary>
        Welcome,

        /// <summary>An upgrade, with releases to report since the last one seen.</summary>
        Update,
    }

    /// <summary>
    /// Whether to show the What's New page, and what to put on it.
    /// </summary>
    /// <remarks>
    /// A pure function of three inputs, kept separate from <see cref="LandingPageGate"/> — which
    /// does the storage — so that every case below is a test with no filesystem in it.
    /// </remarks>
    public sealed class LandingPageDecision
    {
        /// <summary>Show nothing.</summary>
        public static readonly LandingPageDecision None =
            new LandingPageDecision(LandingPageKind.None, string.Empty, new ReleaseEntry[0]);

        private LandingPageDecision(
            LandingPageKind kind,
            string version,
            IReadOnlyList<ReleaseEntry> entries)
        {
            Kind = kind;
            Version = version;
            Entries = entries;
        }

        public LandingPageKind Kind { get; }

        /// <summary>The version being announced, as text.</summary>
        public string Version { get; }

        /// <summary>The releases to show, newest first. Empty is possible on a welcome.</summary>
        public IReadOnlyList<ReleaseEntry> Entries { get; }

        public bool ShouldShow => Kind != LandingPageKind.None;

        /// <param name="currentVersion">The running build's version.</param>
        /// <param name="lastSeenVersion">
        /// The version whose notes this user has already been shown; empty or unparseable means
        /// none has been.
        /// </param>
        /// <param name="notes">The changelog this build ships.</param>
        public static LandingPageDecision Decide(
            string? currentVersion,
            string? lastSeenVersion,
            ReleaseNotes notes)
        {
            if (notes is null)
            {
                throw new ArgumentNullException(nameof(notes));
            }

            if (!ReleaseVersion.TryParse(currentVersion, out var current))
            {
                // Nothing about the running build can be stated honestly, so nothing is. This is
                // not a case anybody should hit — the build pins the version — but "announce
                // something arbitrary" is not the right answer if they do.
                return None;
            }

            var version = currentVersion!.Trim();

            if (!ReleaseVersion.TryParse(lastSeenVersion, out var lastSeen))
            {
                // A first install, or a state file written by something this build cannot read.
                // Both are best served by the introduction rather than by a diff against a version
                // that may never have existed.
                return new LandingPageDecision(LandingPageKind.Welcome, version, EntryFor(notes, current));
            }

            if (lastSeen >= current)
            {
                // Same version, or a downgrade. Neither has anything to announce, and the record
                // is deliberately left alone: somebody who goes back to 1.1.0 after trying 1.2.0
                // has still seen 1.2.0's notes, and should not be shown them twice.
                return None;
            }

            var entries = notes.Between(lastSeen, current);

            // An upgrade the changelog says nothing about. A page listing no changes is worse than
            // no page: it reads as "this release did nothing". The record is left alone too, so
            // the next release that does have notes will cover this one as well.
            return entries.Count == 0
                ? None
                : new LandingPageDecision(LandingPageKind.Update, version, entries);
        }

        /// <summary>
        /// Everything the changelog has up to and including the running build, newest first — what
        /// the page shows when it is opened from the menu rather than by an upgrade.
        /// </summary>
        public static LandingPageDecision OnRequest(string? currentVersion, ReleaseNotes notes)
        {
            if (notes is null)
            {
                throw new ArgumentNullException(nameof(notes));
            }

            var version = currentVersion?.Trim() ?? string.Empty;

            // An unreadable version is not a reason to refuse a page somebody asked for by name,
            // so the upper bound falls away and they get the whole file.
            var entries = ReleaseVersion.TryParse(currentVersion, out var current)
                ? notes.Between(new Version(0, 0, 0, 0), current)
                : notes.Between(new Version(0, 0, 0, 0), new Version(int.MaxValue, 0, 0, 0));

            return new LandingPageDecision(LandingPageKind.Update, version, entries);
        }

        /// <summary>
        /// The current release's own entry, for the welcome page. Deliberately not the whole
        /// history: somebody installing this for the first time is being introduced to it, not
        /// caught up on releases they were never running.
        /// </summary>
        private static IReadOnlyList<ReleaseEntry> EntryFor(ReleaseNotes notes, Version current)
        {
            foreach (var entry in notes.Entries)
            {
                if (entry.Version == current)
                {
                    return new[] { entry };
                }
            }

            return new ReleaseEntry[0];
        }
    }
}
