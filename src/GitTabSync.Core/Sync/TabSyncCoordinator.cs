using System;
using System.Collections.Generic;
using System.Globalization;
using GitTabSync.Git;
using GitTabSync.Model;
using GitTabSync.Settings;
using GitTabSync.Storage;

namespace GitTabSync.Sync
{
    /// <summary>
    /// Ties branch detection, storage and the editor together: saves the outgoing branch's tabs
    /// and restores the incoming branch's tabs when HEAD moves.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The central constraint is that a branch switch is <em>observed after the fact</em>. By the
    /// time HEAD has changed, Visual Studio may already have reacted to files appearing and
    /// disappearing, so asking the editor at that moment what was open no longer describes the
    /// branch being left.
    /// </para>
    /// <para>
    /// The coordinator therefore keeps a continuously refreshed snapshot of the open tabs
    /// (<see cref="CaptureSnapshot"/>, driven by the host's document events) and persists
    /// <em>that</em> against the outgoing branch, never a reading taken at switch time.
    /// </para>
    /// <para>
    /// Settings are resolved at each decision, against the head that decision is about, and never
    /// held. Settings can differ per branch, so a value resolved once and kept would apply the
    /// outgoing branch's configuration to the incoming one — on the one code path where the two
    /// heads are guaranteed to differ.
    /// </para>
    /// </remarks>
    public sealed class TabSyncCoordinator : IDisposable
    {
        private readonly GitRepository _repository;
        private readonly BranchMonitor _monitor;
        private readonly ISessionStore _store;
        private readonly IEditorTabs _editor;
        private readonly ISyncSettings _settings;
        private readonly ITabSyncLog _log;
        private readonly Func<DateTime> _utcNow;
        private readonly object _gate = new object();

        private IReadOnlyList<EditorTab> _snapshot = Array.Empty<EditorTab>();
        private int _snapshotActiveIndex = -1;
        private bool _applying;
        private bool _started;
        private bool _disposed;

        public TabSyncCoordinator(
            GitRepository repository,
            BranchMonitor monitor,
            ISessionStore store,
            IEditorTabs editor,
            ISyncSettings? settings = null,
            ITabSyncLog? log = null,
            Func<DateTime>? utcNow = null)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _editor = editor ?? throw new ArgumentNullException(nameof(editor));
            _settings = settings ?? new TabSyncOptions();
            _log = log ?? NullTabSyncLog.Instance;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        public void Start()
        {
            lock (_gate)
            {
                if (_disposed || _started)
                {
                    return;
                }

                _started = true;
            }

            _monitor.BranchChanged += OnBranchChanged;
            _monitor.Start();

            CaptureSnapshot();

            var head = _monitor.Current;
            if (head is null)
            {
                _log.Info("HEAD could not be read; tab syncing is idle until it becomes readable.");
                return;
            }

            _log.Info(string.Format(CultureInfo.InvariantCulture, "Watching {0} on {1}.", _repository.WorkingDirectory, head));

            if (_settings.IsEnabled(SyncSetting.RestoreOnStartup, ContextFor(head)))
            {
                Restore(head);
            }
        }

        /// <summary>
        /// The situation a setting is resolved against. Branch-level: the coordinator knows the
        /// repository and the head, and nothing about which solution or project a document belongs
        /// to — the host would have to supply that, and does not yet.
        /// </summary>
        private SyncContext ContextFor(GitHead head) =>
            new SyncContext(_repository.WorkingDirectory, head.SessionKey);

        /// <summary>
        /// Refreshes the tracked snapshot of open tabs. The host calls this whenever documents
        /// open, close or are reordered.
        /// </summary>
        public void CaptureSnapshot()
        {
            lock (_gate)
            {
                if (_disposed || _applying)
                {
                    // Ignore the churn caused by our own restore: those intermediate states do
                    // not describe what the user had open.
                    return;
                }
            }

            try
            {
                var tabs = _editor.GetOpenTabs();
                var activeIndex = _editor.GetActiveTabIndex();

                lock (_gate)
                {
                    if (_applying)
                    {
                        return;
                    }

                    _snapshot = tabs ?? Array.Empty<EditorTab>();
                    _snapshotActiveIndex = activeIndex;
                }
            }
            catch (Exception e)
            {
                _log.Error("Failed to read the open documents.", e);
            }
        }

