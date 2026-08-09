using System;
using System.Runtime.InteropServices;
using System.Threading;
using GitTabSync.Sync;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace GitTabSync
{
    /// <summary>
    /// Entry point. Owns one <see cref="RepositorySyncSession"/> for the open solution.
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(PackageGuidString)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideOptionPage(typeof(GitTabSyncOptionsPage), "Git Tab Sync", "General", 0, 0, supportsAutomation: true)]
    public sealed class GitTabSyncPackage : AsyncPackage, IVsSolutionEvents
    {
        public const string PackageGuidString = "b7f0e2a4-3c15-4d8e-9a6b-2f5c8d1e7a30";

        private ITabSyncLog _log = NullTabSyncLog.Instance;
        private IVsSolution? _solution;
        private RepositorySyncSession? _session;
        private uint _solutionEventsCookie;

        protected override async Task InitializeAsync(
            CancellationToken cancellationToken,
            IProgress<ServiceProgressData> progress)
        {
            await base.InitializeAsync(cancellationToken, progress);

            _log = await OutputWindowLog.CreateAsync(this);

            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            _solution = await GetServiceAsync(typeof(SVsSolution)) as IVsSolution;
            _solution?.AdviseSolutionEvents(this, out _solutionEventsCookie);

            // The package autoloads on SolutionExists, so by the time it initialises the
            // solution is usually already open and OnAfterOpenSolution has been and gone.
            StartSessionIfSolutionIsOpen();
        }

        private void StartSessionIfSolutionIsOpen()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_session is not null || _solution is null)
            {
                return;
            }

            if (_solution.GetSolutionInfo(out var solutionDirectory, out _, out _) != VSConstants.S_OK)
            {
                return;
            }

            if (string.IsNullOrEmpty(solutionDirectory))
            {
                return;
            }

            try
            {
                var options = (GetDialogPage(typeof(GitTabSyncOptionsPage)) as GitTabSyncOptionsPage)?.ToOptions()
                    ?? new TabSyncOptions();

                _session = RepositorySyncSession.TryCreate(
                    solutionDirectory, this, JoinableTaskFactory, options, _log);
            }
            catch (Exception e)
            {
                _log.Error("Failed to start tab syncing.", e);
            }
        }

        private void StopSession()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_session is null)
            {
                return;
            }

            try
            {
                // Closing a solution produces no branch change, so this is the only chance to
                // persist what was open.
                _session.SaveCurrentSession();
            }
            catch (Exception e)
            {
                _log.Error("Failed to save the tab session while closing.", e);
            }
            finally
            {
                _session.Dispose();
                _session = null;
            }
        }

        public int OnAfterOpenSolution(object pUnkReserved, int fNewSolution)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            StartSessionIfSolutionIsOpen();
            return VSConstants.S_OK;
        }

        public int OnBeforeCloseSolution(object pUnkReserved)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            StopSession();
            return VSConstants.S_OK;
        }

        public int OnAfterOpenProject(IVsHierarchy pHierarchy, int fAdded) => VSConstants.S_OK;

        public int OnQueryCloseProject(IVsHierarchy pHierarchy, int fRemoving, ref int pfCancel) => VSConstants.S_OK;

        public int OnBeforeCloseProject(IVsHierarchy pHierarchy, int fRemoved) => VSConstants.S_OK;

        public int OnAfterLoadProject(IVsHierarchy pStubHierarchy, IVsHierarchy pRealHierarchy) => VSConstants.S_OK;

        public int OnQueryUnloadProject(IVsHierarchy pRealHierarchy, ref int pfCancel) => VSConstants.S_OK;

        public int OnBeforeUnloadProject(IVsHierarchy pRealHierarchy, IVsHierarchy pStubHierarchy) => VSConstants.S_OK;

        public int OnQueryCloseSolution(object pUnkReserved, ref int pfCancel) => VSConstants.S_OK;

        public int OnAfterCloseSolution(object pUnkReserved) => VSConstants.S_OK;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await JoinableTaskFactory.SwitchToMainThreadAsync();

                    StopSession();

                    if (_solution is not null && _solutionEventsCookie != 0)
                    {
                        _solution.UnadviseSolutionEvents(_solutionEventsCookie);
                        _solutionEventsCookie = 0;
                    }
                });
            }

            base.Dispose(disposing);
        }
    }
}
