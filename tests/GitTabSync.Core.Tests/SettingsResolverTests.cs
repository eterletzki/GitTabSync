using System;
using System.IO;
using System.Linq;
using GitTabSync.Settings;
using GitTabSync.Tests.TestSupport;
using Xunit;

namespace GitTabSync.Tests
{
    public sealed class SettingsResolverTests
    {
        private const string Repo = @"C:\repo";
        private const string SolutionPath = @"C:\repo\GitTabSync.slnx";
        private const string ProjectPath = @"C:\repo\src\GitTabSync.Core\GitTabSync.Core.csproj";

        private const string RelativeSolution = "GitTabSync.slnx";
        private const string RelativeProject = "src/GitTabSync.Core/GitTabSync.Core.csproj";

        private readonly InMemorySettingsStore _store = new InMemorySettingsStore();

        private SettingsResolver Resolver() => new SettingsResolver(_store, Repo);

        private static SyncContext On(string headKey, string? solution = null, string? project = null) =>
            new SyncContext(Repo, headKey, solution, project);

        [Fact]
        public void The_built_in_default_applies_when_nothing_is_set()
        {
            var resolver = Resolver();

            Assert.True(resolver.IsEnabled(SyncSetting.SyncTabs, On("branch/main")));

            // Off because the feature does not exist yet, not because anyone turned it off.
            Assert.False(resolver.IsEnabled(SyncSetting.SyncBookmarks, On("branch/main")));
        }

        // ---- the cascade ----

        [Fact]
        public void The_scope_chain_runs_narrowest_first()
        {
            var chain = On("branch/main", SolutionPath, ProjectPath).ScopeChain;

            Assert.Equal(
                new[]
                {
                    SettingScopeKind.Project,
                    SettingScopeKind.Solution,
                    SettingScopeKind.Branch,
                    SettingScopeKind.Repository,
                    SettingScopeKind.Global,
                },
                chain.Select(s => s.Kind).ToArray());
        }

        [Fact]
        public void A_context_with_no_readable_head_drops_the_branch_levels()
        {
            // HEAD unreadable. Inventing a branch key would attribute settings to a branch the
            // extension cannot name.
            var chain = On(string.Empty, SolutionPath, ProjectPath).ScopeChain;

            Assert.Equal(
                new[] { SettingScopeKind.Repository, SettingScopeKind.Global },
                chain.Select(s => s.Kind).ToArray());
        }

        [Fact]
        public void A_repository_override_beats_the_default()
        {
            var resolver = Resolver();
            resolver.Set(SettingScope.Repository, SyncSetting.SyncTabs, false);

            Assert.False(resolver.IsEnabled(SyncSetting.SyncTabs, On("branch/main")));
        }

        [Fact]
        public void A_branch_override_beats_the_repository()
        {
            var resolver = Resolver();
            resolver.Set(SettingScope.Repository, SyncSetting.SyncBookmarks, false);
            resolver.Set(SettingScope.Branch("branch/main"), SyncSetting.SyncBookmarks, true);

            Assert.True(resolver.IsEnabled(SyncSetting.SyncBookmarks, On("branch/main")));
            Assert.False(resolver.IsEnabled(SyncSetting.SyncBookmarks, On("branch/other")));
        }

        [Fact]
        public void A_solution_override_beats_the_branch()
        {
            var resolver = Resolver();
            resolver.Set(SettingScope.Branch("branch/main"), SyncSetting.SyncTabs, false);
            resolver.Set(SettingScope.Solution("branch/main", RelativeSolution), SyncSetting.SyncTabs, true);

            Assert.True(resolver.IsEnabled(SyncSetting.SyncTabs, On("branch/main", SolutionPath)));
        }

        [Fact]
        public void A_project_override_beats_the_solution()
        {
            var resolver = Resolver();
            resolver.Set(SettingScope.Solution("branch/main", RelativeSolution), SyncSetting.SyncTabs, true);
            resolver.Set(SettingScope.Project("branch/main", RelativeProject), SyncSetting.SyncTabs, false);

            Assert.False(resolver.IsEnabled(SyncSetting.SyncTabs, On("branch/main", SolutionPath, ProjectPath)));
        }

