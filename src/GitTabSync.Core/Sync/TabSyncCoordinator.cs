using System;
using System.Collections.Generic;
using System.Globalization;
using GitTabSync.Git;
using GitTabSync.Model;
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
    /// </remarks>
    public sealed class TabSyncCoordinator : IDisposable
    {
        private readonly GitRepository _repository;
        private readonly BranchMonitor _monitor;
        private readonly ISessionStore _store;
        private readonly IEditorTabs _editor;
        private readonly TabSyncOptions _options;
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
            TabSyncOptions? options = null,
            ITabSyncLog? log = null,
            Func<DateTime>? utcNow = null)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _editor = editor ?? throw new ArgumentNullException(nameof(editor));
            _options = options ?? new TabSyncOptions();
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

            if (_options.RestoreOnStartup)
            {
                Restore(head);
            }
        }

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
            IReadOnlyList<EditorTab> tabs;
            int activeIndex;

            lock (_gate)
            {
                tabs = _snapshot;
                activeIndex = _snapshotActiveIndex;
            }

            try
            {
                var session = TabSessionMapper.ToSession(
                    tabs, activeIndex, _repository.WorkingDirectory, head.SessionKey, _utcNow());

                _store.Save(_repository.WorkingDirectory, session);

                _log.Info(string.Format(
                    CultureInfo.InvariantCulture, "Saved {0} tab(s) for {1}.", session.Tabs.Count, head));
            }
            catch (Exception e)
            {
                _log.Error("Failed to save the tab session for " + head + ".", e);
            }
        }

        private void Restore(GitHead head)
        {
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
                if (!_options.CloseTabsWhenBranchHasNoSession)
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
                _log.Info(string.Format(CultureInfo.InvariantCulture, "Restored {0} tab(s) for {1}.", tabs.Count, head));
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
