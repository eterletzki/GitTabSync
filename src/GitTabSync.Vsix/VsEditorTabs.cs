using System;
using System.Collections.Generic;
using System.IO;
using GitTabSync.Sync;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Threading;

namespace GitTabSync
{
    /// <summary>
    /// Implements <see cref="IEditorTabs"/> against the Visual Studio shell.
    /// </summary>
    /// <remarks>
    /// Every method here marshals to the UI thread itself, because the core calls them from the
    /// branch monitor's timer thread.
    /// </remarks>
    internal sealed class VsEditorTabs : IEditorTabs
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly JoinableTaskFactory _joinableTaskFactory;
        private readonly ITabSyncLog _log;

        public VsEditorTabs(IServiceProvider serviceProvider, JoinableTaskFactory joinableTaskFactory, ITabSyncLog log)
        {
            _serviceProvider = serviceProvider;
            _joinableTaskFactory = joinableTaskFactory;
            _log = log;
        }

        public IReadOnlyList<EditorTab> GetOpenTabs()
        {
            return _joinableTaskFactory.Run(async () =>
            {
                await _joinableTaskFactory.SwitchToMainThreadAsync();
                return GetOpenTabsOnUiThread();
            });
        }

        public int GetActiveTabIndex()
        {
            return _joinableTaskFactory.Run(async () =>
            {
                await _joinableTaskFactory.SwitchToMainThreadAsync();

                var active = GetActiveDocumentPathOnUiThread();
                if (active is null)
                {
                    return -1;
                }

                var tabs = GetOpenTabsOnUiThread();
                for (var i = 0; i < tabs.Count; i++)
                {
                    if (string.Equals(tabs[i].AbsolutePath, active, StringComparison.OrdinalIgnoreCase))
                    {
                        return i;
                    }
                }

                return -1;
            });
        }

        public void ApplyTabs(IReadOnlyList<EditorTab> tabs, int activeIndex)
        {
            _joinableTaskFactory.Run(async () =>
            {
                await _joinableTaskFactory.SwitchToMainThreadAsync();
                ApplyTabsOnUiThread(tabs, activeIndex);
            });
        }

        private List<EditorTab> GetOpenTabsOnUiThread()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var result = new List<EditorTab>();

            foreach (var frame in EnumerateDocumentFrames())
            {
                var path = GetDocumentPath(frame);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                // Skips designers, option pages and unsaved new files: without a file on disk
                // there is nothing that could be reopened on another branch.
                if (!File.Exists(path))
                {
                    continue;
                }

                GetCaretPosition(frame, out var line, out var column);
                result.Add(new EditorTab(path!, line, column));
            }

