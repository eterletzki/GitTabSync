using System.Linq;
using GitTabSync.Release;
using Xunit;

namespace GitTabSync.Tests
{
    /// <summary>
    /// All of the page's wording is here rather than in the window, for the reason
    /// <see cref="SettingsViewModelTests"/> exists: the Visual Studio layer has no coverage at
    /// all, so anything worth getting right has to be on this side of the seam.
    /// </summary>
    public class LandingPageViewModelTests
    {
        private static readonly ReleaseNotes Notes = ChangelogParser.Parse(
            "## 1.3.0 — 2026-02-01\n\n### Added\n\n- Three\n\n"
            + "## 1.2.0 — 2026-01-01\n\n### Fixed\n\n- Two\n\n"
            + "## 1.1.0\n\n- One\n");

        private static LandingPageViewModel Welcome(string version = "1.2.0") =>
            new LandingPageViewModel(LandingPageDecision.Decide(version, null, Notes));

        private static LandingPageViewModel Update(string from = "1.1.0", string to = "1.3.0") =>
            new LandingPageViewModel(LandingPageDecision.Decide(to, from, Notes));

        [Fact]
        public void A_welcome_is_titled_for_the_extension_not_for_a_version()
        {
            Assert.Equal("Git Tab Sync", Welcome().Title);
        }

        [Fact]
        public void An_update_is_titled_for_the_version()
        {
            Assert.Equal("What's new in 1.3.0", Update().Title);
        }

        /// <remarks>
        /// A list of changes means nothing to somebody who has never seen the thing being changed.
        /// </remarks>
        [Fact]
        public void A_welcome_says_what_the_extension_does()
        {
            var text = Welcome().IntroText;

            Assert.Contains("remembers which documents", text);
            Assert.Contains("outside Visual", text);
        }

        [Fact]
        public void An_update_names_the_version_rather_than_re_explaining_the_extension()
        {
            var text = Update().IntroText;

            Assert.Contains("1.3.0", text);
            Assert.DoesNotContain("remembers which documents", text);
        }

        /// <remarks>
        /// Somebody upgrading has had the settings window all along. Repeating where it lives on
        /// every update is how a page like this becomes one people close without reading.
        /// </remarks>
        [Fact]
        public void The_settings_hint_is_shown_on_a_first_install_only()
        {
            Assert.NotEqual(string.Empty, Welcome().SettingsHintText);
            Assert.Equal(string.Empty, Update().SettingsHintText);
        }

        [Fact]
        public void Where_to_reopen_the_page_is_always_said()
        {
            Assert.Contains(LandingPageViewModel.MenuPath, Welcome().ReopenHintText);
            Assert.Contains(LandingPageViewModel.MenuPath, Update().ReopenHintText);
        }

        [Fact]
        public void A_welcome_shows_the_current_release_under_a_heading_of_its_own()
        {
            var model = Welcome();

            Assert.True(model.HasReleases);
            Assert.True(model.HasReleasesHeading);
            Assert.Equal("In this release", model.ReleasesHeading);
        }

        /// <remarks>
        /// The title has already said which version this is.
        /// </remarks>
        [Fact]
        public void One_release_needs_no_version_heading_above_it()
        {
            Assert.False(Assert.Single(Update("1.2.0", "1.3.0").Releases).ShowsHeading);
        }

        [Fact]
        public void Several_releases_each_get_a_heading()
        {
            var model = Update("1.1.0", "1.3.0");

            Assert.Equal(2, model.Releases.Count);
            Assert.All(model.Releases, r => Assert.True(r.ShowsHeading));
        }

        [Fact]
        public void A_release_heading_carries_its_date_when_it_has_one()
        {
            var model = Update("1.1.0", "1.3.0");

            Assert.Equal("1.3.0 — 2026-02-01", model.Releases[0].Heading);
        }

        [Fact]
        public void A_release_with_no_date_is_headed_by_its_version_alone()
        {
            var model = new LandingPageViewModel(LandingPageDecision.OnRequest("1.3.0", Notes));

            Assert.Equal("1.1.0", model.Releases.Single(r => r.Heading.StartsWith("1.1")).Heading);
        }

        [Fact]
        public void Releases_are_newest_first()
        {
            var model = Update("1.1.0", "1.3.0");

            Assert.StartsWith("1.3.0", model.Releases[0].Heading);
            Assert.StartsWith("1.2.0", model.Releases[1].Heading);
        }

        [Fact]
        public void The_groups_of_a_release_come_through_intact()
        {
            var group = Assert.Single(Assert.Single(Update("1.2.0", "1.3.0").Releases).Groups);

            Assert.Equal("Added", group.Heading);
            Assert.Equal(new[] { "Three" }, group.Changes);
        }

        /// <remarks>
        /// A first install of a build the changelog says nothing about still gets its
        /// introduction; what it must not get is a heading over an empty list.
        /// </remarks>
        [Fact]
        public void A_page_with_no_releases_shows_no_heading_over_them()
        {
            var model = new LandingPageViewModel(LandingPageDecision.Decide("9.9.9", null, Notes));

            Assert.False(model.HasReleases);
            Assert.False(model.HasReleasesHeading);
            Assert.Equal(string.Empty, model.ReleasesHeading);
            Assert.NotEqual(string.Empty, model.IntroText);
        }

        [Fact]
        public void The_page_offers_a_way_to_the_settings_and_to_the_project()
        {
            var model = Welcome();

            Assert.NotEqual(string.Empty, model.SettingsButtonText);
            Assert.NotEqual(string.Empty, model.ProjectLinkText);
            Assert.StartsWith("https://", LandingPageViewModel.ProjectUrl);
        }
    }
}
