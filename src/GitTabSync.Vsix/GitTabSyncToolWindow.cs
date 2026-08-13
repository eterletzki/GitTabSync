using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace GitTabSync
{
    /// <summary>
    /// The dockable "Git Tab Sync" window. Shell only — everything it shows comes from
    /// <see cref="Settings.SettingsViewModel"/> in Core.
    /// </summary>
    [Guid(WindowGuidString)]
    public sealed class GitTabSyncToolWindow : ToolWindowPane
    {
        public const string WindowGuidString = "3d400224-0ef4-4881-acfb-d92f64fd5e29";

        /// <summary>Matches <c>guidGitTabSyncCmdSet</c> in GitTabSyncPackage.vsct.</summary>
        public static readonly Guid CommandSet = new Guid("66eb4939-3cb0-4a97-a8c6-967023cd2427");

        /// <summary>Matches <c>cmdidSettingsWindow</c> in GitTabSyncPackage.vsct.</summary>
        public const int CommandId = 0x0100;

        private readonly SettingsWindowControl _control;

        public GitTabSyncToolWindow()
            : base(null)
        {
            Caption = "Git Tab Sync";
            _control = new SettingsWindowControl();
            Content = _control;
        }

        protected override void Initialize()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            base.Initialize();

            // The package is reached through its static instance rather than through this pane,
            // because a docked window is restored at startup and can be constructed while the
            // package is still initialising. The instance is assigned in the constructor, which
            // has certainly run by the time the shell asks for one of its tool windows.
            _control.Attach(GitTabSyncPackage.Instance);
        }

        protected override void Dispose(bool disposing)
        {
            // Unconditional: the shell disposes a window pane on the UI thread either way, and
            // VSTHRD108 rejects an affinity check that only some paths make.
            ThreadHelper.ThrowIfNotOnUIThread();

            if (disposing)
            {
                _control.Detach();
            }

            base.Dispose(disposing);
        }
    }
}