        [Fact]
        public void A_project_override_is_scoped_to_the_branch_it_was_set_on()
        {
            var resolver = Resolver();
            resolver.Set(SettingScope.Project("branch/main", RelativeProject), SyncSetting.SyncTabs, false);

            // Settings reach over a branch; narrowing to a project narrows within that branch
            // rather than escaping it.
            Assert.False(resolver.IsEnabled(SyncSetting.SyncTabs, On("branch/main", project: ProjectPath)));
            Assert.True(resolver.IsEnabled(SyncSetting.SyncTabs, On("branch/other", project: ProjectPath)));
        }

        [Fact]
        public void Off_at_a_narrow_scope_survives_on_at_a_broad_one()
        {
            var resolver = Resolver();
            resolver.Set(SettingScope.Global, SyncSetting.SyncBookmarks, true);
            resolver.Set(SettingScope.Branch("branch/release/1.0.1"), SyncSetting.SyncBookmarks, false);

            // The whole point of the three-state model: "off here" is a value, not an absence,
            // so a broader "on" cannot swallow it.
            Assert.False(resolver.IsEnabled(SyncSetting.SyncBookmarks, On("branch/release/1.0.1")));
            Assert.True(resolver.IsEnabled(SyncSetting.SyncBookmarks, On("branch/main")));
        }

        [Fact]
        public void Clearing_an_override_falls_back_to_the_next_scope_up()
        {
            var resolver = Resolver();
            resolver.Set(SettingScope.Repository, SyncSetting.SyncTabs, false);
            resolver.Set(SettingScope.Branch("branch/main"), SyncSetting.SyncTabs, true);

            resolver.Set(SettingScope.Branch("branch/main"), SyncSetting.SyncTabs, null);

            // Clearing must restore inheritance, not write the value that happened to be showing.
            Assert.Null(resolver.GetOverride(SettingScope.Branch("branch/main"), SyncSetting.SyncTabs));
            Assert.False(resolver.IsEnabled(SyncSetting.SyncTabs, On("branch/main")));
        }

        [Fact]
        public void Branch_scopes_are_case_sensitive()
        {
            var resolver = Resolver();
            resolver.Set(SettingScope.Branch("branch/Feature"), SyncSetting.SyncTabs, false);

            // Git ref names are case-sensitive even on Windows, so these are two branches.
            Assert.False(resolver.IsEnabled(SyncSetting.SyncTabs, On("branch/Feature")));
            Assert.True(resolver.IsEnabled(SyncSetting.SyncTabs, On("branch/feature")));
        }

        [Fact]
        public void Project_scopes_are_case_insensitive()
        {
            var resolver = Resolver();
            resolver.Set(SettingScope.Project("branch/main", RelativeProject), SyncSetting.SyncTabs, false);

            // Windows paths are; the same project reported with different casing is one project.
            var shouted = @"C:\repo\SRC\GITTABSYNC.CORE\GITTABSYNC.CORE.CSPROJ";
            Assert.False(resolver.IsEnabled(SyncSetting.SyncTabs, On("branch/main", project: shouted)));
        }

        [Fact]
        public void A_project_outside_the_repository_is_scoped_by_its_absolute_path()
        {
            var resolver = Resolver();
            var outside = @"C:\elsewhere\Shared.csproj";
            resolver.Set(SettingScope.Project("branch/main", outside), SyncSetting.SyncTabs, false);

            Assert.False(resolver.IsEnabled(SyncSetting.SyncTabs, On("branch/main", project: outside)));
        }

        // ---- origin ----

        [Fact]
        public void Reports_which_scope_decided()
        {
            var resolver = Resolver();
            resolver.Set(SettingScope.Repository, SyncSetting.SyncTabs, false);

            var resolved = resolver.Resolve(SyncSetting.SyncTabs, On("branch/main"));

            // "Why is it doing this here" is the question the settings UI has to answer, and a
            // bare true/false cannot.
            Assert.Equal(SettingScopeKind.Repository, resolved.Origin.Kind);
            Assert.True(resolved.IsInherited(SettingScope.Branch("branch/main")));
            Assert.False(resolved.IsInherited(SettingScope.Repository));
        }

