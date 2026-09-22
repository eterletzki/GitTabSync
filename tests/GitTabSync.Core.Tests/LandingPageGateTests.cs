using System;
using System.IO;
using GitTabSync.Release;
using GitTabSync.Settings;
using GitTabSync.Tests.TestSupport;
using Xunit;

namespace GitTabSync.Tests
{
    /// <summary>
    /// The storage half of the landing page: whether it opens, and whether that is remembered.
    /// </summary>
    public class LandingPageGateTests
    {
        private static readonly ReleaseNotes Notes = ChangelogParser.Parse(
            "## 1.3.0\n\n- Three\n\n## 1.2.0\n\n- Two\n\n## 1.1.0\n\n- One\n");

        private static LandingPageGate GateFor(ISettingsStore store, string version) =>
            new LandingPageGate(store, Notes, version);

        [Fact]
        public void A_first_start_shows_the_page()
        {
            var store = new InMemorySettingsStore();

            Assert.True(GateFor(store, "1.2.0").OnStartup().ShouldShow);
        }

        [Fact]
        public void The_second_start_of_the_same_version_shows_nothing()
        {
            var store = new InMemorySettingsStore();

            Assert.True(GateFor(store, "1.2.0").OnStartup().ShouldShow);
            Assert.False(GateFor(store, "1.2.0").OnStartup().ShouldShow);
        }

        [Fact]
        public void Showing_the_page_records_the_version()
        {
            var store = new InMemorySettingsStore();

            GateFor(store, "1.2.0").OnStartup();

            Assert.Equal("1.2.0", store.LoadInstallState().LastSeenVersion);
        }

        /// <remarks>
        /// <para>
        /// The record is written while the decision is being made, not after the caller has
        /// managed to display anything — so a caller that fails, crashes or <em>hangs</em> on the
        /// way to showing the page faces a different decision next start rather than the same one.
        /// </para>
        /// <para>
        /// This is not hypothetical. Showing the page from inside the package's initialisation
        /// deadlocked Visual Studio on the first solution opened after an upgrade; what kept that
        /// from being a boot loop, and therefore from needing the extension uninstalled by hand,
        /// was that the version had already been recorded by the time the window was asked for.
        /// Move the record to after the display and this suite still passes while the IDE becomes
        /// unrecoverable, which is why the ordering is asserted here rather than left implied.
        /// </para>
        /// </remarks>
        [Fact]
        public void The_version_is_recorded_before_the_caller_has_shown_anything()
        {
            var store = new InMemorySettingsStore();

            var decision = GateFor(store, "1.2.0").OnStartup();

            // Nothing has been displayed at this point — the caller has only been told to.
            Assert.True(decision.ShouldShow);
            Assert.Equal("1.2.0", store.LoadInstallState().LastSeenVersion);

            // So a start that never manages to display it does not repeat the attempt forever.
            Assert.False(GateFor(store, "1.2.0").OnStartup().ShouldShow);
        }

        [Fact]
        public void An_upgrade_after_the_page_was_seen_shows_it_again()
        {
            var store = new InMemorySettingsStore();

            Assert.Equal(LandingPageKind.Welcome, GateFor(store, "1.1.0").OnStartup().Kind);
            Assert.Equal(LandingPageKind.Update, GateFor(store, "1.3.0").OnStartup().Kind);
        }

        /// <remarks>
        /// So the next release that does have notes covers the silent one too. Recording 1.2.0
        /// here would mean its successor's page started at 1.2.0 and skipped whatever 1.2.0
        /// actually changed.
        /// </remarks>
        [Fact]
        public void An_upgrade_with_nothing_to_report_records_nothing()
        {
            var store = new InMemorySettingsStore();
            store.SeedInstallState(new InstallState { LastSeenVersion = "1.1.0" });

            var notes = ChangelogParser.Parse("## 1.3.0\n\n- Three\n\n## 1.1.0\n\n- One\n");
            Assert.False(new LandingPageGate(store, notes, "1.2.0").OnStartup().ShouldShow);

            Assert.Equal("1.1.0", store.LoadInstallState().LastSeenVersion);
        }

        [Fact]
        public void A_silent_upgrade_is_covered_by_the_next_release_that_has_notes()
        {
            var store = new InMemorySettingsStore();
            store.SeedInstallState(new InstallState { LastSeenVersion = "1.1.0" });

            var quiet = ChangelogParser.Parse("## 1.3.0\n\n- Three\n\n## 1.1.0\n\n- One\n");
            new LandingPageGate(store, quiet, "1.2.0").OnStartup();

            var decision = new LandingPageGate(store, Notes, "1.3.0").OnStartup();

            Assert.Equal(LandingPageKind.Update, decision.Kind);
            Assert.Contains(decision.Entries, e => e.VersionText == "1.2.0");
        }

        /// <remarks>
        /// Going back to an older build is not a reason to re-show notes the user has already
        /// read, and it is not a reason to forget that they read them either.
        /// </remarks>
        [Fact]
        public void A_downgrade_shows_nothing_and_records_nothing()
        {
            var store = new InMemorySettingsStore();
            store.SeedInstallState(new InstallState { LastSeenVersion = "1.3.0" });

            Assert.False(GateFor(store, "1.1.0").OnStartup().ShouldShow);
            Assert.Equal("1.3.0", store.LoadInstallState().LastSeenVersion);
        }

