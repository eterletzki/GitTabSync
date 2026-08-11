using System;
using System.IO;
using System.Linq;
using GitTabSync.Git;
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
        private readonly string _headPath;
        private readonly GitRepository _repository;

        private BranchMonitor? _monitor;
        private TabSyncCoordinator? _coordinator;

        public TabSyncCoordinatorTests()
        {
            _headPath = _repo.CreateFile(".git/HEAD", "ref: refs/heads/main\n");
            _repository = GitRepository.Discover(_repo.Path)!;
        }

        private TabSyncCoordinator Start(TabSyncOptions? options = null)
        {
            _monitor = new BranchMonitor(_repository, pollInterval: TimeSpan.Zero);
            _coordinator = new TabSyncCoordinator(
                _repository,
                _monitor,
                _store,
                _editor,
                options ?? new TabSyncOptions { RestoreOnStartup = false });

            _coordinator.Start();
            return _coordinator;
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

        public void Dispose()
        {
            _coordinator?.Dispose();
            _monitor?.Dispose();
            _repo.Dispose();
        }
    }
}
