using System;
using System.IO;
using System.Linq;
using GitTabSync.Git;
using GitTabSync.Settings;
using GitTabSync.Storage;
using GitTabSync.Sync;
using GitTabSync.Tests.TestSupport;
using Xunit;

namespace GitTabSync.Tests
{
    public sealed class TabSyncCoordinatorTests : IDisposable
    {
        private readonly TempDirectory _repo = new TempDirectory("repo");
        private readonly FakeEditorTabs _editor = new FakeEditorTabs();
        private readonly InMemorySessionStore _store = new InMemorySessionStore();
        private readonly InMemorySettingsStore _settingsStore = new InMemorySettingsStore();
        private readonly RecordingTabSyncLog _log = new RecordingTabSyncLog();
        private readonly string _headPath;
        private readonly GitRepository _repository;

        private BranchMonitor? _monitor;
        private TabSyncCoordinator? _coordinator;

        public TabSyncCoordinatorTests()
        {
            _headPath = _repo.CreateFile(".git/HEAD", "ref: refs/heads/main\n");
            _repository = GitRepository.Discover(_repo.Path)!;
        }

        private TabSyncCoordinator Start(ISyncSettings? settings = null, string? solutionFilePath = null)
        {
            _monitor = new BranchMonitor(_repository, pollInterval: TimeSpan.Zero);
            _coordinator = new TabSyncCoordinator(
                _repository,
                _monitor,
                _store,
                _editor,
                settings ?? new TabSyncOptions { RestoreOnStartup = false },
                _log,
                solutionFilePath: solutionFilePath);

            _coordinator.Start();
            return _coordinator;
        }

        /// <summary>
        /// A scoped resolver configured like the default <see cref="TabSyncOptions"/> above, so a
        /// test that swaps one for the other is only changing the thing it is about.
        /// </summary>
        private SettingsResolver Scoped()
        {
            var resolver = new SettingsResolver(_settingsStore, _repo.Path);
            resolver.Set(SettingScope.Repository, SyncSetting.RestoreOnStartup, false);
            return resolver;
        }

        private void SwitchTo(string branch)
        {
            File.WriteAllText(_headPath, "ref: refs/heads/" + branch + "\n");
            _monitor!.CheckNow();
        }

        /// <summary>Creates a real file, since restore filters on existence.</summary>
        private string File_(string relativePath) => _repo.CreateFile(relativePath, "// code");

        [Fact]
        public void Saves_the_outgoing_branch_tabs_when_head_moves()
        {
            var a = File_("src/A.cs");
            _editor.SetOpen(a);
            _editor.ActiveIndex = 0;

            var coordinator = Start();
            coordinator.CaptureSnapshot();

            SwitchTo("feature");

            var saved = _store.Get(_repo.Path, "branch/main");
            Assert.NotNull(saved);
            Assert.Equal("src/A.cs", saved!.Tabs.Single().Path);
            Assert.Equal(0, saved.ActiveIndex);
        }

        [Fact]
        public void Restores_the_incoming_branch_tabs()
        {
            var a = File_("src/A.cs");
            var b = File_("src/B.cs");

            _editor.SetOpen(a);
            var coordinator = Start();
            coordinator.CaptureSnapshot();

            // "feature" was visited before and had B.cs open.
            _store.Save(_repo.Path, new Model.TabSession
            {
                HeadKey = "branch/feature",
                ActiveIndex = 0,
                Tabs = { new Model.TabEntry { Path = "src/B.cs", IsRepositoryRelative = true } },
            });

            SwitchTo("feature");

            Assert.Equal(new[] { b }, _editor.Open.Select(t => t.AbsolutePath).ToArray());
            Assert.Equal(0, _editor.ActiveIndex);
        }

