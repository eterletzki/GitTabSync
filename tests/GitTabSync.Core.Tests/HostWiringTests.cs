using System;
using System.IO;
using System.Linq;
using System.Threading;
using GitTabSync.Git;
using GitTabSync.Sync;
using GitTabSync.Tests.TestSupport;
using Xunit;

namespace GitTabSync.Tests
{
    /// <summary>
    /// The coordinator, the capture scheduler and a host that reports some of what it does — wired
    /// together exactly as <c>RepositorySyncSession</c> wires them, and driven only from the host
    /// side.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Nothing in this file may call <see cref="TabSyncCoordinator.CaptureSnapshot"/>.</strong>
    /// That is the whole point. The bug that shipped survived a green suite because the tests
    /// called it by hand and thereby supplied the one input the production path could not produce:
    /// a snapshot that knew about a pin. Tests that arrange the broken step cannot fail because of
    /// it.
    /// </para>
    /// <para>
    /// Here the only way anything reaches the snapshot is a real notification through a real
    /// <see cref="CaptureScheduler"/>, so these tests fail if the wiring is wrong, if the debounce
    /// swallows something, or if the extension depends on being told about a change the host never
    /// reports.
    /// </para>
    /// </remarks>
    public sealed class HostWiringTests : IDisposable
    {
        /// <summary>Short: most tests wait for the capture rather than racing it.</summary>
        private static readonly TimeSpan CaptureDelay = TimeSpan.FromMilliseconds(60);

        /// <summary>Long enough that a scheduled capture provably has not run yet.</summary>
        private static readonly TimeSpan NeverInThisTest = TimeSpan.FromMinutes(5);

        private static readonly TimeSpan Eventually = TimeSpan.FromSeconds(10);

        private readonly TempDirectory _repo = new TempDirectory("repo");
        private readonly FakeHostEditor _editor = new FakeHostEditor();
        private readonly InMemorySessionStore _store = new InMemorySessionStore();
        private readonly string _headPath;
        private readonly GitRepository _repository;

        private BranchMonitor? _monitor;
        private TabSyncCoordinator? _coordinator;
        private CaptureScheduler? _scheduler;

        public HostWiringTests()
        {
            _headPath = _repo.CreateFile(".git/HEAD", "ref: refs/heads/main\n");
            _repository = GitRepository.Discover(_repo.Path)!;
        }

        /// <summary>Builds the same object graph the VSIX builds, minus Visual Studio.</summary>
        private void Start(TimeSpan? captureDelay = null)
        {
            _monitor = new BranchMonitor(_repository, pollInterval: TimeSpan.Zero);
            _coordinator = new TabSyncCoordinator(
                _repository,
                _monitor,
                _store,
                _editor,
                new TabSyncOptions { RestoreOnStartup = false });

            _scheduler = new CaptureScheduler(_coordinator.CaptureSnapshot, captureDelay ?? CaptureDelay);
            _editor.Reported += _scheduler.Schedule;

            _coordinator.Start();
        }

        private void SwitchTo(string branch)
        {
            File.WriteAllText(_headPath, "ref: refs/heads/" + branch + "\n");
            _monitor!.CheckNow();
        }

        private string File_(string relativePath) => _repo.CreateFile(relativePath, "// code");

        /// <summary>
        /// Runs a host action that should end in a capture, and waits for that capture to land.
        /// </summary>
        /// <remarks>
        /// The baseline is taken <em>before</em> the action. Taking it afterwards races the
        /// scheduler: on a slow machine the capture can already have run by then, and the test
        /// would sit waiting for a second one that nothing is going to trigger, failing after the
        /// full timeout with a message blaming the wiring.
        /// </remarks>
        private void Reporting(Action report, string because = "The reported change never reached a capture.")
        {
            var target = _editor.Reads + 1;
            report();
            Assert.True(_editor.WaitForReads(target, Eventually), because);
        }

        /// <summary>
        /// Waits until the editor has been left alone for several capture delays.
        /// </summary>
        /// <remarks>
        /// Only needed where a capture is already in flight when the next step begins: without
        /// this, <see cref="Reporting"/> can be satisfied by the earlier capture and the test moves
        /// on before the one it actually cares about has happened.
        /// </remarks>
        private void Quiesce()
        {
            var settle = CaptureDelay + CaptureDelay + CaptureDelay;
            var deadline = DateTime.UtcNow + Eventually;

            while (DateTime.UtcNow < deadline)
            {
                var before = _editor.Reads;
                Thread.Sleep(settle);
                if (_editor.Reads == before)
                {
                    return;
                }
            }

            Assert.Fail("The editor was never left alone.");
        }

        private Model.TabSession Saved(string branch) =>
            _store.Get(_repo.Path, "branch/" + branch) ?? throw new InvalidOperationException("nothing saved");

        [Fact]
        public void A_change_the_host_reports_reaches_the_session()
        {
            Start();
            var a = File_("src/A.cs");

            Reporting(() => _editor.OpenAndReport(a));

            SwitchTo("feature");

            // The baseline: the notification path works at all. Everything below is about what
            // happens when it does not.
            Assert.Equal("src/A.cs", Saved("main").Tabs.Single().Path);
        }

