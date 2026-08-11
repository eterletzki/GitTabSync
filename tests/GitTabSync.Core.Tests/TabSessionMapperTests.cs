using System;
using System.Collections.Generic;
using System.IO;
using GitTabSync.Model;
using GitTabSync.Sync;
using Xunit;

namespace GitTabSync.Tests
{
    public sealed class TabSessionMapperTests
    {
        private const string Repo = @"C:\src\MyRepo";

        private static readonly DateTime SavedAt = new DateTime(2026, 7, 27, 10, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void Files_inside_the_repository_are_stored_relative()
        {
            var tabs = new[] { new EditorTab(@"C:\src\MyRepo\src\A.cs", 5, 2) };

            var session = TabSessionMapper.ToSession(tabs, 0, Repo, "branch/main", SavedAt);

            Assert.True(session.Tabs[0].IsRepositoryRelative);
            Assert.Equal("src/A.cs", session.Tabs[0].Path);
            Assert.Equal(5, session.Tabs[0].CaretLine);
            Assert.Equal(2, session.Tabs[0].CaretColumn);
        }

        [Fact]
        public void Files_outside_the_repository_are_stored_absolute()
        {
            var tabs = new[] { new EditorTab(@"C:\other\B.cs") };

            var session = TabSessionMapper.ToSession(tabs, -1, Repo, "branch/main", SavedAt);

            Assert.False(session.Tabs[0].IsRepositoryRelative);
            Assert.Equal(@"C:\other\B.cs", session.Tabs[0].Path);
        }

        [Fact]
        public void A_sibling_directory_sharing_a_prefix_is_not_treated_as_inside_the_repository()
        {
            // "C:\src\MyRepoOther" starts with "C:\src\MyRepo" as a string but is a different
            // directory; a naive prefix check would mangle it into a relative path.
            var tabs = new[] { new EditorTab(@"C:\src\MyRepoOther\B.cs") };

            var session = TabSessionMapper.ToSession(tabs, -1, Repo, "branch/main", SavedAt);

            Assert.False(session.Tabs[0].IsRepositoryRelative);
        }

        [Fact]
        public void Repository_paths_are_matched_ignoring_case()
        {
            var tabs = new[] { new EditorTab(@"c:\SRC\myrepo\src\A.cs") };

            var session = TabSessionMapper.ToSession(tabs, 0, Repo, "branch/main", SavedAt);

            Assert.True(session.Tabs[0].IsRepositoryRelative);
            Assert.Equal("src/A.cs", session.Tabs[0].Path);
        }

        [Fact]
        public void The_pinned_state_survives_a_round_trip()
        {
            var original = new[]
            {
                new EditorTab(@"C:\src\MyRepo\A.cs", isPinned: true),
                new EditorTab(@"C:\src\MyRepo\B.cs"),
            };

            var session = TabSessionMapper.ToSession(original, 0, Repo, "branch/main", SavedAt);

            Assert.True(session.Tabs[0].IsPinned);
            Assert.False(session.Tabs[1].IsPinned);

            var restored = TabSessionMapper.ToEditorTabs(session, Repo, out _, _ => true);

            Assert.True(restored[0].IsPinned);
            Assert.False(restored[1].IsPinned);
        }

        [Fact]
        public void An_out_of_range_active_index_becomes_none()
        {
            var tabs = new[] { new EditorTab(@"C:\src\MyRepo\A.cs") };

            Assert.Equal(-1, TabSessionMapper.ToSession(tabs, 7, Repo, "branch/main", SavedAt).ActiveIndex);
            Assert.Equal(-1, TabSessionMapper.ToSession(tabs, -1, Repo, "branch/main", SavedAt).ActiveIndex);
        }

        [Fact]
        public void Relative_entries_are_resolved_against_the_repository()
        {
            var session = new TabSession
            {
                HeadKey = "branch/main",
                Tabs = { new TabEntry { Path = "src/A.cs", IsRepositoryRelative = true } },
            };

            var tabs = TabSessionMapper.ToEditorTabs(session, Repo, out _, _ => true);

            Assert.Equal(Path.Combine(Repo, "src", "A.cs"), tabs[0].AbsolutePath);
        }

        [Fact]
        public void Entries_whose_file_is_missing_are_dropped()
        {
            var session = new TabSession
            {
                HeadKey = "branch/main",
                Tabs =
                {
                    new TabEntry { Path = "src/Present.cs", IsRepositoryRelative = true },
                    new TabEntry { Path = "src/Gone.cs", IsRepositoryRelative = true },
                },
            };

            // The defining case: switching branches makes files appear and disappear, so a saved
            // session routinely names files that no longer exist.
            var tabs = TabSessionMapper.ToEditorTabs(
                session, Repo, out _, path => !path.EndsWith("Gone.cs", StringComparison.Ordinal));

            Assert.Single(tabs);
            Assert.EndsWith("Present.cs", tabs[0].AbsolutePath, StringComparison.Ordinal);
        }

        [Fact]
        public void The_active_index_follows_its_document_when_earlier_entries_are_dropped()
        {
            var session = new TabSession
            {
                HeadKey = "branch/main",
                ActiveIndex = 2,
                Tabs =
                {
                    new TabEntry { Path = "Gone1.cs", IsRepositoryRelative = true },
                    new TabEntry { Path = "Gone2.cs", IsRepositoryRelative = true },
                    new TabEntry { Path = "Active.cs", IsRepositoryRelative = true },
                },
            };

            var tabs = TabSessionMapper.ToEditorTabs(
                session, Repo, out var activeIndex, path => path.EndsWith("Active.cs", StringComparison.Ordinal));

            Assert.Single(tabs);
            Assert.Equal(0, activeIndex);
        }

        [Fact]
        public void The_active_index_becomes_none_when_its_document_is_dropped()
        {
            var session = new TabSession
            {
                HeadKey = "branch/main",
                ActiveIndex = 0,
                Tabs =
                {
                    new TabEntry { Path = "Gone.cs", IsRepositoryRelative = true },
                    new TabEntry { Path = "Present.cs", IsRepositoryRelative = true },
                },
            };

            var tabs = TabSessionMapper.ToEditorTabs(
                session, Repo, out var activeIndex, path => path.EndsWith("Present.cs", StringComparison.Ordinal));

            Assert.Single(tabs);
            Assert.Equal(-1, activeIndex);
        }

        [Fact]
        public void Blank_and_null_entries_are_skipped()
        {
            var session = new TabSession
            {
                HeadKey = "branch/main",
                Tabs = new List<TabEntry> { new TabEntry { Path = "  " }, null!, new TabEntry { Path = "A.cs", IsRepositoryRelative = true } },
            };

            var tabs = TabSessionMapper.ToEditorTabs(session, Repo, out _, _ => true);

            Assert.Single(tabs);
        }

        [Fact]
        public void Tab_order_is_preserved_through_a_round_trip()
        {
            var original = new[]
            {
                new EditorTab(@"C:\src\MyRepo\A.cs"),
                new EditorTab(@"C:\src\MyRepo\B.cs"),
                new EditorTab(@"C:\src\MyRepo\C.cs"),
            };

            var session = TabSessionMapper.ToSession(original, 1, Repo, "branch/main", SavedAt);
            var restored = TabSessionMapper.ToEditorTabs(session, Repo, out var activeIndex, _ => true);

            Assert.Equal(new[] { "A.cs", "B.cs", "C.cs" }, Names(restored));
            Assert.Equal(1, activeIndex);
        }

        private static string[] Names(IReadOnlyList<EditorTab> tabs)
        {
            var names = new string[tabs.Count];
            for (var i = 0; i < tabs.Count; i++)
            {
                names[i] = Path.GetFileName(tabs[i].AbsolutePath);
            }

            return names;
        }
    }
}