        [Fact]
        public void Saves_the_tabs_that_were_open_before_the_switch_not_the_ones_left_after_it()
        {
            var a = File_("src/A.cs");
            var b = File_("src/B.cs");

            _editor.SetOpen(a, b);
            var coordinator = Start();
            coordinator.CaptureSnapshot();

            // Visual Studio reacts to the checkout on its own and closes B.cs because the file
            // vanished. That happens before the monitor reports the switch, so reading the
            // editor at switch time would record the wrong set for the outgoing branch. The
            // continuously tracked snapshot is what protects against this.
            _editor.SetOpen(a);

            SwitchTo("feature");

            var saved = _store.Get(_repo.Path, "branch/main");
            Assert.Equal(new[] { "src/A.cs", "src/B.cs" }, saved!.Tabs.Select(t => t.Path).ToArray());
        }

        [Fact]
        public void Document_events_raised_during_a_restore_do_not_corrupt_the_snapshot()
        {
            var a = File_("src/A.cs");
            var b = File_("src/B.cs");

            _editor.SetOpen(a);
            var coordinator = Start();
            coordinator.CaptureSnapshot();

            _store.Save(_repo.Path, new Model.TabSession
            {
                HeadKey = "branch/feature",
                Tabs = { new Model.TabEntry { Path = "src/B.cs", IsRepositoryRelative = true } },
            });

            // Opening documents raises host events, which call back into CaptureSnapshot while
            // the restore is still in flight.
            _editor.DuringApply = () => coordinator.CaptureSnapshot();

            SwitchTo("feature");
            _editor.DuringApply = null;

            // Switching back must save what the restore produced, not a half-applied state.
            SwitchTo("main");

            var saved = _store.Get(_repo.Path, "branch/feature");
            Assert.Equal(new[] { "src/B.cs" }, saved!.Tabs.Select(t => t.Path).ToArray());
        }

        [Fact]
        public void Leaves_tabs_alone_when_the_incoming_branch_has_no_stored_session()
        {
            var a = File_("src/A.cs");
            _editor.SetOpen(a);

            var coordinator = Start();
            coordinator.CaptureSnapshot();

            SwitchTo("brand-new");

            // Closing everything would wipe the user's tabs the first time they ever switch
            // after installing the extension.
            Assert.Equal(new[] { a }, _editor.Open.Select(t => t.AbsolutePath).ToArray());
            Assert.Equal(0, _editor.ApplyCallCount);
        }

        [Fact]
        public void Closes_tabs_on_an_unknown_branch_when_configured_to()
        {
            var a = File_("src/A.cs");
            _editor.SetOpen(a);

            var coordinator = Start(new TabSyncOptions
            {
                RestoreOnStartup = false,
                CloseTabsWhenBranchHasNoSession = true,
            });
            coordinator.CaptureSnapshot();

            SwitchTo("brand-new");

            Assert.Empty(_editor.Open);
        }

        [Fact]
        public void Stored_tabs_that_do_not_exist_on_the_incoming_branch_are_skipped()
        {
            var a = File_("src/A.cs");
            _editor.SetOpen(a);
            var coordinator = Start();
            coordinator.CaptureSnapshot();

            _store.Save(_repo.Path, new Model.TabSession
            {
                HeadKey = "branch/feature",
                Tabs =
                {
                    new Model.TabEntry { Path = "src/A.cs", IsRepositoryRelative = true },
                    new Model.TabEntry { Path = "src/OnlyOnAnotherBranch.cs", IsRepositoryRelative = true },
                },
            });

            SwitchTo("feature");

            Assert.Equal(new[] { a }, _editor.Open.Select(t => t.AbsolutePath).ToArray());
        }

        [Fact]
        public void Switching_away_and_back_restores_the_original_tabs()
        {
            var a = File_("src/A.cs");
            var b = File_("src/B.cs");

            _editor.SetOpen(a, b);
            _editor.ActiveIndex = 1;
            var coordinator = Start();
            coordinator.CaptureSnapshot();

            SwitchTo("feature");
            _editor.SetOpen(b);
            coordinator.CaptureSnapshot();

            SwitchTo("main");

            Assert.Equal(new[] { a, b }, _editor.Open.Select(t => t.AbsolutePath).ToArray());
            Assert.Equal(1, _editor.ActiveIndex);
        }

