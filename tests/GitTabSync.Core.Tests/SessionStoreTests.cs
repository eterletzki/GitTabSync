using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GitTabSync.Model;
using GitTabSync.Storage;
using GitTabSync.Tests.TestSupport;
using Xunit;

namespace GitTabSync.Tests
{
    public sealed class SessionStoreTests
    {
        [Fact]
        public void Round_trips_a_session()
        {
            using var temp = new TempDirectory();
            var store = new FileSessionStore(temp.Path);
            var session = new TabSession
            {
                HeadKey = "branch/feature/x",
                SavedAtUtc = "2026-07-27T10:00:00.0000000Z",
                ActiveIndex = 1,
                Tabs =
                {
                    new TabEntry { Path = "src/A.cs", IsRepositoryRelative = true, CaretLine = 12, CaretColumn = 3 },
                    new TabEntry { Path = @"C:\elsewhere\B.cs", IsRepositoryRelative = false },
                },
            };

            store.Save(@"C:\repo", session);
            var loaded = store.Load(@"C:\repo", "branch/feature/x");

            Assert.NotNull(loaded);
            Assert.Equal(2, loaded!.Tabs.Count);
            Assert.Equal(1, loaded.ActiveIndex);
            Assert.Equal("src/A.cs", loaded.Tabs[0].Path);
            Assert.True(loaded.Tabs[0].IsRepositoryRelative);
            Assert.Equal(12, loaded.Tabs[0].CaretLine);
            Assert.Equal(3, loaded.Tabs[0].CaretColumn);
            Assert.False(loaded.Tabs[1].IsRepositoryRelative);
        }

        [Fact]
        public void Round_trips_the_pinned_state()
        {
            using var temp = new TempDirectory();
            var store = new FileSessionStore(temp.Path);

            store.Save(@"C:\repo", new TabSession
            {
                HeadKey = "branch/main",
                Tabs =
                {
                    new TabEntry { Path = "src/Pinned.cs", IsRepositoryRelative = true, IsPinned = true },
                    new TabEntry { Path = "src/Loose.cs", IsRepositoryRelative = true },
                },
            });

            var loaded = store.Load(@"C:\repo", "branch/main");

            Assert.True(loaded!.Tabs[0].IsPinned);
            Assert.False(loaded.Tabs[1].IsPinned);
        }

