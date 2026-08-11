using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using GitTabSync.Sync;

namespace GitTabSync.Tests.TestSupport
{
    /// <summary>
    /// An editor that behaves the way Visual Studio behaves, including the part that matters most:
    /// <em>not everything it does is reported</em>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="FakeEditorTabs"/> is a passive store — whatever a test puts in is what comes out.
    /// That makes it useless for the failure that actually shipped, where the editor's state and
    /// the extension's idea of it drifted apart because nothing raised an event. There is no way
    /// to express that in a double with no notion of a notification.
    /// </para>
    /// <para>
    /// So every mutation here is named for whether the host reports it. Opening, closing and
    /// activating a document raise <see cref="Reported"/>, standing in for the running document
    /// table. Pinning and moving the caret change the editor and raise nothing, because that is
    /// what Visual Studio does. Tests wire <see cref="Reported"/> to a real
    /// <see cref="CaptureScheduler"/> and are then only able to pass if the production wiring is
    /// right.
    /// </para>
    /// </remarks>
    public sealed class FakeHostEditor : IEditorTabs
    {
        private readonly object _gate = new object();
        private readonly List<EditorTab> _open = new List<EditorTab>();
        private readonly ManualResetEventSlim _read = new ManualResetEventSlim(false);

        private int _activeIndex = -1;
        private int _reads;

        /// <summary>The subset of what happens here that Visual Studio would tell us about.</summary>
        public event Action? Reported;

        /// <summary>How many times the editor has been asked what it has open.</summary>
        public int Reads => Volatile.Read(ref _reads);

        public IReadOnlyList<EditorTab> Open
        {
            get { lock (_gate) { return _open.ToArray(); } }
        }

        // ---- things the host reports ----

        public void OpenAndReport(string path, int caretLine = 0, int caretColumn = 0)
        {
            OpenSilently(path, caretLine, caretColumn);
            Reported?.Invoke();
        }

        public void CloseAndReport(string path)
        {
            CloseSilently(path);
            Reported?.Invoke();
        }

        public void ActivateAndReport(string path)
        {
            lock (_gate)
            {
                _activeIndex = IndexOf(path);
            }

            Reported?.Invoke();
        }

        // ---- things the host does not report ----

        /// <summary>Pinning changes a window property, not a document. Nothing fires.</summary>
        public void PinSilently(string path) => SetPinned(path, true);

        public void UnpinSilently(string path) => SetPinned(path, false);

        /// <summary>Moving the caret raises no document event either.</summary>
        public void MoveCaretSilently(string path, int line, int column)
        {
            lock (_gate)
            {
                var index = IndexOf(path);
                if (index < 0)
                {
                    return;
                }

                _open[index] = new EditorTab(path, line, column, _open[index].IsPinned);
            }
        }

        /// <summary>
        /// A document appearing without a report — Visual Studio opening something of its own
        /// accord, or an event we are not subscribed to.
        /// </summary>
        public void OpenSilently(string path, int caretLine = 0, int caretColumn = 0)
        {
            lock (_gate)
            {
                if (IndexOf(path) >= 0)
                {
                    return;
                }

                _open.Add(new EditorTab(path, caretLine, caretColumn));
            }
        }

        /// <summary>
        /// A tab closed by the checkout, before anyone told us the branch had moved. This is the
        /// case the whole snapshot design exists for.
        /// </summary>
        public void CloseSilently(string path)
        {
            lock (_gate)
            {
                var index = IndexOf(path);
                if (index < 0)
                {
                    return;
                }

                _open.RemoveAt(index);
                if (_activeIndex >= _open.Count)
                {
                    _activeIndex = _open.Count - 1;
                }
            }
        }

        // ---- IEditorTabs ----

        public IReadOnlyList<EditorTab> GetOpenTabs()
        {
            lock (_gate)
            {
                Interlocked.Increment(ref _reads);
                _read.Set();
                return _open.ToArray();
            }
        }

        public int GetActiveTabIndex()
        {
            lock (_gate)
            {
                return _activeIndex;
            }
        }

        public void ApplyTabs(IReadOnlyList<EditorTab> tabs, int activeIndex)
        {
            lock (_gate)
            {
                _open.Clear();
                _open.AddRange(tabs);
                _activeIndex = activeIndex;
            }

            // Restoring opens and closes documents, so the host reports it — which is exactly how
            // a half-applied state could get written over the snapshot.
            Reported?.Invoke();
        }

        /// <summary>Waits until the editor has been read at least <paramref name="count"/> times.</summary>
        public bool WaitForReads(int count, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (Reads < count)
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    return false;
                }

                _read.Reset();
                if (Reads >= count)
                {
                    return true;
                }

                _read.Wait(remaining);
            }

            return true;
        }

        private void SetPinned(string path, bool pinned)
        {
            lock (_gate)
            {
                var index = IndexOf(path);
                if (index < 0)
                {
                    return;
                }

                var tab = _open[index];
                _open[index] = new EditorTab(tab.AbsolutePath, tab.CaretLine, tab.CaretColumn, pinned);
            }
        }

        private int IndexOf(string path)
        {
            for (var i = 0; i < _open.Count; i++)
            {
                if (string.Equals(_open[i].AbsolutePath, path, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
