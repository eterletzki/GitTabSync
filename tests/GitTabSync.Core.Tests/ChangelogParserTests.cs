using System.Linq;
using GitTabSync.Release;
using Xunit;

namespace GitTabSync.Tests
{
    /// <summary>
    /// The changelog is a document people write by hand, so most of these are about what the
    /// parser is willing to ignore. It reads three things — a release heading, a group heading and
    /// a bullet — and everything else in the file is prose.
    /// </summary>
    public class ChangelogParserTests
    {
        [Fact]
        public void A_release_heading_with_a_date_is_split_into_both()
        {
            var notes = ChangelogParser.Parse("## 1.2.0 — 2026-01-14\n\n- Something\n");

            var entry = Assert.Single(notes.Entries);
            Assert.Equal("1.2.0", entry.VersionText);
            Assert.Equal("2026-01-14", entry.Date);
        }

        [Theory]
        [InlineData("## 1.2.0 - 2026-01-14")]
        [InlineData("## 1.2.0 — 2026-01-14")]
        [InlineData("## 1.2.0 – 2026-01-14")]
        [InlineData("## 1.2.0: 2026-01-14")]
        [InlineData("## 1.2.0 2026-01-14")]
        [InlineData("## [1.2.0] - 2026-01-14")]
        [InlineData("## v1.2.0 - 2026-01-14")]
        public void The_separator_between_a_version_and_its_date_is_not_prescribed(string heading)
        {
            var notes = ChangelogParser.Parse(heading + "\n\n- Something\n");

            var entry = Assert.Single(notes.Entries);
            Assert.Equal(new System.Version(1, 2, 0, 0), entry.Version);
            Assert.Equal("2026-01-14", entry.Date);
        }

        [Fact]
        public void A_release_with_no_date_keeps_an_empty_one_rather_than_inventing_one()
        {
            var notes = ChangelogParser.Parse("## 1.2.0\n\n- Something\n");

            Assert.Equal(string.Empty, Assert.Single(notes.Entries).Date);
        }

        [Fact]
        public void Changes_are_grouped_under_their_headings()
        {
            var notes = ChangelogParser.Parse(
                "## 1.2.0\n\n### Added\n\n- One\n- Two\n\n### Fixed\n\n- Three\n");

            var entry = Assert.Single(notes.Entries);
            Assert.Equal(2, entry.Groups.Count);
            Assert.Equal("Added", entry.Groups[0].Heading);
            Assert.Equal(new[] { "One", "Two" }, entry.Groups[0].Changes);
            Assert.Equal("Fixed", entry.Groups[1].Heading);
            Assert.Equal(new[] { "Three" }, entry.Groups[1].Changes);
        }

        [Fact]
        public void Changes_listed_without_a_heading_form_a_group_with_none()
        {
            var notes = ChangelogParser.Parse("## 1.2.0\n\n- One\n- Two\n");

            var group = Assert.Single(Assert.Single(notes.Entries).Groups);
            Assert.False(group.HasHeading);
            Assert.Equal(new[] { "One", "Two" }, group.Changes);
        }

        [Theory]
        [InlineData("-")]
        [InlineData("*")]
        [InlineData("+")]
        public void All_three_bullet_characters_begin_a_change(string bullet)
        {
            var notes = ChangelogParser.Parse("## 1.2.0\n\n" + bullet + " Something\n");

            Assert.Equal(
                new[] { "Something" },
                Assert.Single(Assert.Single(notes.Entries).Groups).Changes);
        }

        [Fact]
        public void An_indented_line_continues_the_change_above_it_joined_by_a_space()
        {
            var notes = ChangelogParser.Parse(
                "## 1.2.0\n\n- A change that ran past\n  the margin and wrapped.\n");

            Assert.Equal(
                new[] { "A change that ran past the margin and wrapped." },
                Assert.Single(Assert.Single(notes.Entries).Groups).Changes);
        }

        [Fact]
        public void A_blank_line_ends_a_change_so_indented_prose_after_it_is_not_swallowed()
        {
            var notes = ChangelogParser.Parse(
                "## 1.2.0\n\n- A change\n\n  An indented paragraph that is not part of it.\n");

            Assert.Equal(
                new[] { "A change" },
                Assert.Single(Assert.Single(notes.Entries).Groups).Changes);
        }

