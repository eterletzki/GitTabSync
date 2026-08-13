using System.Collections.Generic;
using System.Linq;
using GitTabSync.Settings;
using GitTabSync.Tests.TestSupport;
using Xunit;

namespace GitTabSync.Tests
{
    public sealed class SettingsViewModelTests
    {
        private const string Repo = @"C:\repo";
        private const string SolutionPath = @"C:\repo\GitTabSync.slnx";
        private const string ProjectPath = @"C:\repo\src\GitTabSync.Core\GitTabSync.Core.csproj";

        private readonly InMemorySettingsStore _store = new InMemorySettingsStore();

        private SettingsResolver _resolver = null!;

        private SettingsViewModel Model(
            string headKey = "branch/release/1.0.1",
            string? solution = null,
            string? project = null)
        {
            _resolver = new SettingsResolver(_store, Repo);
            return new SettingsViewModel(
                _resolver,
                new SyncContext(Repo, headKey, solution, project),
                "GitTabSync");
        }

        private static SyncSettingRow Row(SettingsViewModel model, SyncSetting setting) =>
            model.Rows.Single(r => r.Setting == setting);

        private static List<string> Changes(SettingsViewModel model)
        {
            var seen = new List<string>();
            model.PropertyChanged += (_, e) => seen.Add(e.PropertyName!);
            return seen;
        }

        // ---- the scope picker ----

        [Fact]
        public void Offers_only_the_scopes_that_apply_where_the_user_is()
        {
            var model = Model();

            Assert.Equal(
                new[] { SettingScopeKind.Global, SettingScopeKind.Repository, SettingScopeKind.Branch },
                model.Scopes.Select(s => s.Kind).ToArray());
        }

        [Fact]
        public void Offers_the_solution_and_project_when_the_host_supplies_them()
        {
            var model = Model(solution: SolutionPath, project: ProjectPath);

            Assert.Equal(
                new[]
                {
                    SettingScopeKind.Global,
                    SettingScopeKind.Repository,
                    SettingScopeKind.Branch,
                    SettingScopeKind.Solution,
                    SettingScopeKind.Project,
                },
                model.Scopes.Select(s => s.Kind).ToArray());
        }

        [Fact]
        public void Starts_on_the_branch_because_that_is_what_settings_reach_over()
        {
            var model = Model(solution: SolutionPath, project: ProjectPath);

            Assert.Equal(SettingScopeKind.Branch, model.SelectedScope.Kind);
            Assert.Equal("release/1.0.1", model.SelectedScope.Label);
        }

        [Fact]
        public void Falls_back_to_the_repository_when_head_cannot_be_read()
        {
            var model = Model(headKey: string.Empty);

            Assert.Equal(SettingScopeKind.Repository, model.SelectedScope.Kind);
            Assert.Equal("(unknown)", model.HeadText);
        }

        [Fact]
        public void A_detached_head_is_labelled_by_a_short_commit_id()
        {
            var model = Model(headKey: "detached/" + new string('a', 40));

            Assert.Equal("aaaaaaaa (detached)", model.HeadText);
        }

        [Fact]
        public void Solution_and_project_scopes_are_labelled_by_file_name_and_branch()
        {
            var model = Model(solution: SolutionPath, project: ProjectPath);

            Assert.Equal(
                "GitTabSync.Core.csproj on release/1.0.1",
                model.Scopes.Single(s => s.Kind == SettingScopeKind.Project).Label);
        }

        // ---- editing ----

        [Fact]
        public void Setting_a_row_writes_through_to_the_resolver_at_the_selected_scope()
        {
            var model = Model();

            Row(model, SyncSetting.SyncTabs).Value = false;

            Assert.False(_resolver.GetOverride(SettingScope.Branch("branch/release/1.0.1"), SyncSetting.SyncTabs));
            Assert.True(_resolver.IsEnabled(SyncSetting.SyncTabs, new SyncContext(Repo, "branch/main")));
        }

        [Fact]
        public void Clearing_a_row_returns_it_to_inheriting()
        {
            var model = Model();
            var row = Row(model, SyncSetting.SyncTabs);
            row.Value = false;

            row.Clear();

            Assert.Null(row.Value);
            Assert.False(row.IsSetHere);
            Assert.True(row.EffectiveValue);
        }

        [Fact]
        public void Changing_the_selected_scope_edits_that_scope_instead()
        {
            var model = Model();
            model.SelectedScope = model.Scopes.Single(s => s.Kind == SettingScopeKind.Repository);

            Row(model, SyncSetting.SyncTabs).Value = false;

            Assert.False(_resolver.GetOverride(SettingScope.Repository, SyncSetting.SyncTabs));
            Assert.Null(_resolver.GetOverride(SettingScope.Branch("branch/release/1.0.1"), SyncSetting.SyncTabs));
        }

        [Fact]
        public void Rows_are_rebuilt_when_the_scope_changes()
        {
            var model = Model();
            Row(model, SyncSetting.SyncTabs).Value = false;

            model.SelectedScope = model.Scopes.Single(s => s.Kind == SettingScopeKind.Repository);

            // The repository scope inherits; the branch override must not show as set here, or
            // the next click would clear a scope the user is not looking at.
            Assert.Null(Row(model, SyncSetting.SyncTabs).Value);
        }

        // ---- what the row says about itself ----

        [Fact]
        public void Reports_a_value_nobody_set_as_the_default()
        {
            Assert.Equal("On, default", Row(Model(), SyncSetting.SyncTabs).EffectiveText);
        }

