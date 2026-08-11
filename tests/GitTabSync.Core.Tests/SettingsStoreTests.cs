using System;
using System.IO;
using System.Linq;
using GitTabSync.Settings;
using GitTabSync.Tests.TestSupport;
using Xunit;

namespace GitTabSync.Tests
{
    public sealed class SettingsStoreTests
    {
        private static ScopedSettings Branch(string headKey, SyncSetting setting, bool on) =>
            new ScopedSettings
            {
                Scopes =
                {
                    new ScopeOverrides
                    {
                        Kind = nameof(SettingScopeKind.Branch),
                        HeadKey = headKey,
                        Values = { new SettingOverride { Setting = SyncSettingCatalog.NameOf(setting), On = on } },
                    },
                },
            };

        [Fact]
        public void Round_trips_a_repository_document()
        {
            using var temp = new TempDirectory();
            var store = new FileSettingsStore(temp.Path);

            store.Save(@"C:\repo", Branch("branch/release/1.0.1", SyncSetting.SyncBookmarks, on: true));
            var loaded = store.Load(@"C:\repo");

            var scope = Assert.Single(loaded.Scopes);
            Assert.Equal("Branch", scope.Kind);
            Assert.Equal("branch/release/1.0.1", scope.HeadKey);
            Assert.Equal("syncBookmarks", scope.Values.Single().Setting);
            Assert.True(scope.Values.Single().On);
        }

        [Fact]
        public void Round_trips_the_defaults_document()
        {
            using var temp = new TempDirectory();
            var store = new FileSettingsStore(temp.Path);

            store.SaveDefaults(new ScopedSettings
            {
                Scopes =
                {
                    new ScopeOverrides
                    {
                        Kind = nameof(SettingScopeKind.Global),
                        Values = { new SettingOverride { Setting = "syncTabs", On = false } },
                    },
                },
            });

            Assert.False(store.LoadDefaults().Scopes.Single().Values.Single().On);
        }

        [Fact]
        public void The_defaults_and_a_repository_are_separate_documents()
        {
            using var temp = new TempDirectory();
            var store = new FileSettingsStore(temp.Path);

            store.Save(@"C:\repo", Branch("branch/main", SyncSetting.SyncTabs, on: false));

            // Defaults outlive any clone; branch overrides are meaningless without the repository
            // that names them. Writing one must not disturb the other.
            Assert.Empty(store.LoadDefaults().Scopes);
        }

        [Fact]
        public void Two_repositories_do_not_share_overrides()
        {
            using var temp = new TempDirectory();
            var store = new FileSettingsStore(temp.Path);

            store.Save(@"C:\one", Branch("branch/main", SyncSetting.SyncTabs, on: false));

            Assert.Empty(store.Load(@"C:\two").Scopes);
        }

        [Fact]
        public void Load_returns_an_empty_document_when_nothing_was_saved()
        {
            using var temp = new TempDirectory();
            var store = new FileSettingsStore(temp.Path);

            // Not null: "no overrides" is the normal state, and every caller null-checking it
            // buys nothing.
            Assert.Empty(store.Load(@"C:\repo").Scopes);
            Assert.Empty(store.LoadDefaults().Scopes);
        }

        [Fact]
        public void Load_falls_back_to_the_defaults_for_a_corrupt_file_instead_of_throwing()
        {
            using var temp = new TempDirectory();
            var store = new FileSettingsStore(temp.Path);
            store.Save(@"C:\repo", Branch("branch/main", SyncSetting.SyncTabs, on: false));

            File.WriteAllText(store.GetRepositoryFilePath(@"C:\repo"), "{ this is not json");

            // Running on the built-in defaults is recoverable; refusing to run is not.
            Assert.Empty(store.Load(@"C:\repo").Scopes);
        }

        [Fact]
        public void Load_ignores_a_document_written_by_a_newer_schema()
        {
            using var temp = new TempDirectory();
            var store = new FileSettingsStore(temp.Path);

            var document = Branch("branch/main", SyncSetting.SyncTabs, on: false);
            document.SchemaVersion = ScopedSettings.CurrentSchemaVersion + 1;
            store.Save(@"C:\repo", document);

            Assert.Empty(store.Load(@"C:\repo").Scopes);
        }

