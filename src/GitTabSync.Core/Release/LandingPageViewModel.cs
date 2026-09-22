using System;
using System.Collections.Generic;

namespace GitTabSync.Release
{
    /// <summary>
    /// Everything the What's New page shows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In Core with no WPF reference, for the reason <see cref="Settings.SettingsViewModel"/> is:
    /// the Visual Studio layer has no coverage, so the wording, the grouping and the difference
    /// between a welcome and an upgrade are all decided on this side of the seam. The window binds
    /// and marshals; it decides nothing.
    /// </para>
    /// <para>
    /// Text that is not wanted in a given state is exposed as the empty string rather than as a
    /// visibility flag, because the theme's note style already collapses an empty run — the same
    /// mechanism the settings window's row explanations use.
    /// </para>
    /// </remarks>
    public sealed class LandingPageViewModel
    {
        /// <summary>Where the extension's windows live, quoted in more than one place.</summary>
        public const string MenuPath = "View > Other Windows";

        public const string ProjectUrl = "https://github.com/Eterletzki/GitTabSync";

        public LandingPageViewModel(LandingPageDecision decision)
        {
            if (decision is null)
            {
                throw new ArgumentNullException(nameof(decision));
            }

            IsWelcome = decision.Kind == LandingPageKind.Welcome;
            Version = decision.Version;

            // Version headings earn their place only when there is more than one release on the
            // page; with one, the title has already said which version this is.
            var sections = new List<ReleaseSection>(decision.Entries.Count);
            var showHeadings = decision.Entries.Count > 1;

            foreach (var entry in decision.Entries)
            {
                sections.Add(new ReleaseSection(entry, showHeadings));
            }

            Releases = sections;
        }

        public bool IsWelcome { get; }

        public string Version { get; }

        public IReadOnlyList<ReleaseSection> Releases { get; }

        public bool HasReleases => Releases.Count != 0;

        public string Title =>
            IsWelcome
                ? "Git Tab Sync"
                : Version.Length == 0 ? "What's new" : "What's new in " + Version;

        /// <summary>
        /// What the page opens with. A first install is told what the extension does, because a
        /// list of changes means nothing to somebody who has never seen the thing being changed;
        /// an upgrade already knows, and wants the list.
        /// </summary>
        public string IntroText =>
            IsWelcome
                ? "Git Tab Sync remembers which documents you had open on each branch and puts them"
                    + " back when you switch, including when the branch is switched outside Visual"
                    + " Studio. It is already working — switch branches and come back, and your"
                    + " tabs come back with you."
                : "Visual Studio has been updated to Git Tab Sync " + Version + ".";

        /// <summary>Empty when there is nothing below it to head.</summary>
        public string ReleasesHeading => HasReleases && IsWelcome ? "In this release" : string.Empty;

        public bool HasReleasesHeading => ReleasesHeading.Length != 0;

        /// <summary>
        /// Shown on a first install only. Somebody upgrading has had the settings window all
        /// along, and repeating where it lives every time they update is how a page like this
        /// turns into something people close without reading.
        /// </summary>
        public string SettingsHintText =>
            IsWelcome
                ? "Everything it does can be configured per branch, per solution, per repository or"
                    + " everywhere at once — so one branch can behave differently without changing"
                    + " any other."
                : string.Empty;

        public string ReopenHintText =>
            "This page can be reopened from " + MenuPath + " > Git Tab Sync: What's New.";

        public string SettingsButtonText => "Open settings";

        public string ProjectLinkText => "Documentation and issues on GitHub";
    }

    /// <summary>One release on the page.</summary>
    public sealed class ReleaseSection
    {
        internal ReleaseSection(ReleaseEntry entry, bool showsHeading)
        {
            Heading = entry.Date.Length == 0
                ? entry.VersionText
                : entry.VersionText + " — " + entry.Date;

            ShowsHeading = showsHeading;
            Groups = entry.Groups;
        }

        public string Heading { get; }

        /// <summary>
        /// Decided here rather than in the window so the window needs no binding that reaches back
        /// up out of its item template.
        /// </summary>
        public bool ShowsHeading { get; }

        public IReadOnlyList<ReleaseChangeGroup> Groups { get; }
    }
}