        [Fact]
        public void A_pinned_tab_comes_back_pinned()
        {
            var a = File_("src/A.cs");
            var b = File_("src/B.cs");

            _editor.SetOpen(new EditorTab(a, isPinned: true), new EditorTab(b));
            var coordinator = Start();
            coordinator.CaptureSnapshot();

            SwitchTo("feature");
            _editor.SetOpen(b);
            coordinator.CaptureSnapshot();

            SwitchTo("main");

            Assert.True(_editor.Open.Single(t => t.AbsolutePath == a).IsPinned);
            Assert.False(_editor.Open.Single(t => t.AbsolutePath == b).IsPinned);
        }

        [Fact]
        public void A_tab_pinned_after_the_last_capture_is_still_saved_as_pinned()
        {
            var a = File_("src/A.cs");
            _editor.SetOpen(a);

            var coordinator = Start();
            coordinator.CaptureSnapshot();

            // Pinning raises nothing the host is obliged to report, so the snapshot can be this
            // stale at the moment a branch switch arrives. The pinned state is therefore read
            // from the editor as the session is written, not taken from the snapshot.
            _editor.SetOpen(new EditorTab(a, isPinned: true));

            SwitchTo("feature");

            Assert.True(_store.Get(_repo.Path, "branch/main")!.Tabs.Single().IsPinned);
        }

        [Fact]
        public void Unpinning_after_the_last_capture_is_saved_too()
        {
            var a = File_("src/A.cs");
            _editor.SetOpen(new EditorTab(a, isPinned: true));

            var coordinator = Start();
            coordinator.CaptureSnapshot();

            _editor.SetOpen(new EditorTab(a));

            SwitchTo("feature");

            Assert.False(_store.Get(_repo.Path, "branch/main")!.Tabs.Single().IsPinned);
        }

        [Fact]
        public void Re_reading_the_pinned_state_does_not_change_which_tabs_are_saved()
        {
            var a = File_("src/A.cs");
            var b = File_("src/B.cs");
            var c = File_("src/C.cs");

            _editor.SetOpen(a, b);
            var coordinator = Start();
            coordinator.CaptureSnapshot();

            // The checkout has already closed B.cs and opened C.cs by the time the switch is
            // observed. Re-reading pinned state must not let any of that into the session — the
            // set and the order still come from the snapshot alone.
            _editor.SetOpen(new EditorTab(a, isPinned: true), new EditorTab(c));

            SwitchTo("feature");

            var saved = _store.Get(_repo.Path, "branch/main");
            Assert.Equal(new[] { "src/A.cs", "src/B.cs" }, saved!.Tabs.Select(t => t.Path).ToArray());
            Assert.True(saved.Tabs[0].IsPinned);
        }

        [Fact]
        public void A_tab_the_editor_has_already_closed_keeps_its_last_known_pinned_state()
        {
            var a = File_("src/A.cs");
            _editor.SetOpen(new EditorTab(a, isPinned: true));

            var coordinator = Start();
            coordinator.CaptureSnapshot();

            // Visual Studio closed the tab when the checkout deleted the file. The editor cannot
            // report a pinned state for a window that no longer exists, and "gone" must not be
            // read as "not pinned".
            _editor.SetOpen(Array.Empty<string>());

            SwitchTo("feature");

            Assert.True(_store.Get(_repo.Path, "branch/main")!.Tabs.Single().IsPinned);
        }

