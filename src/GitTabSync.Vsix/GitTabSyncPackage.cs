using System;
using System.ComponentModel.Design;
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
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideToolWindow(typeof(GitTabSyncToolWindow))]
    public sealed class GitTabSyncPackage : AsyncPackage, IVsSolutionEvents
    {
        public const string PackageGuidString = "b7f0e2a4-3c15-4d8e-9a6b-2f5c8d1e7a30";

        public GitTabSyncPackage()
        {
            // Assigned here rather than in InitializeAsync because a docked tool window is
            // restored at startup and can be constructed while initialisation is still running.
            // There is exactly one package instance per Visual Studio process.
            Instance = this;
        }

        /// <summary>The loaded package, for the tool window to reach the running session.</summary>
        internal static GitTabSyncPackage? Instance { get; private set; }

        private ITabSyncLog _log = NullTabSyncLog.Instance;
        private IVsSolution? _solution;
        private RepositorySyncSession? _session;
        private uint _solutionEventsCookie;

        /// <summary>
        /// Raised when the session is created or torn down, so an open settings window can follow
        /// the solution rather than showing a repository that is no longer open. UI thread.
        /// </summary>
        public event EventHandler? SessionChanged;

        /// <summary>The session for the open solution, or <c>null</c> when there is none.</summary>
        internal RepositorySyncSession? CurrentSession => _session;

        protected override async Task InitializeAsync(
            CancellationToken cancellationToken,
            IProgress<ServiceProgressData> progress)
        {
            await base.InitializeAsync(cancellationToken, progress);

            _log = await OutputWindowLog.CreateAsync(this);

            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            _solution = await GetServiceAsync(typeof(SVsSolution)) as IVsSolution;
            _solution?.AdviseSolutionEvents(this, out _solutionEventsCookie);

            // The interface, not OleMenuCommandService: the concrete class inherits from
            // MenuCommandService in System.Design, and referencing a WinForms designer assembly to
            // add one menu item is not a trade worth making.
            if (await GetServiceAsync(typeof(IMenuCommandService)) is IMenuCommandService commands)
            {
                commands.AddCommand(new MenuCommand(
                    ShowSettingsWindow,
                    new CommandID(GitTabSyncToolWindow.CommandSet, GitTabSyncToolWindow.CommandId)));
            }
            else
            {
                _log.Info("The command service is unavailable; the settings window cannot be opened from the menu.");
            }

            // The package autoloads on SolutionExists, so by the time it initialises the
            // solution is usually already open and OnAfterOpenSolution has been and gone.
            StartSessionIfSolutionIsOpen();
        }

        private void ShowSettingsWindow(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var window = FindToolWindow(typeof(GitTabSyncToolWindow), 0, create: true);
                if (window?.Frame is not IVsWindowFrame frame)
                {
                    _log.Info("The settings window could not be created.");
                    return;
                }

                ErrorHandler.ThrowOnFailure(frame.Show());
            }
            catch (Exception exception)
            {
                _log.Error("Failed to open the settings window.", exception);
            }
        }

        private void StartSessionIfSolutionIsOpen()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_session is not null || _solution is null)
            {
                return;
            }

            if (_solution.GetSolutionInfo(out var solutionDirectory, out var solutionFile, out _) != VSConstants.S_OK)
            {
                return;
            }

            if (string.IsNullOrEmpty(solutionDirectory))
            {
                return;
            }

            try
            {
                _session = RepositorySyncSession.TryCreate(
                    solutionDirectory, solutionFile, this, JoinableTaskFactory, _log);
            }
            catch (Exception e)
            {
                _log.Error("Failed to start tab syncing.", e);
            }

            SessionChanged?.Invoke(this, EventArgs.Empty);
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
                SessionChanged?.Invoke(this, EventArgs.Empty);
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
