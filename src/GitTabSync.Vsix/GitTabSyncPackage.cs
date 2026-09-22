using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using GitTabSync.Release;
using GitTabSync.Settings;
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
    [ProvideToolWindow(typeof(WhatsNewToolWindow))]
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
        /// The page an upgrade or a first install queued, waiting for the window to be built.
        /// Taken once: after that, opening the window from the menu shows the whole changelog
        /// instead of the same announcement a second time.
        /// </summary>
        private LandingPageDecision? _pendingLandingPage;

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

            // The theme host may already have loaded a theme for a window restored during startup,
            // so this connects its log rather than supplying one: from here on, a broken theme file
            // is reported in the pane like everything else.
            ThemeHost.Instance.Log = _log;

            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            _solution = await GetServiceAsync(typeof(SVsSolution)) as IVsSolution;
            _solution?.AdviseSolutionEvents(this, out _solutionEventsCookie);

            // The interface, not OleMenuCommandService: the concrete class inherits from
            // MenuCommandService in System.Design, and referencing a WinForms designer assembly to
            // add one menu item is not a trade worth making.
            if (await GetServiceAsync(typeof(IMenuCommandService)) is IMenuCommandService commands)
            {
                commands.AddCommand(new MenuCommand(
                    OnShowSettingsWindow,
                    new CommandID(GitTabSyncToolWindow.CommandSet, GitTabSyncToolWindow.CommandId)));

                commands.AddCommand(new MenuCommand(
                    OnShowWhatsNewWindow,
                    new CommandID(GitTabSyncToolWindow.CommandSet, WhatsNewToolWindow.CommandId)));
            }
            else
            {
                _log.Info("The command service is unavailable; the windows cannot be opened from the menu.");
            }

            // The package autoloads on SolutionExists, so by the time it initialises the
            // solution is usually already open and OnAfterOpenSolution has been and gone.
            StartSessionIfSolutionIsOpen();

            // Last, and swallowing everything. A page announcing what changed is the least
            // important thing this package does, and it runs after the parts that matter are
            // already wired up — so however it fails, tab syncing is already working.
            ScheduleWhatsNewPage();
        }

        /// <summary>
        /// Decides whether the What's New page is due and, if it is, schedules it to be shown once
        /// the shell has finished starting up.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The <em>decision</em> is made here and now: it is a file read and a few comparisons, it
        /// cannot block, and taking it synchronously means the record of having shown the page is
        /// written before anything downstream can go wrong. That ordering is load-bearing — see
        /// the end of this comment.
        /// </para>
        /// <para>
        /// <strong>Showing the window is deferred, and that is not a preference.</strong> Creating
        /// a tool window from inside <c>InitializeAsync</c> deadlocks Visual Studio: the shell will
        /// not hand out a tool window until the package is initialised, and the package cannot
        /// finish initialising while it is blocked waiting for one. This is not theoretical. The
        /// first version of this method called <c>FindToolWindow(create: true)</c> inline and hung
        /// the IDE on the first solution opened after an upgrade — no dialog, no crash dump, and
        /// no log, because a hang produces none of the three. If you are tempted to move this back
        /// inline because it looks simpler, that is the bug you are re-introducing.
        /// </para>
        /// <para>
        /// So the show waits on <see cref="KnownUIContexts.ShellInitializedContext"/> — the shell
        /// saying it has finished starting — and runs fire-and-forget. The worst a failure can now
        /// do is leave the page unshown, which is the same cost the gate already accepts elsewhere.
        /// </para>
        /// <para>
        /// Because the version is recorded <em>before</em> the window is asked for, a failure to
        /// show costs the page once rather than every startup. That is what kept the deadlock from
        /// being a boot loop: the record was already on disk, so the next start decided there was
        /// nothing to show and came up clean.
        /// </para>
        /// </remarks>
        private void ScheduleWhatsNewPage()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            LandingPageDecision decision;

            try
            {
                decision = new LandingPageGate(new FileSettingsStore(), log: _log).OnStartup();
            }
            catch (Exception e)
            {
                // Deliberately broad. An escaped exception on the initialisation path would take
                // down the autoload, and with it the feature the extension exists for, in exchange
                // for a page nobody asked to see.
                _log.Error("Failed to decide whether to show the What's New page.", e);
                return;
            }

            if (!decision.ShouldShow)
            {
                return;
            }

            // Read back by the window as it is constructed, once the shell lets us build one.
            _pendingLandingPage = decision;

            // VSSDK007 wants this awaited or joined. There is nothing to wait for, and waiting is
            // precisely what must not happen here: initialisation has to return so that the shell
            // can finish starting and the continuation below can run at all.
