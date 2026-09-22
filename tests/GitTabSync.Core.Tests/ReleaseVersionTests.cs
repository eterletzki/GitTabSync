using System;
using GitTabSync.Release;
using Xunit;

namespace GitTabSync.Tests
{
    /// <summary>
    /// Versions reach this extension from three places written by three different hands — the
    /// assembly's informational version, a heading in CHANGELOG.md, and the state file from
    /// whenever the user last saw the page. They have to order consistently or an upgrade shows
    /// the wrong notes, which is why there is one parse rather than three.
    /// </summary>
    public class ReleaseVersionTests
    {
        [Theory]
        [InlineData("1.2.3")]
        [InlineData(" 1.2.3 ")]
        [InlineData("v1.2.3")]
        [InlineData("V1.2.3")]
        [InlineData("[1.2.3]")]
        [InlineData("(1.2.3)")]
        public void The_ways_people_write_a_version_all_parse_to_the_same_one(string text)
        {
            Assert.True(ReleaseVersion.TryParse(text, out var version));
            Assert.Equal(new Version(1, 2, 3, 0), version);
        }

        /// <remarks>
        /// <see cref="Version"/> leaves unspecified components at -1, which sorts below 0. Without
        /// normalising, "1.1" would compare as older than "1.1.0" and upgrading between two
        /// spellings of the same release would announce notes the user had already seen.
        /// </remarks>
        [Fact]
        public void Two_spellings_of_the_same_version_are_equal()
        {
            Assert.True(ReleaseVersion.TryParse("1.1", out var shortForm));
            Assert.True(ReleaseVersion.TryParse("1.1.0", out var longForm));
            Assert.True(ReleaseVersion.TryParse("1.1.0.0", out var longestForm));

            Assert.Equal(longForm, shortForm);
            Assert.Equal(longestForm, shortForm);
            Assert.False(shortForm < longForm);
            Assert.False(longForm < shortForm);
        }

        [Theory]
        [InlineData("1.2.0-beta.1")]
        [InlineData("1.2.0-rc1")]
        [InlineData("1.2.0+abc123")]
        [InlineData("1.2.0-beta+abc123")]
        public void A_pre_release_or_metadata_suffix_is_the_release_it_belongs_to(string text)
        {
            Assert.True(ReleaseVersion.TryParse(text, out var version));
            Assert.Equal(new Version(1, 2, 0, 0), version);
        }

        /// <remarks>
        /// Failing rather than returning a version of zero is the point. Zero would read as an
        /// ancient install and announce the entire history; "no information" is what every caller
        /// actually needs to hear.
        /// </remarks>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("Unreleased")]
        [InlineData("Work in progress")]
        [InlineData("v")]
        [InlineData("-1.2.0")]
        [InlineData("1.2.0.0.0")]
        public void Anything_that_is_not_a_version_is_no_information_rather_than_zero(string? text)
        {
            Assert.False(ReleaseVersion.TryParse(text, out _));
        }

        [Fact]
        public void Ordering_is_numeric_not_lexicographic()
        {
            Assert.True(ReleaseVersion.TryParse("1.9.0", out var nine));
            Assert.True(ReleaseVersion.TryParse("1.10.0", out var ten));

            Assert.True(ten > nine);
        }
    }
}