        [Fact]
        public void The_built_in_default_is_reported_as_not_explicit()
        {
            var resolved = Resolver().Resolve(SyncSetting.SyncBookmarks, On("branch/main"));

            Assert.False(resolved.Value);
            Assert.Equal(SettingScopeKind.Global, resolved.Origin.Kind);
            Assert.False(resolved.IsExplicit);
        }

        [Fact]
        public void A_value_set_to_the_same_as_the_default_is_still_explicit()
        {
            var resolver = Resolver();
            resolver.Set(SettingScope.Global, SyncSetting.SyncBookmarks, false);

            // "Off" and "off, because you said so" look identical in the value alone. They are not
            // the same thing: the second one survives a change to the built-in default.
            var resolved = resolver.Resolve(SyncSetting.SyncBookmarks, On("branch/main"));

            Assert.False(resolved.Value);
            Assert.True(resolved.IsExplicit);
        }

        // ---- listing what has been set ----

        [Fact]
        public void Lists_every_stored_override_narrowest_last()
        {
            var resolver = Resolver();
            resolver.Set(SettingScope.Branch("branch/main"), SyncSetting.SyncTabs, false);
            resolver.Set(SettingScope.Global, SyncSetting.SyncBookmarks, true);
            resolver.Set(SettingScope.Repository, SyncSetting.RestoreOnStartup, false);

            // An override nobody can see is an override nobody can undo.
            Assert.Equal(
                new[] { SettingScopeKind.Global, SettingScopeKind.Repository, SettingScopeKind.Branch },
                resolver.Overrides.Select(o => o.Scope.Kind).ToArray());
        }

        [Fact]
        public void Clearing_the_last_setting_of_a_scope_removes_the_scope()
        {
            var resolver = Resolver();
            resolver.Set(SettingScope.Branch("branch/main"), SyncSetting.SyncTabs, false);
            resolver.Set(SettingScope.Branch("branch/main"), SyncSetting.SyncTabs, null);

            // An empty scope would linger in the file and show in the UI as an override that says
            // nothing.
            Assert.Empty(resolver.Overrides);
            Assert.Empty(_store.Load(Repo).Scopes);
        }

        // ---- persistence ----

        [Fact]
        public void Defaults_reach_a_second_repository_but_branch_overrides_do_not()
        {
            var resolver = Resolver();
            resolver.Set(SettingScope.Global, SyncSetting.SyncBookmarks, true);
            resolver.Set(SettingScope.Branch("branch/main"), SyncSetting.SyncTabs, false);

            var elsewhere = new SettingsResolver(_store, @"C:\other-repo");

            Assert.True(elsewhere.IsEnabled(SyncSetting.SyncBookmarks, new SyncContext(@"C:\other-repo", "branch/main")));
            Assert.True(elsewhere.IsEnabled(SyncSetting.SyncTabs, new SyncContext(@"C:\other-repo", "branch/main")));
        }

        [Fact]
        public void Survives_a_reload_through_a_real_settings_file()
        {
            using var temp = new TempDirectory();
            var store = new FileSettingsStore(temp.Path);

            var resolver = new SettingsResolver(store, Repo);
            resolver.Set(SettingScope.Global, SyncSetting.RestoreOnStartup, false);
            resolver.Set(SettingScope.Branch("branch/release/1.0.1"), SyncSetting.SyncBookmarks, true);
            resolver.Set(SettingScope.Project("branch/release/1.0.1", RelativeProject), SyncSetting.SyncTabs, false);

            // A fresh resolver over the same files is what the next Visual Studio session gets.
            var reopened = new SettingsResolver(store, Repo);

            Assert.False(reopened.IsEnabled(SyncSetting.RestoreOnStartup, On("branch/main")));
            Assert.True(reopened.IsEnabled(SyncSetting.SyncBookmarks, On("branch/release/1.0.1")));
            Assert.False(reopened.IsEnabled(
                SyncSetting.SyncTabs,
                On("branch/release/1.0.1", project: ProjectPath)));
            Assert.True(reopened.IsEnabled(SyncSetting.SyncTabs, On("branch/release/1.0.1")));
        }