        [Fact]
        public void Nothing_is_saved_when_the_previous_head_was_never_known()
        {
            File.WriteAllText(_headPath, string.Empty);
            _editor.SetOpen(File_("src/A.cs"));

            var coordinator = Start();
            coordinator.CaptureSnapshot();

            // HEAD only becomes readable now, so there is no branch the open tabs can be
            // attributed to and nothing may be written.
            SwitchTo("main");

            Assert.Equal(0, _store.SaveCount);
        }

        [Fact]
        public void SaveCurrentSession_persists_against_the_current_head()
        {
            var a = File_("src/A.cs");
            _editor.SetOpen(a);

            var coordinator = Start();
            coordinator.CaptureSnapshot();

            // Solution close and shutdown produce no branch change to trigger a save.
            coordinator.SaveCurrentSession();

            Assert.Equal("src/A.cs", _store.Get(_repo.Path, "branch/main")!.Tabs.Single().Path);
        }

        [Fact]
        public void Restores_on_startup_when_enabled()
        {
            var b = File_("src/B.cs");
            _store.Save(_repo.Path, new Model.TabSession
            {
                HeadKey = "branch/main",
                Tabs = { new Model.TabEntry { Path = "src/B.cs", IsRepositoryRelative = true } },
            });

            Start(new TabSyncOptions { RestoreOnStartup = true });

            Assert.Equal(new[] { b }, _editor.Open.Select(t => t.AbsolutePath).ToArray());
        }

        [Fact]
        public void A_detached_head_gets_its_own_session()
        {
            var a = File_("src/A.cs");
            _editor.SetOpen(a);
            var coordinator = Start();
            coordinator.CaptureSnapshot();

            File.WriteAllText(_headPath, new string('a', 40) + "\n");
            _monitor!.CheckNow();
            _editor.SetOpen(File_("src/B.cs"));
            coordinator.CaptureSnapshot();
            coordinator.SaveCurrentSession();

            Assert.NotNull(_store.Get(_repo.Path, "detached/" + new string('a', 40)));
            Assert.NotNull(_store.Get(_repo.Path, "branch/main"));
        }

        [Fact]
        public void Dispose_stops_reacting_to_branch_changes()
        {
            var a = File_("src/A.cs");
            _editor.SetOpen(a);
            var coordinator = Start();
            coordinator.CaptureSnapshot();
            coordinator.Dispose();

            SwitchTo("feature");

            Assert.Equal(0, _store.SaveCount);
        }

        // ---- scoped settings ----
        //
        // The coordinator resolves settings per decision, against the head that decision is about.
        // These drive that through the real resolver rather than a flat TabSyncOptions, because
        // the bug they exist to catch — resolving once and keeping the answer — is invisible to
        // any setting that cannot differ between two branches.

        [Fact]
        public void A_branch_with_tab_syncing_off_is_left_alone_on_arrival()
        {
            var a = File_("src/A.cs");
            File_("src/B.cs");
            _editor.SetOpen(a);

            var settings = Scoped();
            settings.Set(SettingScope.Branch("branch/feature"), SyncSetting.SyncTabs, false);

            var coordinator = Start(settings);
            coordinator.CaptureSnapshot();

            _store.Save(_repo.Path, new Model.TabSession
            {
                HeadKey = "branch/feature",
                Tabs = { new Model.TabEntry { Path = "src/B.cs", IsRepositoryRelative = true } },
            });

            SwitchTo("feature");

            Assert.Equal(new[] { a }, _editor.Open.Select(t => t.AbsolutePath).ToArray());
            Assert.Equal(0, _editor.ApplyCallCount);

            // Only the incoming branch opted out; the outgoing one is still saved as normal.
            Assert.NotNull(_store.Get(_repo.Path, "branch/main"));
        }