        /// <summary>
        /// Persists the tracked snapshot against the current HEAD. Called on solution close and
        /// shutdown, where no branch change will arrive to trigger a save.
        /// </summary>
        public void SaveCurrentSession()
        {
            var head = _monitor.Current;
            if (head is null)
            {
                return;
            }

            SaveSnapshotFor(head);
        }

        private void OnBranchChanged(object sender, BranchChangedEventArgs e)
        {
            try
            {
                // Serialised so a rapid sequence of switches cannot interleave a save of one
                // branch with a restore of another.
                lock (_gate)
                {
                    if (_disposed)
                    {
                        return;
                    }
                }

                if (e.Previous is not null)
                {
                    SaveSnapshotFor(e.Previous);
                }

                _log.Info(string.Format(
                    CultureInfo.InvariantCulture,
                    "HEAD moved {0} -> {1}.",
                    e.Previous?.ToString() ?? "(unknown)",
                    e.Current));

                Restore(e.Current);
            }
            catch (Exception exception)
            {
                // This runs on a timer thread; an escape would take the process down.
                _log.Error("Failed to handle a branch change.", exception);
            }
        }

        private void SaveSnapshotFor(GitHead head)
        {
            if (!_settings.IsEnabled(SyncSetting.SyncTabs, ContextFor(head)))
            {
                // Deliberately does not delete what is already stored, so turning the setting off
                // means "stop touching this branch" rather than "forget it". What was remembered
                // survives until the next save for this branch happens with syncing back on.
                // Capturing carries on regardless: the snapshot is per-repository, costs nothing,
                // and keeping it current is what makes re-enabling take effect immediately.
                _log.Info("Tab syncing is off for " + head + "; the stored session was left as it was.");
                return;
            }

            IReadOnlyList<EditorTab> tabs;
            int activeIndex;

            lock (_gate)
            {
                tabs = _snapshot;
                activeIndex = _snapshotActiveIndex;
            }

            tabs = RefreshPinnedState(tabs);

            try
            {
                var session = TabSessionMapper.ToSession(
                    tabs, activeIndex, _repository.WorkingDirectory, head.SessionKey, _utcNow());

                _store.Save(_repository.WorkingDirectory, session);

                _log.Info(string.Format(
                    CultureInfo.InvariantCulture,
                    "Saved {0} tab(s) ({1} pinned) for {2}.",
                    session.Tabs.Count,
                    CountPinned(tabs),
                    head));
            }
            catch (Exception e)
            {
                _log.Error("Failed to save the tab session for " + head + ".", e);
            }
        }

        /// <summary>
        /// Replaces the snapshot's pinned flags with what the editor reports at this instant, for
        /// the documents it still has open. Membership and order come from the snapshot and are
        /// not touched.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Pinning a tab changes no document, so no host notification is guaranteed to arrive: the
        /// snapshot's copy of the pinned state can be arbitrarily old, and a tab pinned a moment
        /// before a branch switch would otherwise be saved as unpinned. Asking the editor here
        /// needs no notification at all.
        /// </para>
        /// <para>
        /// This does not contradict the rule that the tab <em>set</em> is never read at switch
        /// time. That rule exists because the checkout has already closed tabs for deleted files,
        /// so the live editor no longer describes the branch being left. Membership is still taken
        /// entirely from the snapshot; a document the editor no longer lists keeps the flag it was
        /// last known to have, and a document the editor has opened since is ignored.
        /// </para>
        /// <para>
        /// Only the pinned flag is refreshed, deliberately. A checkout reloads files whose content
        /// changed, which can move or reset the caret — so the live caret at this moment may be
        /// worse than the snapshot's, while pin state survives a reload untouched.
        /// </para>
        /// </remarks>
        private IReadOnlyList<EditorTab> RefreshPinnedState(IReadOnlyList<EditorTab> tabs)
        {
            if (tabs.Count == 0)
            {
                return tabs;
            }

            IReadOnlyList<EditorTab> live;
            try
            {
                live = _editor.GetOpenTabs() ?? Array.Empty<EditorTab>();
            }
            catch (Exception e)
            {
                // The snapshot's flags are stale but plausible; saving them beats saving nothing.
                _log.Error("Failed to re-read the pinned tabs; saving the tracked snapshot as it is.", e);
                return tabs;
            }

            var open = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pinned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tab in live)
            {
                open.Add(tab.AbsolutePath);
                if (tab.IsPinned)
                {
                    pinned.Add(tab.AbsolutePath);
                }
            }

