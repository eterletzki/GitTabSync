using System;
using System.Threading;
using EnvDTE;
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

        /// <summary>
        /// The command behind Window &gt; Pin Tab, the tab's pin glyph and the tab context menu.
        /// </summary>
        /// <remarks>
        /// Pinning changes no document, only a window frame property, so it raises no running
        /// document table event: the command that performs it is the only notification there is.
        /// The subscription is filtered to this one command, so nothing else in the IDE pays for
        /// it.
        /// </remarks>
        private static readonly string PinTabCommandSet = VSConstants.CMDSETID.StandardCommandSet11_string;

        private const int PinTabCommandId = (int)VSConstants.VSStd11CmdID.PinTab;

        private readonly BranchMonitor _monitor;
        private readonly TabSyncCoordinator _coordinator;
        private readonly IVsRunningDocumentTable? _runningDocumentTable;
        private readonly Timer _captureTimer;
        private readonly ITabSyncLog _log;
        private readonly uint _rdtCookie;

        /// <summary>
        /// Held for the lifetime of the session on purpose. A DTE event object stops raising
        /// events as soon as nothing references it, so a local would unsubscribe itself at the
        /// next collection.
        /// </summary>
        private readonly CommandEvents? _pinTabCommandEvents;

        private bool _disposed;

        private RepositorySyncSession(
            BranchMonitor monitor,
            TabSyncCoordinator coordinator,
            IVsRunningDocumentTable? runningDocumentTable,
            DTE? dte,
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

            _pinTabCommandEvents = SubscribeToPinTab(dte);
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
            var dte = serviceProvider.GetService(typeof(SDTE)) as DTE;
            var session = new RepositorySyncSession(monitor, coordinator, runningDocumentTable, dte, log);

            coordinator.Start();
            return session;
        }

        /// <summary>Re-reads HEAD now, for moments a file watcher cannot observe.</summary>
        public void CheckForBranchChange() => _monitor.CheckNow();

        public void SaveCurrentSession() => _coordinator.SaveCurrentSession();

        private CommandEvents? SubscribeToPinTab(DTE? dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (dte is null)
            {
                _log.Info("DTE is unavailable; pinning a tab will only be noticed at the next document event.");
                return null;
            }

            try
            {
                var events = dte.Events.CommandEvents[PinTabCommandSet, PinTabCommandId];
                if (events is null)
                {
                    _log.Info("The Pin Tab command could not be subscribed to; pinning a tab will only be "
                        + "noticed at the next document event.");
                    return null;
                }

                events.AfterExecute += OnPinTabExecuted;
                return events;
            }
            catch (Exception e)
            {
                _log.Error("Failed to subscribe to the Pin Tab command.", e);
                return null;
            }
        }

        private void OnPinTabExecuted(string guid, int id, object customIn, object customOut)
        {
            if (_disposed)
            {
                return;
            }

            // Captured straight away rather than through the debounce. That delay exists for
            // document events, which arrive in bursts while a document is still half-open; a pin
            // is one settled action, and the delay is exactly what would lose it to a branch
            // switch made a moment later.
            _log.Info("A tab was pinned or unpinned; capturing the open documents now.");
            CaptureNow();
        }

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

            if (_pinTabCommandEvents is not null)
            {
                _pinTabCommandEvents.AfterExecute -= OnPinTabExecuted;
            }

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
