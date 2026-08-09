using System;
using System.Globalization;
using GitTabSync.Sync;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace GitTabSync
{
    /// <summary>
    /// Writes the core's activity to a dedicated Output window pane.
    /// </summary>
    /// <remarks>
    /// Tab syncing happens without the user asking for it, so when it does something surprising
    /// the only way to find out why is a log of what it saw and decided.
    /// </remarks>
    internal sealed class OutputWindowLog : ITabSyncLog
    {
        private static readonly Guid PaneId = new Guid("3f7a1c9e-6b24-4d80-9e15-2a8c47b6d013");

        private readonly IVsOutputWindowPane _pane;

        private OutputWindowLog(IVsOutputWindowPane pane)
        {
            _pane = pane;
        }

        // Fully qualified: Microsoft.VisualStudio.Shell also defines a Task type.
        public static async System.Threading.Tasks.Task<ITabSyncLog> CreateAsync(AsyncPackage package)
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            if (await package.GetServiceAsync(typeof(SVsOutputWindow)) is not IVsOutputWindow outputWindow)
            {
                return NullTabSyncLog.Instance;
            }

            var paneId = PaneId;
            outputWindow.CreatePane(ref paneId, "Git Tab Sync", fInitVisible: 0, fClearWithSolution: 0);

            return outputWindow.GetPane(ref paneId, out var pane) == VSConstants.S_OK && pane is not null
                ? new OutputWindowLog(pane)
                : NullTabSyncLog.Instance;
        }

        public void Info(string message) => Write(message);

        public void Error(string message, Exception? exception)
        {
            Write(exception is null ? message : message + " " + exception);
        }

        private void Write(string message)
        {
            // OutputStringThreadSafe is callable from any thread, which matters because the
            // branch monitor reports from a timer thread.
            var line = string.Format(
                CultureInfo.CurrentCulture,
                "[{0:HH:mm:ss}] {1}{2}",
                DateTime.Now,
                message,
                Environment.NewLine);

            try
            {
                // VSTHRD010 flags every IVsOutputWindowPane member as main-thread-only, but
                // OutputStringThreadSafe is the documented exception and is the reason this type
                // can log from the branch monitor's timer thread without marshalling.
#pragma warning disable VSTHRD010
                _pane.OutputStringThreadSafe(line);
#pragma warning restore VSTHRD010
            }
            catch (Exception)
            {
                // Logging must never be the reason a branch switch fails.
            }
        }
    }
}