        [Fact]
        public void A_branch_with_tab_syncing_off_keeps_the_session_it_already_had()
        {
            var a = File_("src/A.cs");
            _editor.SetOpen(a);

            var settings = Scoped();
            settings.Set(SettingScope.Branch("branch/main"), SyncSetting.SyncTabs, false);

            _store.Save(_repo.Path, new Model.TabSession
            {
                HeadKey = "branch/main",
                Tabs = { new Model.TabEntry { Path = "src/FromBefore.cs", IsRepositoryRelative = true } },
            });

            var coordinator = Start(settings);
            coordinator.CaptureSnapshot();

            SwitchTo("feature");

            // Turning syncing off means "stop touching this", not "forget what you knew" — the
            // stored session has to survive so turning it back on is not a fresh start.
            Assert.Equal("src/FromBefore.cs", _store.Get(_repo.Path, "branch/main")!.Tabs.Single().Path);
        }

        [Fact]
        public void Turning_tab_syncing_off_for_one_branch_leaves_the_others_syncing()
        {
            var a = File_("src/A.cs");
            var b = File_("src/B.cs");
            _editor.SetOpen(a);

            var settings = Scoped();
            settings.Set(SettingScope.Branch("branch/quiet"), SyncSetting.SyncTabs, false);

            var coordinator = Start(settings);
            coordinator.CaptureSnapshot();

            _store.Save(_repo.Path, new Model.TabSession
            {
                HeadKey = "branch/feature",
                Tabs = { new Model.TabEntry { Path = "src/B.cs", IsRepositoryRelative = true } },
            });

            SwitchTo("feature");

            Assert.Equal(new[] { b }, _editor.Open.Select(t => t.AbsolutePath).ToArray());
        }

        [Fact]
        public void Settings_are_resolved_at_each_switch_not_captured_when_the_session_starts()
        {
            var a = File_("src/A.cs");
            var b = File_("src/B.cs");
            _editor.SetOpen(a);

            var settings = Scoped();
            settings.Set(SettingScope.Branch("branch/feature"), SyncSetting.SyncTabs, false);

            var coordinator = Start(settings);
            coordinator.CaptureSnapshot();

            _store.Save(_repo.Path, new Model.TabSession
            {
                HeadKey = "branch/feature",
                Tabs = { new Model.TabEntry { Path = "src/B.cs", IsRepositoryRelative = true } },
            });

            SwitchTo("feature");
            Assert.Equal(new[] { a }, _editor.Open.Select(t => t.AbsolutePath).ToArray());

            SwitchTo("main");

            // The user changes their mind without restarting Visual Studio, from another branch.
            // A coordinator holding the answer it resolved at startup would go on ignoring this
            // branch forever — and worse, would apply the outgoing branch's settings to the
            // incoming one on every switch, which is the one code path where the two heads always
            // differ.
            settings.Set(SettingScope.Branch("branch/feature"), SyncSetting.SyncTabs, null);

            SwitchTo("feature");

            Assert.Equal(new[] { b }, _editor.Open.Select(t => t.AbsolutePath).ToArray());
        }

        [Fact]
        public void Closing_tabs_on_an_unvisited_branch_can_be_turned_on_for_one_branch_only()
        {
            var a = File_("src/A.cs");
            _editor.SetOpen(a);

            var settings = Scoped();
            settings.Set(
                SettingScope.Branch("branch/scratch"),
                SyncSetting.CloseTabsWhenBranchHasNoSession,
                true);

            var coordinator = Start(settings);
            coordinator.CaptureSnapshot();

            SwitchTo("other");
            Assert.Equal(new[] { a }, _editor.Open.Select(t => t.AbsolutePath).ToArray());

            SwitchTo("scratch");
            Assert.Empty(_editor.Open);
        }

