using System;
using System.Threading;
using GitTabSync.Git;
using GitTabSync.Storage;
using GitTabSync.Sync;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;

namespace GitTabSync
{
    /// <summary>
    /// Everything that is alive while one repository is open: the branch monitor, the coordinator,
    /// and the running document table subscription that keeps the tab snapshot current.
    /// </summary>
    internal sealed class RepositorySyncSession : IVsRunningDocTableEvents, IDisposable
    {
        /// <summary>
        /// Document events arrive in bursts and while the document is still half-open or
        /// half-closed. Capturing after a short quiet period reads a settled editor instead.
        /// </summary>
        private static readonly TimeSpan CaptureDelay = TimeSpan.FromMilliseconds(300);

        private readonly BranchMonitor _monitor;
        private readonly TabSyncCoordinator _coordinator;
        private readonly IVsRunningDocumentTable? _runningDocumentTable;
        private readonly Timer _captureTimer;
        private readonly ITabSyncLog _log;
        private readonly uint _rdtCookie;

        private bool _disposed;

        private RepositorySyncSession(
            BranchMonitor monitor,
            TabSyncCoordinator coordinator,
            IVsRunningDocumentTable? runningDocumentTable,
            ITabSyncLog log)
        {
            _monitor = monitor;
            _coordinator = coordinator;
            _runningDocumentTable = runningDocumentTable;
            _log = log;
            _captureTimer = new Timer(_ => CaptureNow(), null, Timeout.Infinite, Timeout.Infinite);

            ThreadHelper.ThrowIfNotOnUIThread();

            if (_runningDocumentTable is not null)
            {
                _runningDocumentTable.AdviseRunningDocTableEvents(this, out _rdtCookie);
            }
        }

        /// <summary>
        /// Starts syncing for the repository containing <paramref name="pathInsideRepository"/>.
        /// Returns <c>null</c> when that path is not in a git repository, which is the normal
        /// case for a solution that is not version controlled.
        /// </summary>
        public static RepositorySyncSession? TryCreate(
            string pathInsideRepository,
            IServiceProvider serviceProvider,
            JoinableTaskFactory joinableTaskFactory,
            TabSyncOptions options,
            ITabSyncLog log)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var repository = GitRepository.Discover(pathInsideRepository);
            if (repository is null)
            {
                log.Info("No git repository found for " + pathInsideRepository + "; tab syncing is off.");
                return null;
            }

            var monitor = new BranchMonitor(repository);
            var editor = new VsEditorTabs(serviceProvider, joinableTaskFactory, log);
            var coordinator = new TabSyncCoordinator(
                repository, monitor, new FileSessionStore(), editor, options, log);

            var runningDocumentTable = serviceProvider.GetService(typeof(SVsRunningDocumentTable)) as IVsRunningDocumentTable;
            var session = new RepositorySyncSession(monitor, coordinator, runningDocumentTable, log);

            coordinator.Start();
            return session;
        }

        /// <summary>Re-reads HEAD now, for moments a file watcher cannot observe.</summary>
        public void CheckForBranchChange() => _monitor.CheckNow();

        public void SaveCurrentSession() => _coordinator.SaveCurrentSession();

        private void ScheduleCapture()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                _captureTimer.Change(CaptureDelay, Timeout.InfiniteTimeSpan);
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void CaptureNow()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                _coordinator.CaptureSnapshot();
            }
            catch (Exception e)
            {
                // Runs on a timer thread; an escape would take Visual Studio down.
                _log.Error("Failed to capture the open documents.", e);
            }
        }

        public int OnAfterFirstDocumentLock(uint docCookie, uint lockType, uint readLocksRemaining, uint editLocksRemaining)
        {
            ScheduleCapture();
            return VSConstants.S_OK;
        }

        public int OnBeforeLastDocumentUnlock(uint docCookie, uint lockType, uint readLocksRemaining, uint editLocksRemaining)
        {
            ScheduleCapture();
            return VSConstants.S_OK;
        }

        public int OnAfterSave(uint docCookie) => VSConstants.S_OK;

        public int OnAfterAttributeChange(uint docCookie, uint grfAttribs) => VSConstants.S_OK;

        public int OnBeforeDocumentWindowShow(uint docCookie, int fFirstShow, IVsWindowFrame pFrame)
        {
            // Activation changes which tab is the active one, which is part of the session.
            ScheduleCapture();
            return VSConstants.S_OK;
        }

        public int OnAfterDocumentWindowHide(uint docCookie, IVsWindowFrame pFrame)
        {
            ScheduleCapture();
            return VSConstants.S_OK;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            ThreadHelper.ThrowIfNotOnUIThread();

            if (_runningDocumentTable is not null && _rdtCookie != 0)
            {
                _runningDocumentTable.UnadviseRunningDocTableEvents(_rdtCookie);
            }

            _captureTimer.Dispose();
            _coordinator.Dispose();
            _monitor.Dispose();
        }
    }
}