        [Fact]
        public void A_pin_the_host_never_reports_is_saved_anyway()
        {
            Start();
            var a = File_("src/A.cs");
            Reporting(() => _editor.OpenAndReport(a));

            // The failure that shipped. Pinning raises nothing, so no capture happens and the
            // snapshot still says "not pinned" at the moment the branch moves.
            _editor.PinSilently(a);

            SwitchTo("feature");

            Assert.True(Saved("main").Tabs.Single().IsPinned);
        }

        [Fact]
        public void A_pin_is_saved_even_when_a_capture_is_still_pending()
        {
            // The document is already open when the solution loads, so the one capture this test
            // allows — the one Start does — puts it in the snapshot unpinned.
            var a = File_("src/A.cs");
            _editor.OpenSilently(a);

            // A capture delay no test can outlast: after this line nothing may read the editor
            // again except the save itself.
            Start(NeverInThisTest);

            _editor.PinSilently(a);

            SwitchTo("feature");

            // The tab itself comes from the snapshot taken at startup; the pinned flag has to have
            // been read at save time, because nothing else read the editor after the pin.
            Assert.True(Saved("main").Tabs.Single().IsPinned);
        }

        [Fact]
        public void An_unpin_the_host_never_reports_is_saved_too()
        {
            Start();
            var a = File_("src/A.cs");
            _editor.OpenAndReport(a);
            _editor.PinSilently(a);

            // An unrelated report — clicking another tab — is what drags the pin into the snapshot.
            // The wait starts after the pin, so whichever capture satisfies it has seen the pin;
            // waiting for "some capture" without that ordering would let the test pass with an
            // unpinned snapshot and prove nothing.
            Reporting(() => _editor.ActivateAndReport(a));

            _editor.UnpinSilently(a);

            SwitchTo("feature");

            Assert.False(Saved("main").Tabs.Single().IsPinned);
        }

        [Fact]
        public void A_tab_the_checkout_closed_before_the_switch_keeps_its_pinned_state()
        {
            Start();
            var a = File_("src/A.cs");
            _editor.OpenAndReport(a);
            _editor.PinSilently(a);
            Reporting(() => _editor.ActivateAndReport(a));

            // The checkout deleted the file and Visual Studio closed the tab before the branch
            // monitor noticed. The editor cannot report a pinned state for a window that is gone,
            // and "gone" must not be read as "not pinned".
            _editor.CloseSilently(a);

            SwitchTo("feature");

            var saved = Saved("main").Tabs.Single();
            Assert.Equal("src/A.cs", saved.Path);
            Assert.True(saved.IsPinned);
        }

        [Fact]
        public void A_document_the_host_never_reported_stays_out_of_the_session()
        {
            Start();
            var a = File_("src/A.cs");
            var b = File_("src/B.cs");
            Reporting(() => _editor.OpenAndReport(a));

            // Visual Studio opened B.cs itself as part of the checkout. Re-reading the editor at
            // save time must not let it into the outgoing branch's session — the set comes from
            // the snapshot, and this is the deliberate limit of the pinned-state refresh.
            _editor.OpenSilently(b);
            _editor.PinSilently(b);

            SwitchTo("feature");

            Assert.Equal(new[] { "src/A.cs" }, Saved("main").Tabs.Select(t => t.Path).ToArray());
        }

        [Fact]
        public void A_caret_move_the_host_never_reported_does_not_reach_the_session()
        {
            Start();
            var a = File_("src/A.cs");
            Reporting(() => _editor.OpenAndReport(a, caretLine: 10, caretColumn: 3));

            // Only the pinned flag is re-read at save time. A checkout reloads files whose content
            // changed, which can move the caret, so the live value here may be worse than the
            // snapshot's — this pins that scope from the other side.
            _editor.MoveCaretSilently(a, 99, 1);

            SwitchTo("feature");

            Assert.Equal(10, Saved("main").Tabs.Single().CaretLine);
        }

        [Fact]
        public void A_restore_leaves_the_capture_path_working()
        {
            Start();
            var a = File_("src/A.cs");
            _ = File_("src/B.cs");
            var c = File_("src/C.cs");
            Reporting(() => _editor.OpenAndReport(a));

            _store.Save(_repo.Path, new Model.TabSession
            {
                HeadKey = "branch/feature",
                Tabs = { new Model.TabEntry { Path = "src/B.cs", IsRepositoryRelative = true } },
            });

            // Applying tabs opens documents, which the host reports, which schedules a capture the
            // coordinator then drops because its own restore is what caused it.
            SwitchTo("feature");
            Quiesce();

            // The drop has to be temporary. A restore that left the guard set would silently stop
            // every later capture, and the extension would go on saving whatever was open at the
            // moment of that restore for the rest of the session — a failure that looks like
            // nothing at all until the wrong tabs come back.
            Reporting(() => _editor.OpenAndReport(c), "Nothing was captured after the restore.");

            SwitchTo("main");

            Assert.Equal(new[] { "src/B.cs", "src/C.cs" }, Saved("feature").Tabs.Select(t => t.Path).ToArray());
        }

        public void Dispose()
        {
            if (_scheduler is not null)
            {
                _editor.Reported -= _scheduler.Schedule;
                _scheduler.Dispose();
            }

            _coordinator?.Dispose();
            _monitor?.Dispose();
            _repo.Dispose();
        }
    }
}