            var refreshed = new List<EditorTab>(tabs.Count);
            var changed = 0;

            foreach (var tab in tabs)
            {
                if (!open.Contains(tab.AbsolutePath))
                {
                    // Already closed — by the checkout, or by the user. Keep what was last known.
                    refreshed.Add(tab);
                    continue;
                }

                var isPinned = pinned.Contains(tab.AbsolutePath);
                if (isPinned == tab.IsPinned)
                {
                    refreshed.Add(tab);
                    continue;
                }

                changed++;
                refreshed.Add(new EditorTab(tab.AbsolutePath, tab.CaretLine, tab.CaretColumn, isPinned));
            }

            if (changed == 0)
            {
                return tabs;
            }

            _log.Info(string.Format(
                CultureInfo.InvariantCulture,
                "The pinned state of {0} tab(s) had changed since the last capture.",
                changed));

            return refreshed;
        }

        private static int CountPinned(IReadOnlyList<EditorTab> tabs)
        {
            var count = 0;
            foreach (var tab in tabs)
            {
                if (tab.IsPinned)
                {
                    count++;
                }
            }

            return count;
        }

        private void Restore(GitHead head)
        {
            var context = ContextFor(head);

            if (!_settings.IsEnabled(SyncSetting.SyncTabs, context))
            {
                _log.Info("Tab syncing is off for " + head + "; the open tabs were left alone.");
                return;
            }

            TabSession? session;
            try
            {
                session = _store.Load(_repository.WorkingDirectory, head.SessionKey);
            }
            catch (Exception e)
            {
                _log.Error("Failed to load the tab session for " + head + ".", e);
                return;
            }

            IReadOnlyList<EditorTab> tabs;
            int activeIndex;

            if (session is null)
            {
                if (!_settings.IsEnabled(SyncSetting.CloseTabsWhenBranchHasNoSession, context))
                {
                    _log.Info("No stored session for " + head + "; leaving the open tabs alone.");
                    return;
                }

                tabs = Array.Empty<EditorTab>();
                activeIndex = -1;
            }
            else
            {
                tabs = TabSessionMapper.ToEditorTabs(session, _repository.WorkingDirectory, out activeIndex);

                var dropped = session.Tabs.Count - tabs.Count;
                if (dropped > 0)
                {
                    _log.Info(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} stored tab(s) do not exist on {1} and were skipped.",
                        dropped,
                        head));
                }
            }

            lock (_gate)
            {
                _applying = true;
            }

            try
            {
                _editor.ApplyTabs(tabs, activeIndex);
                _log.Info(string.Format(
                    CultureInfo.InvariantCulture,
                    "Restored {0} tab(s) ({1} pinned) for {2}.",
                    tabs.Count,
                    CountPinned(tabs),
                    head));
            }
            catch (Exception e)
            {
                _log.Error("Failed to restore tabs for " + head + ".", e);
            }
            finally
            {
                lock (_gate)
                {
                    _applying = false;
                    _snapshot = tabs;
                    _snapshotActiveIndex = activeIndex;
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
            }

            _monitor.BranchChanged -= OnBranchChanged;
        }
    }
}