        [Fact]
        public void A_solution_scoped_override_is_honoured_by_the_coordinator()
        {
            var a = File_("src/A.cs");
            File_("src/B.cs");
            _editor.SetOpen(a);

            var solution = _repo.CreateFile("GitTabSync.slnx", "<Solution />");
            var settings = Scoped();
            settings.Set(
                SettingScope.Solution("branch/feature", "GitTabSync.slnx"),
                SyncSetting.SyncTabs,
                false);

            var coordinator = Start(settings, solutionFilePath: solution);
            coordinator.CaptureSnapshot();

            _store.Save(_repo.Path, new Model.TabSession
            {
                HeadKey = "branch/feature",
                Tabs = { new Model.TabEntry { Path = "src/B.cs", IsRepositoryRelative = true } },
            });

            SwitchTo("feature");

            // The settings window offers a solution scope. A scope the UI can set but the
            // coordinator never consults is a control that does nothing.
            Assert.Equal(new[] { a }, _editor.Open.Select(t => t.AbsolutePath).ToArray());
        }

        [Fact]
        public void RestoreCurrent_applies_the_stored_session_on_request()
        {
            var a = File_("src/A.cs");
            var b = File_("src/B.cs");
            _editor.SetOpen(a);

            var coordinator = Start();
            coordinator.CaptureSnapshot();

            _store.Save(_repo.Path, new Model.TabSession
            {
                HeadKey = "branch/main",
                Tabs = { new Model.TabEntry { Path = "src/B.cs", IsRepositoryRelative = true } },
            });

            // The only restore that is not a reaction to something. Turning tab syncing back on
            // for the branch you are standing on has nothing to react to, and rearranging the
            // editor the moment a checkbox changed would be worse than a button.
            coordinator.RestoreCurrent();

            Assert.Equal(new[] { b }, _editor.Open.Select(t => t.AbsolutePath).ToArray());
        }

        [Fact]
        public void RestoreCurrent_does_nothing_when_head_cannot_be_read()
        {
            File.WriteAllText(_headPath, string.Empty);
            var a = File_("src/A.cs");
            _editor.SetOpen(a);

            var coordinator = Start();
            coordinator.RestoreCurrent();

            Assert.Equal(new[] { a }, _editor.Open.Select(t => t.AbsolutePath).ToArray());
            Assert.Equal(0, _editor.ApplyCallCount);
        }

        [Fact]
        public void RestoreCurrent_still_respects_a_branch_with_tab_syncing_off()
        {
            var a = File_("src/A.cs");
            File_("src/B.cs");
            _editor.SetOpen(a);

            var settings = Scoped();
            settings.Set(SettingScope.Branch("branch/main"), SyncSetting.SyncTabs, false);

            var coordinator = Start(settings);
            _store.Save(_repo.Path, new Model.TabSession
            {
                HeadKey = "branch/main",
                Tabs = { new Model.TabEntry { Path = "src/B.cs", IsRepositoryRelative = true } },
            });

            coordinator.RestoreCurrent();

            // Asking for a restore is not a way round the setting; it is a way to apply it.
            Assert.Equal(new[] { a }, _editor.Open.Select(t => t.AbsolutePath).ToArray());
        }

        // ---- failures ----
        //
        // "Storage and restore failures are swallowed and logged, not thrown: losing a remembered
        // tab set beats failing a branch switch." That is the rule the whole error handling in the
        // coordinator exists to implement, and until these tests it was the only design rule in
        // the project with no coverage at all — every catch block could have been deleted and the
        // suite would still have been green.

        [Fact]
        public void A_failing_save_does_not_cost_the_incoming_branch_its_restore()
        {
            var a = File_("src/A.cs");
            var b = File_("src/B.cs");
            _editor.SetOpen(a);

            var coordinator = Start();
            coordinator.CaptureSnapshot();

            _store.Save(_repo.Path, new Model.TabSession
            {
                HeadKey = "branch/feature",
                Tabs = { new Model.TabEntry { Path = "src/B.cs", IsRepositoryRelative = true } },
            });
            _store.FailOnSave = new IOException("the session file is locked");

            SwitchTo("feature");

            // The save and the restore are independent halves of one switch. Losing the outgoing
            // branch's tabs is a bad day; losing the incoming branch's as well, because the first
            // half threw, is the same bad day twice.
            Assert.Equal(new[] { b }, _editor.Open.Select(t => t.AbsolutePath).ToArray());
            Assert.Single(_log.Errors);
        }