        [Fact]
        public void An_unparseable_running_version_shows_nothing_and_records_nothing()
        {
            var store = new InMemorySettingsStore();

            Assert.False(GateFor(store, "not a version").OnStartup().ShouldShow);
            Assert.Equal(string.Empty, store.LoadInstallState().LastSeenVersion);
        }

        /// <remarks>
        /// <para>
        /// The rule that inverts the storage policy the rest of the extension follows. Everywhere
        /// else a swallowed write costs a convenience; here it would reopen this window on every
        /// startup for the life of the install.
        /// </para>
        /// <para>
        /// The fake accepts the save and keeps nothing, which is exactly what
        /// <see cref="FileSettingsStore"/> looks like from the outside when the write fails — it
        /// swallows the IO failure and returns void, so "succeeded" and "did nothing" are the same
        /// observation. Only loading the value back tells them apart, which is why a store that
        /// merely throws is not enough of a test.
        /// </para>
        /// </remarks>
        [Fact]
        public void The_page_is_not_shown_when_the_record_of_showing_it_cannot_be_read_back()
        {
            var store = new InMemorySettingsStore { DropsInstallStateSaves = true };

            Assert.False(GateFor(store, "1.2.0").OnStartup().ShouldShow);
        }

        [Fact]
        public void A_store_that_drops_saves_silently_still_reports_the_save_as_done()
        {
            var store = new InMemorySettingsStore { DropsInstallStateSaves = true };

            store.SaveInstallState(new InstallState { LastSeenVersion = "1.2.0" });

            // The premise of the test above: nothing threw, and nothing was kept.
            Assert.Equal(string.Empty, store.LoadInstallState().LastSeenVersion);
        }

        [Fact]
        public void The_page_is_not_shown_when_the_record_cannot_be_written_at_all()
        {
            var store = new InMemorySettingsStore { FailOnSave = new IOException("no") };

            Assert.False(GateFor(store, "1.2.0").OnStartup().ShouldShow);
        }

        [Fact]
        public void A_store_that_cannot_be_read_or_written_stays_silent_rather_than_greeting_daily()
        {
            var store = new UnusableSettingsStore();

            Assert.False(GateFor(store, "1.2.0").OnStartup().ShouldShow);
            Assert.False(GateFor(store, "1.2.0").OnStartup().ShouldShow);
        }

        [Fact]
        public void A_failure_to_record_is_logged_rather_than_thrown()
        {
            var store = new InMemorySettingsStore { DropsInstallStateSaves = true };
            var log = new RecordingTabSyncLog();

            new LandingPageGate(store, Notes, "1.2.0", log).OnStartup();

            Assert.Contains(log.Messages, m => m.Contains("What's New", StringComparison.Ordinal));
        }

        [Fact]
        public void A_page_opened_from_the_menu_always_has_something_to_show()
        {
            var store = new InMemorySettingsStore();
            store.SeedInstallState(new InstallState { LastSeenVersion = "1.3.0" });

            var decision = GateFor(store, "1.3.0").OnRequest();

            Assert.True(decision.ShouldShow);
            Assert.NotEmpty(decision.Entries);
        }

        /// <remarks>
        /// They are looking at it, so it should not open again by itself on the next start.
        /// </remarks>
        [Fact]
        public void Opening_the_page_from_the_menu_marks_the_version_as_seen()
        {
            var store = new InMemorySettingsStore();

            GateFor(store, "1.2.0").OnRequest();

            Assert.Equal("1.2.0", store.LoadInstallState().LastSeenVersion);
            Assert.False(GateFor(store, "1.2.0").OnStartup().ShouldShow);
        }

        [Fact]
        public void A_page_opened_from_the_menu_is_shown_even_when_it_cannot_be_recorded()
        {
            var store = new InMemorySettingsStore { DropsInstallStateSaves = true };

            // The read-back rule guards a page nobody asked for. This one was asked for by name,
            // so the worst a failed record can do is show it again on the next start.
            Assert.True(GateFor(store, "1.2.0").OnRequest().ShouldShow);
        }

        [Fact]
        public void The_state_survives_a_real_file_store()
        {
            using var directory = new TempDirectory();
            var store = new FileSettingsStore(directory.Path);

            Assert.True(GateFor(store, "1.2.0").OnStartup().ShouldShow);
            Assert.False(GateFor(new FileSettingsStore(directory.Path), "1.2.0").OnStartup().ShouldShow);
        }

        /// <summary>A store that fails both ways, without swallowing anything.</summary>
        private sealed class UnusableSettingsStore : ISettingsStore
        {
            public ScopedSettings LoadDefaults() => throw new IOException("no");

            public void SaveDefaults(ScopedSettings settings) => throw new IOException("no");

            public ScopedSettings Load(string repositoryWorkingDirectory) => throw new IOException("no");

            public void Save(string repositoryWorkingDirectory, ScopedSettings settings) =>
                throw new IOException("no");

            public UiPreferences LoadPreferences() => throw new IOException("no");

            public void SavePreferences(UiPreferences preferences) => throw new IOException("no");

            public InstallState LoadInstallState() => throw new IOException("no");

            public void SaveInstallState(InstallState state) => throw new IOException("no");
        }
    }
}