            return result;
        }

        private void ApplyTabsOnUiThread(IReadOnlyList<EditorTab> tabs, int activeIndex)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tab in tabs)
            {
                wanted.Add(tab.AbsolutePath);
            }

            CloseUnwantedDocuments(wanted);

            IVsWindowFrame? frameToActivate = null;

            for (var i = 0; i < tabs.Count; i++)
            {
                var frame = OpenDocument(tabs[i]);
                if (i == activeIndex)
                {
                    frameToActivate = frame;
                }
            }

            frameToActivate?.Show();
        }

        private void CloseUnwantedDocuments(ICollection<string> wanted)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            foreach (var frame in EnumerateDocumentFrames())
            {
                var path = GetDocumentPath(frame);
                if (string.IsNullOrEmpty(path) || wanted.Contains(path!))
                {
                    // Documents open on both branches are left alone rather than closed and
                    // reopened, which would lose undo history and scroll position.
                    continue;
                }

                // Unsaved work is never closed. A branch switch is not a reason to discard edits
                // or to throw a save prompt at someone who did not ask for one.
                if (IsDirty(frame))
                {
                    _log.Info("Left " + path + " open because it has unsaved changes.");
                    continue;
                }

                try
                {
                    frame.CloseFrame((uint)__FRAMECLOSE.FRAMECLOSE_NoSave);
                }
                catch (Exception e)
                {
                    _log.Error("Failed to close " + path + ".", e);
                }
            }
        }

        private IVsWindowFrame? OpenDocument(EditorTab tab)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                VsShellUtilities.OpenDocument(
                    _serviceProvider,
                    tab.AbsolutePath,
                    VSConstants.LOGVIEWID_Primary,
                    out _,
                    out _,
                    out var frame);

                if (frame is null)
                {
                    return null;
                }

                frame.Show();
                RestoreCaretPosition(frame, tab);
                return frame;
            }
            catch (Exception e)
            {
                _log.Error("Failed to open " + tab.AbsolutePath + ".", e);
                return null;
            }
        }

        private void RestoreCaretPosition(IVsWindowFrame frame, EditorTab tab)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (tab.CaretLine <= 0)
            {
                return;
            }

            try
            {
                var textView = VsShellUtilities.GetTextView(frame);
                if (textView is null)
                {
                    return;
                }

                // Stored 1-based, the shell is 0-based. The file may be shorter on this branch,
                // so a failed SetCaretPos is expected and ignored.
                var line = tab.CaretLine - 1;
                var column = Math.Max(0, tab.CaretColumn - 1);

                if (textView.SetCaretPos(line, column) == VSConstants.S_OK)
                {
                    textView.CenterLines(line, 1);
                }
            }
            catch (Exception)
            {
                // Caret restoration is a nicety; never fail a restore over it.
            }
        }

        private IEnumerable<IVsWindowFrame> EnumerateDocumentFrames()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_serviceProvider.GetService(typeof(SVsUIShell)) is not IVsUIShell shell)
            {
                yield break;
            }

            if (shell.GetDocumentWindowEnum(out var frames) != VSConstants.S_OK || frames is null)
            {
                yield break;
            }

            var buffer = new IVsWindowFrame[1];
            while (frames.Next(1, buffer, out var fetched) == VSConstants.S_OK && fetched == 1)
            {
                if (buffer[0] is not null)
                {
                    yield return buffer[0];
                }
            }
        }

        private static string? GetDocumentPath(IVsWindowFrame frame)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                return frame.GetProperty((int)__VSFPROPID.VSFPROPID_pszMkDocument, out var value) == VSConstants.S_OK
                    ? value as string
                    : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool IsDirty(IVsWindowFrame frame)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                if (frame.GetProperty((int)__VSFPROPID.VSFPROPID_DocData, out var docData) != VSConstants.S_OK)
                {
                    // Unknown state: assume dirty, because the cost of being wrong the other way
                    // is discarding someone's edits.
                    return true;
                }

                if (docData is IVsPersistDocData persist && persist.IsDocDataDirty(out var dirty) == VSConstants.S_OK)
                {
                    return dirty != 0;
                }

                return true;
            }
            catch (Exception)
            {
                return true;
            }
        }

        private static void GetCaretPosition(IVsWindowFrame frame, out int line, out int column)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            line = 0;
            column = 0;

            try
            {
                if (frame.GetProperty((int)__VSFPROPID.VSFPROPID_DocView, out var docView) != VSConstants.S_OK)
                {
                    return;
                }

                if (docView is not IVsCodeWindow codeWindow)
                {
                    return;
                }

                if (codeWindow.GetPrimaryView(out var textView) != VSConstants.S_OK || textView is null)
                {
                    return;
                }

                if (textView.GetCaretPos(out var zeroBasedLine, out var zeroBasedColumn) == VSConstants.S_OK)
                {
                    line = zeroBasedLine + 1;
                    column = zeroBasedColumn + 1;
                }
            }
            catch (Exception)
            {
                // A non-text document; leave the position unknown.
            }
        }

        private string? GetActiveDocumentPathOnUiThread()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                if (_serviceProvider.GetService(typeof(SVsUIShell)) is not IVsUIShell shell)
                {
                    return null;
                }

                if (shell.GetDocumentWindowEnum(out var frames) != VSConstants.S_OK || frames is null)
                {
                    return null;
                }

                // The shell enumerates document frames with the active one first.
                var buffer = new IVsWindowFrame[1];
                while (frames.Next(1, buffer, out var fetched) == VSConstants.S_OK && fetched == 1)
                {
                    var frame = buffer[0];
                    if (frame is null)
                    {
                        continue;
                    }

                    if (frame.IsOnScreen(out var onScreen) == VSConstants.S_OK && onScreen != 0)
                    {
                        return GetDocumentPath(frame);
                    }
                }

                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