        [Fact]
        public void Reports_a_value_set_at_the_selected_scope_as_set_here()
        {
            var model = Model();
            var row = Row(model, SyncSetting.SyncTabs);

            row.Value = false;

            Assert.Equal("Off, set here", row.EffectiveText);
        }

        [Fact]
        public void Reports_a_value_from_a_broader_scope_as_inherited()
        {
            var model = Model();
            _resolver.Set(SettingScope.Repository, SyncSetting.SyncTabs, false);
            model.Refresh();

            Assert.Equal("Off, inherited from This repository", Row(model, SyncSetting.SyncTabs).EffectiveText);
        }

        [Fact]
        public void Reports_a_value_from_a_narrower_scope_as_overridden()
        {
            var model = Model(solution: SolutionPath, project: ProjectPath);
            _resolver.Set(SettingScope.Project("branch/release/1.0.1", "src/GitTabSync.Core/GitTabSync.Core.csproj"),
                SyncSetting.SyncTabs, false);
            model.Refresh();

            model.SelectedScope = model.Scopes.Single(s => s.Kind == SettingScopeKind.Branch);

            // Saying "inherited from" here would suggest the branch is what decides, while it is
            // being overruled — which is exactly when a user concludes the toggle is broken.
            Assert.Equal(
                "Off, overridden on GitTabSync.Core.csproj on release/1.0.1",
                Row(model, SyncSetting.SyncTabs).EffectiveText);
        }

        [Fact]
        public void Marks_the_settings_whose_feature_does_not_exist_yet()
        {
            var model = Model();

            Assert.False(Row(model, SyncSetting.SyncBookmarks).IsImplemented);
            Assert.False(Row(model, SyncSetting.SyncBreakpoints).IsImplemented);
            Assert.NotNull(Row(model, SyncSetting.SyncBookmarks).UnavailableReason);

            // A toggle wired to nothing must not look like a working one.
            Assert.True(Row(model, SyncSetting.SyncTabs).IsImplemented);
            Assert.Null(Row(model, SyncSetting.SyncTabs).UnavailableReason);
        }

        // ---- the overrides list ----

        [Fact]
        public void Lists_nothing_until_something_is_set()
        {
            var model = Model();

            Assert.False(model.HasOverrides);
            Assert.Empty(model.Overrides);
        }

        [Fact]
        public void Lists_an_override_as_soon_as_a_row_is_set()
        {
            var model = Model();

            Row(model, SyncSetting.SyncTabs).Value = false;

            var listed = Assert.Single(model.Overrides);
            Assert.Equal("release/1.0.1", listed.ScopeText);
            Assert.Equal("Tabs", listed.SettingText);
            Assert.Equal("off", listed.ValueText);
        }

        [Fact]
        public void Clears_an_override_belonging_to_a_branch_the_user_is_not_on()
        {
            var model = Model();
            _resolver.Set(SettingScope.Branch("branch/some-other-branch"), SyncSetting.SyncTabs, false);
            model.Refresh();

            var stranded = model.Overrides.Single(o => o.ScopeText == "some-other-branch");
            model.ClearOverride(stranded);

            // An override on a branch you have forgotten is the one most likely to read as a bug,
            // so removing it must not require checking that branch out.
            Assert.Empty(model.Overrides);
            Assert.Null(_resolver.GetOverride(SettingScope.Branch("branch/some-other-branch"), SyncSetting.SyncTabs));
        }

        // ---- following the repository ----

        [Fact]
        public void Follows_a_branch_switch_onto_the_new_branch()
        {
            var model = Model();
            _resolver.Set(SettingScope.Branch("branch/main"), SyncSetting.SyncTabs, false);

            model.UpdateContext(new SyncContext(Repo, "branch/main"));

            Assert.Equal("main", model.HeadText);
            Assert.Equal(SettingScopeKind.Branch, model.SelectedScope.Kind);
            Assert.False(Row(model, SyncSetting.SyncTabs).Value);
        }

        [Fact]
        public void Keeps_the_level_the_user_had_selected_across_a_switch()
        {
            var model = Model();
            model.SelectedScope = model.Scopes.Single(s => s.Kind == SettingScopeKind.Global);

            model.UpdateContext(new SyncContext(Repo, "branch/main"));

            // Having chosen to look at the defaults, being dropped back to the branch on every
            // switch would be its own small annoyance.
            Assert.Equal(SettingScopeKind.Global, model.SelectedScope.Kind);
        }

        [Fact]
        public void Drops_to_a_level_that_still_exists_when_the_old_one_does_not()
        {
            var model = Model(solution: SolutionPath, project: ProjectPath);
            model.SelectedScope = model.Scopes.Single(s => s.Kind == SettingScopeKind.Project);

            // The active document changed to one belonging to no project.
            model.UpdateContext(new SyncContext(Repo, "branch/release/1.0.1", SolutionPath));

            Assert.Equal(SettingScopeKind.Branch, model.SelectedScope.Kind);
        }

        [Fact]
        public void Tells_the_view_that_the_head_and_scopes_changed()
        {
            var model = Model();
            var seen = Changes(model);

            model.UpdateContext(new SyncContext(Repo, "branch/main"));

            Assert.Contains(nameof(SettingsViewModel.HeadText), seen);
            Assert.Contains(nameof(SettingsViewModel.Scopes), seen);
            Assert.Contains(nameof(SettingsViewModel.Rows), seen);
        }
    }
}