        [Fact]
        public void A_session_written_before_pinning_existed_loads_as_unpinned()
        {
            using var temp = new TempDirectory();
            var store = new FileSessionStore(temp.Path);
            store.Save(@"C:\repo", new TabSession { HeadKey = "branch/main" });

            // "pinned" was appended to the tab contract without bumping the schema version, so
            // files written by an earlier build must still load rather than being discarded.
            File.WriteAllText(
                store.GetSessionFilePath(@"C:\repo", "branch/main"),
                @"{""schema"":1,""head"":""branch\/main"",""savedAtUtc"":""2026-08-09T10:14:32.1174820Z"",""activeIndex"":0,"
                    + @"""tabs"":[{""path"":""src\/A.cs"",""relative"":true,""line"":42,""column"":9}]}");

            var loaded = store.Load(@"C:\repo", "branch/main");

            Assert.Equal("src/A.cs", loaded!.Tabs.Single().Path);
            Assert.Equal(42, loaded.Tabs[0].CaretLine);
            Assert.False(loaded.Tabs[0].IsPinned);
        }

        [Fact]
        public void Load_returns_null_when_nothing_was_saved()
        {
            using var temp = new TempDirectory();
            var store = new FileSessionStore(temp.Path);

            Assert.Null(store.Load(@"C:\repo", "branch/main"));
        }

        [Fact]
        public void Load_returns_null_for_a_corrupt_file_instead_of_throwing()
        {
            using var temp = new TempDirectory();
            var store = new FileSessionStore(temp.Path);
            store.Save(@"C:\repo", new TabSession { HeadKey = "branch/main" });

            var path = store.GetSessionFilePath(@"C:\repo", "branch/main");
            File.WriteAllText(path, "{ this is not json");

            // Losing a remembered tab set is recoverable; throwing during a branch switch is not.
            Assert.Null(store.Load(@"C:\repo", "branch/main"));
        }

        [Fact]
        public void Load_ignores_a_session_written_by_a_newer_schema()
        {
            using var temp = new TempDirectory();
            var store = new FileSessionStore(temp.Path);

            store.Save(@"C:\repo", new TabSession
            {
                SchemaVersion = TabSession.CurrentSchemaVersion + 1,
                HeadKey = "branch/main",
            });

            Assert.Null(store.Load(@"C:\repo", "branch/main"));
        }

        [Fact]
        public void Save_overwrites_a_previous_session_for_the_same_branch()
        {
            using var temp = new TempDirectory();
            var store = new FileSessionStore(temp.Path);

            store.Save(@"C:\repo", new TabSession
            {
                HeadKey = "branch/main",
                Tabs = { new TabEntry { Path = "A.cs" }, new TabEntry { Path = "B.cs" } },
            });
            store.Save(@"C:\repo", new TabSession
            {
                HeadKey = "branch/main",
                Tabs = { new TabEntry { Path = "C.cs" } },
            });

            var loaded = store.Load(@"C:\repo", "branch/main");

            Assert.Single(loaded!.Tabs);
            Assert.Equal("C.cs", loaded.Tabs[0].Path);
        }

        [Fact]
        public void Save_leaves_no_temporary_file_behind()
        {
            using var temp = new TempDirectory();
            var store = new FileSessionStore(temp.Path);

            store.Save(@"C:\repo", new TabSession { HeadKey = "branch/main" });
            store.Save(@"C:\repo", new TabSession { HeadKey = "branch/main" });

            var directory = Path.GetDirectoryName(store.GetSessionFilePath(@"C:\repo", "branch/main"))!;
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }

        [Fact]
        public void Delete_removes_a_session_and_tolerates_a_missing_one()
        {
            using var temp = new TempDirectory();
            var store = new FileSessionStore(temp.Path);
            store.Save(@"C:\repo", new TabSession { HeadKey = "branch/main" });

            store.Delete(@"C:\repo", "branch/main");
            store.Delete(@"C:\repo", "branch/main");

            Assert.Null(store.Load(@"C:\repo", "branch/main"));
        }

        [Fact]
        public void Different_branches_of_one_repository_are_stored_separately()
        {
            using var temp = new TempDirectory();
            var store = new FileSessionStore(temp.Path);

            store.Save(@"C:\repo", new TabSession { HeadKey = "branch/main", Tabs = { new TabEntry { Path = "A.cs" } } });
            store.Save(@"C:\repo", new TabSession { HeadKey = "branch/dev", Tabs = { new TabEntry { Path = "B.cs" } } });

            Assert.Equal("A.cs", store.Load(@"C:\repo", "branch/main")!.Tabs[0].Path);
            Assert.Equal("B.cs", store.Load(@"C:\repo", "branch/dev")!.Tabs[0].Path);
        }

        [Fact]
        public void The_same_branch_in_different_repositories_is_stored_separately()
        {
            using var temp = new TempDirectory();
            var store = new FileSessionStore(temp.Path);

            store.Save(@"C:\one", new TabSession { HeadKey = "branch/main", Tabs = { new TabEntry { Path = "A.cs" } } });
            store.Save(@"C:\two", new TabSession { HeadKey = "branch/main", Tabs = { new TabEntry { Path = "B.cs" } } });

            Assert.Equal("A.cs", store.Load(@"C:\one", "branch/main")!.Tabs[0].Path);
            Assert.Equal("B.cs", store.Load(@"C:\two", "branch/main")!.Tabs[0].Path);
        }

        [Fact]
        public void Sessions_are_stored_outside_the_repository()
        {
            using var repo = new TempDirectory("repo");
            using var storage = new TempDirectory("store");
            var store = new FileSessionStore(storage.Path);

            store.Save(repo.Path, new TabSession { HeadKey = "branch/main" });

            // Anything written inside the working tree would be rewritten by the very checkout
            // the session exists to survive, and would show up as a pending change.
            Assert.Empty(Directory.GetFileSystemEntries(repo.Path));
        }

        public sealed class Keys
        {
            [Fact]
            public void Branch_names_that_sanitise_to_the_same_text_get_different_files()
            {
                using var temp = new TempDirectory();
                var store = new FileSessionStore(temp.Path);

                // "feature/x" and "feature-x" both contain characters that must be replaced to
                // form a file name; only the hash keeps them apart.
                var a = store.GetSessionFilePath(@"C:\repo", "branch/feature/x");
                var b = store.GetSessionFilePath(@"C:\repo", "branch/feature-x");

                Assert.NotEqual(a, b);
            }

            [Fact]
            public void Branch_names_differing_only_by_case_get_different_files()
            {
                using var temp = new TempDirectory();
                var store = new FileSessionStore(temp.Path);

                // Git ref names are case-sensitive even on Windows, so these are two branches.
                var a = store.GetSessionFilePath(@"C:\repo", "branch/Feature");
                var b = store.GetSessionFilePath(@"C:\repo", "branch/feature");

                Assert.NotEqual(a, b);
            }

            [Fact]
            public void Repository_paths_differing_only_by_case_or_trailing_slash_share_a_directory()
            {
                using var temp = new TempDirectory();
                var store = new FileSessionStore(temp.Path);

                // Windows paths are case-insensitive; the same repository must not end up with
                // two caches because it was reported with different casing.
                var a = store.GetSessionFilePath(@"C:\Repo", "branch/main");
                var b = store.GetSessionFilePath(@"c:\repo\", "branch/main");

                Assert.Equal(a, b);
            }

            [Fact]
            public void A_very_long_branch_name_produces_a_usable_file_name()
            {
                using var temp = new TempDirectory();
                var store = new FileSessionStore(temp.Path);
                var longName = "branch/" + new string('x', 400);

                var path = store.GetSessionFilePath(@"C:\repo", longName);

                Assert.True(Path.GetFileName(path).Length < 80);
                store.Save(@"C:\repo", new TabSession { HeadKey = longName });
                Assert.NotNull(store.Load(@"C:\repo", longName));
            }

            [Theory]
            [InlineData("branch/feature:x")]
            [InlineData("branch/feature*x")]
            [InlineData("branch/..")]
            [InlineData("detached/abc")]
            public void Awkward_branch_names_stay_inside_the_storage_root(string headKey)
            {
                using var temp = new TempDirectory();
                var store = new FileSessionStore(temp.Path);

                var path = Path.GetFullPath(store.GetSessionFilePath(@"C:\repo", headKey));

                Assert.StartsWith(Path.GetFullPath(temp.Path), path, StringComparison.OrdinalIgnoreCase);
            }
        }

        public sealed class Json
        {
            [Fact]
            public void Is_written_as_readable_field_names()
            {
                var session = new TabSession
                {
                    HeadKey = "branch/main",
                    Tabs = { new TabEntry { Path = "src/A.cs", IsRepositoryRelative = true } },
                };

                var json = FileSessionStore.ToJson(session);

                Assert.Contains("\"head\"", json, StringComparison.Ordinal);
                Assert.Contains("\"tabs\"", json, StringComparison.Ordinal);

                // DataContractJsonSerializer escapes '/' as '\/'. That is valid JSON and
                // round-trips through any parser, so it is left alone rather than post-processed.
                Assert.Contains(@"src\/A.cs", json, StringComparison.Ordinal);
            }

            [Fact]
            public void Survives_a_path_containing_characters_that_need_escaping()
            {
                using var temp = new TempDirectory();
                var store = new FileSessionStore(temp.Path);
                const string awkward = @"src\folder with spaces\ümlaut ""quoted"".cs";

                store.Save(@"C:\repo", new TabSession
                {
                    HeadKey = "branch/main",
                    Tabs = new List<TabEntry> { new TabEntry { Path = awkward } },
                });

                Assert.Equal(awkward, store.Load(@"C:\repo", "branch/main")!.Tabs[0].Path);
            }
        }
    }
}