        [Fact]
        public void Prose_between_releases_is_ignored()
        {
            var notes = ChangelogParser.Parse(
                "# Changelog\n\nNotable changes, newest first.\n\n"
                + "## 1.2.0\n\nA sentence introducing the release.\n\n- One\n");

            var entry = Assert.Single(notes.Entries);
            Assert.Equal(new[] { "One" }, Assert.Single(entry.Groups).Changes);
        }

        /// <remarks>
        /// The rule the page depends on most. Every item in an Unreleased section is a promise the
        /// installed build cannot keep, so the section is skipped along with everything under it —
        /// and in particular its items are not attached to whichever release follows it, which is
        /// what a parser that merely ignored the heading would do.
        /// </remarks>
        [Fact]
        public void An_unreleased_section_is_skipped_with_its_contents()
        {
            var notes = ChangelogParser.Parse(
                "## Unreleased\n\n### Added\n\n- Not shipped yet\n\n## 1.2.0\n\n- Shipped\n");

            var entry = Assert.Single(notes.Entries);
            Assert.Equal("1.2.0", entry.VersionText);
            Assert.Equal(new[] { "Shipped" }, Assert.Single(entry.Groups).Changes);
        }

        [Fact]
        public void A_heading_that_names_no_version_is_skipped_whatever_it_says()
        {
            var notes = ChangelogParser.Parse("## Work in progress\n\n- Something\n");

            Assert.True(notes.IsEmpty);
        }

        [Fact]
        public void A_group_with_no_changes_under_it_is_dropped_rather_than_shown_empty()
        {
            var notes = ChangelogParser.Parse("## 1.2.0\n\n### Added\n\n### Fixed\n\n- Three\n");

            var group = Assert.Single(Assert.Single(notes.Entries).Groups);
            Assert.Equal("Fixed", group.Heading);
        }

        [Fact]
        public void Deeper_headings_are_prose_not_groups()
        {
            var notes = ChangelogParser.Parse("## 1.2.0\n\n- One\n\n#### An aside\n\n- Two\n");

            var entry = Assert.Single(notes.Entries);
            Assert.Equal(new[] { "One", "Two" }, Assert.Single(entry.Groups).Changes);
        }

        [Fact]
        public void Releases_are_kept_in_file_order()
        {
            var notes = ChangelogParser.Parse("## 1.2.0\n\n- Two\n\n## 1.1.0\n\n- One\n");

            Assert.Equal(new[] { "1.2.0", "1.1.0" }, notes.Entries.Select(e => e.VersionText));
        }

        [Fact]
        public void Carriage_returns_do_not_become_part_of_the_text()
        {
            var notes = ChangelogParser.Parse("## 1.2.0\r\n\r\n### Added\r\n\r\n- Something\r\n");

            var group = Assert.Single(Assert.Single(notes.Entries).Groups);
            Assert.Equal("Added", group.Heading);
            Assert.Equal(new[] { "Something" }, group.Changes);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   \n\n  ")]
        [InlineData("# Changelog\n\nNothing has been released yet.\n")]
        public void A_file_with_no_releases_in_it_parses_to_nothing(string? markdown)
        {
            Assert.True(ChangelogParser.Parse(markdown).IsEmpty);
        }

        [Fact]
        public void A_bullet_before_any_release_belongs_to_no_release()
        {
            var notes = ChangelogParser.Parse(
                "# Changelog\n\n- A note about the file itself\n\n## 1.2.0\n\n- One\n");

            Assert.Equal(
                new[] { "One" },
                Assert.Single(Assert.Single(notes.Entries).Groups).Changes);
        }

        /// <remarks>
        /// By comparing, not by taking the first entry: a changelog is hand-edited, and an entry
        /// added in the wrong place should not change what "latest" means.
        /// </remarks>
        [Fact]
        public void Latest_is_the_highest_version_not_the_first_one_written()
        {
            var notes = ChangelogParser.Parse("## 1.1.0\n\n- One\n\n## 1.3.0\n\n- Three\n");

            Assert.Equal("1.3.0", notes.Latest!.VersionText);
        }

        [Fact]
        public void Latest_is_null_when_there_is_nothing_to_be_latest()
        {
            Assert.Null(ReleaseNotes.Empty.Latest);
        }
    }
}
