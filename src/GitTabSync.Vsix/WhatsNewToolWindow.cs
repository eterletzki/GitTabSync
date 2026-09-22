using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace GitTabSync
{
    /// <summary>
    /// The dockable "Git Tab Sync: What's New" window. Shell only — everything it shows comes from
    /// <see cref="Release.LandingPageViewModel"/> in Core.
    /// </summary>
    /// <remarks>
    /// A tool window rather than a dialog, deliberately. This page opens without being asked for,
    /// and a modal dialog that appears while Visual Studio is still starting is in the way of
    /// whatever the user actually opened the IDE to do. A dockable window can be moved, tabbed
    /// behind something else, or closed with one click, and closing it is the end of it.
    /// </remarks>
    [Guid(WindowGuidString)]
    public sealed class WhatsNewToolWindow : ToolWindowPane
    {
        public const string WindowGuidString = "c4a6e8b2-7d13-4f59-9e02-3b8a5c6d1f47";

        /// <summary>Matches <c>cmdidWhatsNewWindow</c> in GitTabSyncPackage.vsct.</summary>
        public const int CommandId = 0x0101;

        private readonly WhatsNewWindowControl _control;

        public WhatsNewToolWindow()
            : base(null)
        {
            Caption = "Git Tab Sync: What's New";
            _control = new WhatsNewWindowControl();
            Content = _control;
        }

        protected override void Initialize()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            base.Initialize();

            // Through the static instance rather than this pane's Package, for the reason
            // GitTabSyncToolWindow does it: a docked window is restored at startup and can be
            // constructed while the package is still initialising.
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