#pragma warning disable VSSDK007
            JoinableTaskFactory
                .RunAsync(async () =>
                {
                    try
                    {
                        // Unconditionally leave the initialisation call stack first. The wait
                        // below completes synchronously when the shell is already up, and a
                        // synchronous completion would put the tool window creation straight back
                        // on the stack this method exists to get off.
                        await Task.Yield();

                        await WhenShellInitializedAsync();

                        await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);

                        await ShowToolWindowAsync(
                            typeof(WhatsNewToolWindow), id: 0, create: true, cancellationToken: DisposalToken);
                    }
                    catch (OperationCanceledException)
                    {
                        // Visual Studio is shutting down, or the package is being disposed, before
                        // the shell finished starting. Nothing to report.
                    }
                    catch (Exception e)
                    {
                        _log.Error("Failed to show the What's New page.", e);
                    }
                })
                .FileAndForget("GitTabSync/WhatsNew/Show");
#pragma warning restore VSSDK007
        }

        /// <summary>
        /// The page to show in a window being built now: the one an upgrade queued, or — when
        /// nothing is queued, which is every open from the menu — the whole changelog.
        /// </summary>
        internal Release.LandingPageViewModel CreateLandingPageModel()
        {
            var pending = _pendingLandingPage;
            _pendingLandingPage = null;

            if (pending is not null)
            {
                return new Release.LandingPageViewModel(pending);
            }

            // Opening it by hand marks the running version as seen, since they are looking at it.
            var gate = new LandingPageGate(new FileSettingsStore(), log: _log);
            return new Release.LandingPageViewModel(gate.OnRequest());
        }

        /// <summary>
        /// Completes once the shell reports that it has finished starting up.
        /// </summary>
        /// <remarks>
        /// Written out rather than using a <c>WhenActivated</c> helper because this SDK's
        /// <see cref="UIContext"/> has none. Callers must not assume this yields: it completes
        /// synchronously when the shell is already up, which is the common case on an upgrade,
        /// since the package autoloads on a solution being opened rather than at startup.
        /// </remarks>
        private async Task WhenShellInitializedAsync()
        {
            // Switching rather than asserting: VSTHRD109 rejects a Task-returning method that
            // throws when it is called off the main thread, and UIContext is main-thread only.
            await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);

            var context = KnownUIContexts.ShellInitializedContext;
            if (context.IsActive)
            {
                return;
            }

            var completion = new System.Threading.Tasks.TaskCompletionSource<bool>();

            void OnChanged(object sender, UIContextChangedEventArgs e)
            {
                if (!e.Activated)
                {
                    return;
                }

                context.UIContextChanged -= OnChanged;
                completion.TrySetResult(true);
            }

            context.UIContextChanged += OnChanged;

            // Between the check above and the subscription the context may have gone active, in
            // which case no event is coming and the wait would never end.
            if (context.IsActive)
            {
                context.UIContextChanged -= OnChanged;
                completion.TrySetResult(true);
            }

            await completion.Task;
        }

        /// <summary>Opens the settings window. Also the What's New page's button.</summary>
        internal void ShowSettingsWindow()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ShowWindow(typeof(GitTabSyncToolWindow), "settings window");
        }

        private void OnShowSettingsWindow(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ShowWindow(typeof(GitTabSyncToolWindow), "settings window");
        }

        private void OnShowWhatsNewWindow(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ShowWindow(typeof(WhatsNewToolWindow), "What's New page");
        }

        /// <param name="description">Names the window in the log, which is the only place a
        /// failure to open one is reported.</param>
        private void ShowWindow(Type windowType, string description)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var window = FindToolWindow(windowType, 0, create: true);
                if (window?.Frame is not IVsWindowFrame frame)
                {
                    _log.Info("The " + description + " could not be created.");
                    return;
                }

                ErrorHandler.ThrowOnFailure(frame.Show());
            }
            catch (Exception exception)
            {
                _log.Error("Failed to open the " + description + ".", exception);
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
