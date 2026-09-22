using System;
using GitTabSync.Settings;
using GitTabSync.Sync;

namespace GitTabSync.Release
{
    /// <summary>
    /// Decides whether the What's New page opens on startup, and records that it did.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything about <em>what</em> to show is in <see cref="LandingPageDecision"/>. What is here
    /// is the one thing that needs storage, and the rule that makes it safe.
    /// </para>
    /// <para>
    /// <strong>The page is not shown unless the record of having shown it can be read back.</strong>
    /// This inverts the storage rule the rest of the extension follows. Everywhere else a failed
    /// write costs a convenience — a remembered tab set, a theme choice — and swallowing it is
    /// plainly right. Here, a failed write costs the user this page on <em>every startup, forever</em>,
    /// because the condition that opened it is still true next time. Between annoying somebody
    /// once a day for the life of the install and never showing them a page they did not ask for,
    /// silence is the better failure. The write is therefore done first and verified by loading it
    /// back, which tests the thing that matters — that the version is recorded — rather than
    /// trusting a store that reports nothing.
    /// </para>
    /// </remarks>
    public sealed class LandingPageGate
    {
        private readonly ISettingsStore _store;
        private readonly ReleaseNotes _notes;
        private readonly string _version;
        private readonly ITabSyncLog _log;

        /// <param name="store">Where the last-seen version is kept.</param>
        /// <param name="notes">The changelog. Defaults to the one this build ships.</param>
        /// <param name="version">The running version. Defaults to this build's.</param>
        public LandingPageGate(
            ISettingsStore store,
            ReleaseNotes? notes = null,
            string? version = null,
            ITabSyncLog? log = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _notes = notes ?? ShippedRelease.Notes;
            _version = version ?? ShippedRelease.Version;
            _log = log ?? NullTabSyncLog.Instance;
        }

        /// <summary>
        /// What to show now, if anything. Called once per Visual Studio session; returns
        /// <see cref="LandingPageDecision.None"/> every time after the first for a given version.
        /// </summary>
        public LandingPageDecision OnStartup()
        {
            var state = Load();
            var decision = LandingPageDecision.Decide(_version, state.LastSeenVersion, _notes);

            if (!decision.ShouldShow)
            {
                return LandingPageDecision.None;
            }

            if (!Record(state))
            {
                _log.Info(
                    "The What's New page for " + _version + " was not shown: the record of having"
                    + " shown it could not be saved, and a page that cannot be dismissed would"
                    + " open on every startup.");

                return LandingPageDecision.None;
            }

            _log.Info(
                decision.Kind == LandingPageKind.Welcome
                    ? "Showing the What's New page for a first install of " + _version + "."
                    : "Showing the What's New page: upgraded to " + _version + " from "
                        + state.LastSeenVersion + ".");

            return decision;
        }

        /// <summary>
        /// The page somebody asked for from the menu. Always has something to show, and marks the
        /// current version as seen — they are looking at it.
        /// </summary>
        public LandingPageDecision OnRequest()
        {
            Record(Load());
            return LandingPageDecision.OnRequest(_version, _notes);
        }

        private InstallState Load()
        {
            try
            {
                return _store.LoadInstallState();
            }
            catch (Exception e)
            {
                // The file store swallows what it expects; this is for a store that does not. An
                // unreadable state is "nothing recorded", which the decision reads as a first
                // install — but the write below has to succeed before anything is shown, so a
                // store that fails both ways stays silent rather than greeting the user daily.
                _log.Error("Failed to read the install state.", e);
                return new InstallState();
            }
        }

        /// <summary>
        /// Records that the notes for the running version have been seen, and says whether that
        /// is now true — which is not the same as whether the write threw. See the class remarks.
        /// </summary>
        private bool Record(InstallState state)
        {
            if (string.Equals(state.LastSeenVersion, _version, StringComparison.Ordinal))
            {
                return true;
            }

            state.LastSeenVersion = _version;

            try
            {
                _store.SaveInstallState(state);
            }
            catch (Exception e)
            {
                _log.Error("Failed to record the version whose release notes have been shown.", e);
                return false;
            }

            try
            {
                return string.Equals(_store.LoadInstallState().LastSeenVersion, _version, StringComparison.Ordinal);
            }
            catch (Exception e)
            {
                _log.Error("Failed to confirm the version whose release notes have been shown.", e);
                return false;
            }
        }
    }
}