        [Fact]
        public void Save_overwrites_the_previous_document()
        {
            using var temp = new TempDirectory();
            var store = new FileSettingsStore(temp.Path);

            store.Save(@"C:\repo", Branch("branch/main", SyncSetting.SyncTabs, on: false));
            store.Save(@"C:\repo", Branch("branch/dev", SyncSetting.SyncBookmarks, on: true));

            Assert.Equal("branch/dev", store.Load(@"C:\repo").Scopes.Single().HeadKey);
        }

        [Fact]
        public void Save_leaves_no_temporary_file_behind()
        {
            using var temp = new TempDirectory();
            var store = new FileSettingsStore(temp.Path);

            store.Save(@"C:\repo", Branch("branch/main", SyncSetting.SyncTabs, on: false));
            store.Save(@"C:\repo", Branch("branch/main", SyncSetting.SyncTabs, on: true));

            var directory = Path.GetDirectoryName(store.GetRepositoryFilePath(@"C:\repo"))!;
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }

        [Fact]
        public void The_settings_file_sits_beside_the_sessions_and_cannot_collide_with_one()
        {
            using var temp = new TempDirectory();
            var settings = new FileSettingsStore(temp.Path);
            var sessions = new Storage.FileSessionStore(temp.Path);

            var settingsPath = settings.GetRepositoryFilePath(@"C:\repo");

            // StorageKey.ForHead always appends a hash, so no branch — not even one literally
            // named "settings" — can produce the settings file's name.
            Assert.Equal(
                Path.GetDirectoryName(sessions.GetSessionFilePath(@"C:\repo", "branch/main")),
                Path.GetDirectoryName(settingsPath));
            Assert.NotEqual(settingsPath, sessions.GetSessionFilePath(@"C:\repo", "settings"));
        }

        [Fact]
        public void The_default_storage_root_is_outside_the_repository()
        {
            using var repo = new TempDirectory("repo");
            var store = new FileSettingsStore();

            var path = Path.GetFullPath(store.GetRepositoryFilePath(repo.Path));

            // A settings file inside the working tree would be rewritten by the very checkout
            // whose behaviour it configures, and would show as a pending change on every switch.
            Assert.DoesNotContain(Path.GetFullPath(repo.Path), path, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(
                Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),
                path,
                StringComparison.OrdinalIgnoreCase);
        }

        public sealed class Json
        {
            [Fact]
            public void Is_written_as_readable_field_names()
            {
                var json = FileSettingsStore.ToJson(Branch("branch/main", SyncSetting.SyncBreakpoints, on: true));

                Assert.Contains("\"scopes\"", json, StringComparison.Ordinal);
                Assert.Contains("\"kind\":\"Branch\"", json, StringComparison.Ordinal);
                Assert.Contains("\"setting\":\"syncBreakpoints\"", json, StringComparison.Ordinal);
            }

            [Fact]
            public void The_scope_kind_is_stored_by_name_not_by_number()
            {
                var json = FileSettingsStore.ToJson(Branch("branch/main", SyncSetting.SyncTabs, on: false));

                // Renumbering SettingScopeKind to insert a level must not silently reinterpret
                // files that already exist.
                Assert.Contains("\"kind\":\"Branch\"", json, StringComparison.Ordinal);
                Assert.DoesNotContain("\"kind\":2", json, StringComparison.Ordinal);
            }

            [Fact]
            public void An_absent_entry_is_how_inherit_is_stored()
            {
                var json = FileSettingsStore.ToJson(Branch("branch/main", SyncSetting.SyncTabs, on: false));

                // The three states are on, off, and no entry at all. A scope that inherits
                // everything writes no values, which is what makes "off here" distinguishable
                // from "not set here".
                Assert.Contains("\"setting\":\"syncTabs\"", json, StringComparison.Ordinal);
                Assert.DoesNotContain("syncBookmarks", json, StringComparison.Ordinal);
            }
        }
    }
}