        [Fact]
        public void A_failing_load_leaves_the_open_tabs_untouched()
        {
            var a = File_("src/A.cs");
            _editor.SetOpen(a);

            var coordinator = Start();
            coordinator.CaptureSnapshot();
            _store.FailOnLoad = new IOException("the session file is unreadable");

            SwitchTo("feature");

            // An unreadable session is not the same as an empty one. Treating it as "this branch
            // wants no tabs" would close the user's documents because of a transient disk error.
            Assert.Equal(new[] { a }, _editor.Open.Select(t => t.AbsolutePath).ToArray());
            Assert.Equal(0, _editor.ApplyCallCount);
            Assert.Single(_log.Errors);
        }

        [Fact]
        public void A_failing_restore_does_not_stop_later_captures()
        {
            var a = File_("src/A.cs");
            var b = File_("src/B.cs");
            _editor.SetOpen(a);

            var coordinator = Start();
            coordinator.CaptureSnapshot();

            _store.Save(_repo.Path, new Model.TabSession
            {
                HeadKey = "branch/feature",
                Tabs = { new Model.TabEntry { Path = "src/B.cs", IsRepositoryRelative = true } },
            });
            _editor.FailOnApplyTabs = new InvalidOperationException("the shell refused the window");

            SwitchTo("feature");

            // The guard that stops a restore's own churn from overwriting the snapshot is set
            // before ApplyTabs and cleared in a finally. If a throw could leave it set, every
            // capture from here on would be dropped in silence and the extension would keep
            // saving the tab set it happened to hold at this instant.
            _editor.FailOnApplyTabs = null;
            _editor.SetOpen(a, b);
            coordinator.CaptureSnapshot();

            SwitchTo("main");

            Assert.Equal(
                new[] { "src/A.cs", "src/B.cs" },
                _store.Get(_repo.Path, "branch/feature")!.Tabs.Select(t => t.Path).ToArray());
        }

        [Fact]
        public void A_failing_capture_keeps_the_last_good_snapshot()
        {
            var a = File_("src/A.cs");
            _editor.SetOpen(a);

            var coordinator = Start();
            coordinator.CaptureSnapshot();

            _editor.FailOnGetOpenTabs = new InvalidOperationException("the window enumeration failed");
            coordinator.CaptureSnapshot();
            _editor.FailOnGetOpenTabs = null;

            SwitchTo("feature");

            // A failed read carries no information. Letting it empty the snapshot would turn one
            // unlucky moment into a branch remembered as having nothing open.
            Assert.Equal("src/A.cs", _store.Get(_repo.Path, "branch/main")!.Tabs.Single().Path);
        }

        [Fact]
        public void The_snapshot_is_still_saved_when_the_pinned_state_cannot_be_re_read()
        {
            var a = File_("src/A.cs");
            _editor.SetOpen(new EditorTab(a, isPinned: true));

            var coordinator = Start();
            coordinator.CaptureSnapshot();

            // The save path reads the editor once more, for the pinned flags alone. That read is
            // an improvement on the snapshot, not a prerequisite for it: if it throws, the right
            // answer is to write the slightly stale flags rather than to abandon the save.
            _editor.FailOnGetOpenTabs = new InvalidOperationException("the window enumeration failed");

            SwitchTo("feature");

            var saved = _store.Get(_repo.Path, "branch/main");
            Assert.Equal("src/A.cs", saved!.Tabs.Single().Path);
            Assert.True(saved.Tabs[0].IsPinned);
            Assert.Single(_log.Errors);
        }

        public void Dispose()
        {
            _coordinator?.Dispose();
            _monitor?.Dispose();
            _repo.Dispose();
        }
    }
}
