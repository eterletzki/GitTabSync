using System.Linq;
using GitTabSync.Release;
using Xunit;

namespace GitTabSync.Tests
{
    /// <summary>
    /// What this build says it is, and whether its changelog agrees.
    /// </summary>
    /// <remarks>
    /// These run against the real embedded resource and the real assembly version rather than
    /// against fixtures, because what they are for is catching a release that was half finished —
    /// and a fixture cannot be half finished on anyone's behalf.
    /// </remarks>
    public class ShippedReleaseTests
    {
        [Fact]
        public void The_changelog_is_embedded_in_the_assembly()
        {
            var markdown = ShippedRelease.ReadChangelog();

            Assert.False(string.IsNullOrWhiteSpace(markdown));
        }

        [Fact]
        public void The_build_knows_its_own_version()
        {
            Assert.True(ReleaseVersion.TryParse(ShippedRelease.Version, out _));
        }

        /// <remarks>
        /// Build metadata is not part of what was released, and this string is both shown to the
        /// user and written to their state file.
        /// </remarks>
        [Fact]
        public void The_version_carries_no_commit_suffix()
        {
            Assert.DoesNotContain('+', ShippedRelease.Version);
        }

        [Fact]
        public void The_shipped_changelog_parses_to_at_least_one_release()
        {
            Assert.False(ShippedRelease.Notes.IsEmpty);
        }

        /// <summary>
        /// The half of the release check that lives in the tests. Its other half is the
        /// VerifyManifestVersion target in the VSIX project, which compares the same
        /// <c>$(Version)</c> against the VSIX manifest. Together they mean a release with no notes
        /// written, or with a manifest nobody updated, fails the build rather than shipping a
        /// What's New page that has nothing on it.
        /// </summary>
        [Fact]
        public void The_shipped_changelog_has_an_entry_for_the_version_being_shipped()
        {
            Assert.True(
                ReleaseVersion.TryParse(ShippedRelease.Version, out var current),
                "This build's version could not be parsed: " + ShippedRelease.Version);

            Assert.True(
                ShippedRelease.Notes.Entries.Any(entry => entry.Version == current),
                "CHANGELOG.md has no entry for " + ShippedRelease.Version
                + ". Releasing means renaming the Unreleased heading to this version, not only"
                + " changing Version in Directory.Build.props. The entries it does have are: "
                + string.Join(", ", ShippedRelease.Notes.Entries.Select(e => e.VersionText)) + ".");
        }

        /// <remarks>
        /// The page it produces would list a release the user does not have, which is the one
        /// thing the upper bound in <see cref="ReleaseNotes.Between"/> exists to prevent — but a
        /// changelog written that far ahead is a mistake worth naming rather than quietly
        /// trimming.
        /// </remarks>
        [Fact]
        public void The_changelog_describes_no_release_newer_than_this_build()
        {
            Assert.True(ReleaseVersion.TryParse(ShippedRelease.Version, out var current));

            var ahead = ShippedRelease.Notes.Entries
                .Where(entry => entry.Version > current)
                .Select(entry => entry.VersionText)
                .ToArray();

            Assert.True(
                ahead.Length == 0,
                "CHANGELOG.md describes " + string.Join(", ", ahead) + ", which is newer than this"
                + " build (" + ShippedRelease.Version + "). Unreleased work belongs under the"
                + " Unreleased heading, which is skipped, not under a version number.");
        }

        /// <remarks>
        /// Not a style rule: an Unreleased section is the one place work in progress can be
        /// described without the page promising it, so losing the convention would mean either
        /// nowhere to write it or a page that lies.
        /// </remarks>
        [Fact]
        public void Work_in_progress_is_kept_out_of_the_parsed_releases()
        {
            var markdown = ShippedRelease.ReadChangelog()!;

            if (!markdown.Contains("## Unreleased"))
            {
                return;
            }

            Assert.DoesNotContain(ShippedRelease.Notes.Entries, e => e.VersionText == "Unreleased");
        }
    }
}
