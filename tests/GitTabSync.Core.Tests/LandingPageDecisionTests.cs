using System.Linq;
using GitTabSync.Release;
using Xunit;

namespace GitTabSync.Tests
{
    /// <summary>
    /// The whole decision table, one test per row. Pure: no filesystem, no store — that half is
    /// <see cref="LandingPageGateTests"/>.
    /// </summary>
    public class LandingPageDecisionTests
    {
        private static readonly ReleaseNotes Notes = ChangelogParser.Parse(
            "# Changelog\n\n"
            + "## 1.3.0\n\n- Three\n\n"
            + "## 1.2.0\n\n- Two\n\n"
            + "## 1.1.0\n\n- One\n\n"
            + "## 1.0.0\n\n- Zero\n");

        [Fact]
        public void Nothing_seen_shows_a_welcome()
        {
            var decision = LandingPageDecision.Decide("1.2.0", lastSeenVersion: null, Notes);

            Assert.Equal(LandingPageKind.Welcome, decision.Kind);
            Assert.True(decision.ShouldShow);
            Assert.Equal("1.2.0", decision.Version);
        }

        /// <remarks>
        /// Somebody installing this for the first time is being introduced to it, not caught up on
        /// releases they were never running.
        /// </remarks>
        [Fact]
        public void A_welcome_shows_only_the_current_release_not_the_whole_history()
        {
            var decision = LandingPageDecision.Decide("1.2.0", lastSeenVersion: "", Notes);

            var entry = Assert.Single(decision.Entries);
            Assert.Equal("1.2.0", entry.VersionText);
        }

        [Fact]
        public void The_same_version_again_shows_nothing()
        {
            var decision = LandingPageDecision.Decide("1.2.0", "1.2.0", Notes);

            Assert.Equal(LandingPageKind.None, decision.Kind);
            Assert.False(decision.ShouldShow);
        }

        [Fact]
        public void A_version_spelled_differently_is_still_the_same_version()
        {
            Assert.False(LandingPageDecision.Decide("1.1.0", "1.1", Notes).ShouldShow);
        }

        /// <remarks>
        /// Somebody who goes back to 1.1.0 after trying 1.3.0 has still seen 1.3.0's notes. The
        /// gate leaves the record alone for the same reason — see
        /// <see cref="LandingPageGateTests"/>.
        /// </remarks>
        [Fact]
        public void A_downgrade_shows_nothing()
        {
            var decision = LandingPageDecision.Decide("1.1.0", "1.3.0", Notes);

            Assert.Equal(LandingPageKind.None, decision.Kind);
        }

        [Fact]
        public void An_upgrade_shows_the_releases_between_the_two_newest_first()
        {
            var decision = LandingPageDecision.Decide("1.3.0", "1.1.0", Notes);

            Assert.Equal(LandingPageKind.Update, decision.Kind);
            Assert.Equal(new[] { "1.3.0", "1.2.0" }, decision.Entries.Select(e => e.VersionText));
        }

        [Fact]
        public void The_version_last_seen_is_not_shown_again()
        {
            var decision = LandingPageDecision.Decide("1.3.0", "1.1.0", Notes);

            Assert.DoesNotContain(decision.Entries, e => e.VersionText == "1.1.0");
        }

        /// <remarks>
        /// The upper bound matters when a changelog has been written ahead of the release it
        /// describes. Announcing a version the user does not have is worse than saying nothing,
        /// because every item in it is a promise the installed build cannot keep.
        /// </remarks>
        [Fact]
        public void Releases_above_the_running_build_are_not_announced()
        {
            var decision = LandingPageDecision.Decide("1.2.0", "1.0.0", Notes);

            Assert.Equal(new[] { "1.2.0", "1.1.0" }, decision.Entries.Select(e => e.VersionText));
        }

        [Fact]
        public void An_unparseable_current_version_shows_nothing()
        {
            Assert.False(LandingPageDecision.Decide("not a version", "1.1.0", Notes).ShouldShow);
            Assert.False(LandingPageDecision.Decide(null, "1.1.0", Notes).ShouldShow);
            Assert.False(LandingPageDecision.Decide("", "1.1.0", Notes).ShouldShow);
        }

        [Fact]
        public void An_unparseable_stored_version_is_treated_as_a_first_install()
        {
            var decision = LandingPageDecision.Decide("1.2.0", "corrupted", Notes);

            Assert.Equal(LandingPageKind.Welcome, decision.Kind);
        }

        /// <remarks>
        /// A page listing no changes is worse than no page: it reads as "this release did
        /// nothing".
        /// </remarks>
        [Fact]
        public void An_upgrade_the_changelog_says_nothing_about_shows_nothing()
        {
            var notes = ChangelogParser.Parse("## 1.0.0\n\n- Zero\n");

            Assert.False(LandingPageDecision.Decide("1.2.0", "1.1.0", notes).ShouldShow);
        }

        [Fact]
        public void An_upgrade_with_an_empty_changelog_shows_nothing()
        {
            Assert.False(LandingPageDecision.Decide("1.2.0", "1.1.0", ReleaseNotes.Empty).ShouldShow);
        }

        /// <remarks>
        /// Unlike an upgrade, a first install is worth greeting even when the changelog has no
        /// entry for the running build: the welcome is an introduction to the extension, and the
        /// release's own list is the smaller half of it.
        /// </remarks>
        [Fact]
        public void A_first_install_is_still_welcomed_when_the_changelog_has_no_matching_entry()
        {
            var decision = LandingPageDecision.Decide("9.9.9", lastSeenVersion: null, Notes);

            Assert.Equal(LandingPageKind.Welcome, decision.Kind);
            Assert.Empty(decision.Entries);
        }

        [Fact]
        public void A_requested_page_shows_everything_up_to_the_running_build_newest_first()
        {
            var decision = LandingPageDecision.OnRequest("1.2.0", Notes);

            Assert.True(decision.ShouldShow);
            Assert.Equal(
                new[] { "1.2.0", "1.1.0", "1.0.0" },
                decision.Entries.Select(e => e.VersionText));
        }

        /// <remarks>
        /// An unreadable version is not a reason to refuse a page somebody asked for by name, so
        /// the upper bound falls away rather than the page being empty.
        /// </remarks>
        [Fact]
        public void A_requested_page_survives_a_version_it_cannot_read()
        {
            var decision = LandingPageDecision.OnRequest("not a version", Notes);

            Assert.Equal(4, decision.Entries.Count);
        }
    }
}