        [Fact]
        public void Reload_picks_up_a_file_changed_underneath()
        {
            var resolver = Resolver();
            _store.Seed(Repo, new ScopedSettings
            {
                Scopes =
                {
                    new ScopeOverrides
                    {
                        Kind = nameof(SettingScopeKind.Repository),
                        Values = { new SettingOverride { Setting = "syncTabs", On = false } },
                    },
                },
            });

            Assert.True(resolver.IsEnabled(SyncSetting.SyncTabs, On("branch/main")));
            resolver.Reload();
            Assert.False(resolver.IsEnabled(SyncSetting.SyncTabs, On("branch/main")));
        }

        [Fact]
        public void A_failing_save_still_applies_to_the_running_session()
        {
            var resolver = Resolver();
            _store.FailOnSave = new IOException("the settings file is locked");

            resolver.Set(SettingScope.Branch("branch/main"), SyncSetting.SyncTabs, false);

            // The user asked for this and is watching for it to happen. Losing the persistence is
            // a smaller failure than ignoring the request.
            Assert.False(resolver.IsEnabled(SyncSetting.SyncTabs, On("branch/main")));
        }

        // ---- files written by something else ----

        [Fact]
        public void An_unrecognised_scope_kind_is_skipped()
        {
            _store.Seed(Repo, new ScopedSettings
            {
                Scopes =
                {
                    new ScopeOverrides
                    {
                        Kind = "Workspace",
                        HeadKey = "branch/main",
                        Values = { new SettingOverride { Setting = "syncTabs", On = false } },
                    },
                },
            });

            // A newer build wrote a level this one does not have. Guessing at it would apply a
            // setting somewhere the user never asked for.
            Assert.True(Resolver().IsEnabled(SyncSetting.SyncTabs, On("branch/main")));
        }

        [Fact]
        public void An_unrecognised_setting_name_is_skipped_without_losing_the_rest()
        {
            _store.Seed(Repo, new ScopedSettings
            {
                Scopes =
                {
                    new ScopeOverrides
                    {
                        Kind = nameof(SettingScopeKind.Repository),
                        Values =
                        {
                            new SettingOverride { Setting = "syncMinimapDoodles", On = true },
                            new SettingOverride { Setting = "syncTabs", On = false },
                        },
                    },
                },
            });

            Assert.False(Resolver().IsEnabled(SyncSetting.SyncTabs, On("branch/main")));
        }

        [Fact]
        public void A_repository_scope_hand_written_into_the_defaults_is_ignored()
        {
            _store.SeedDefaults(new ScopedSettings
            {
                Scopes =
                {
                    new ScopeOverrides
                    {
                        Kind = nameof(SettingScopeKind.Branch),
                        HeadKey = "branch/main",
                        Values = { new SettingOverride { Setting = "syncTabs", On = false } },
                    },
                },
            });

            // Otherwise one repository's branch override would silently apply to every repository
            // that happens to have a branch of the same name.
            Assert.True(Resolver().IsEnabled(SyncSetting.SyncTabs, On("branch/main")));
        }

        [Fact]
        public void A_global_scope_hand_written_into_a_repository_document_is_ignored()
        {
            _store.Seed(Repo, new ScopedSettings
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

            Assert.True(Resolver().IsEnabled(SyncSetting.SyncTabs, On("branch/main")));
        }

        [Fact]
        public void A_branch_scope_stored_without_a_head_is_skipped()
        {
            _store.Seed(Repo, new ScopedSettings
            {
                Scopes =
                {
                    new ScopeOverrides
                    {
                        Kind = nameof(SettingScopeKind.Branch),
                        HeadKey = string.Empty,
                        Values = { new SettingOverride { Setting = "syncTabs", On = false } },
                    },
                },
            });

            // A branch scope with no branch would collide with every other one, because the head
            // key is the whole of its identity.
            Assert.True(Resolver().IsEnabled(SyncSetting.SyncTabs, On("branch/main")));
        }
    }
}
