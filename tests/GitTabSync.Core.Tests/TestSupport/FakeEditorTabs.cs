using System;
using System.Collections.Generic;
using System.Linq;
using GitTabSync.Sync;

namespace GitTabSync.Tests.TestSupport
{
    public sealed class FakeEditorTabs : IEditorTabs
    {
        private List<EditorTab> _open = new List<EditorTab>();

        public int ActiveIndex { get; set; } = -1;

        public int ApplyCallCount { get; private set; }

        /// <summary>Runs inside <see cref="ApplyTabs"/>, to simulate the host raising document
        /// events while a restore is in progress.</summary>
        public Action? DuringApply { get; set; }

        /// <summary>
        /// When set, <see cref="GetOpenTabs"/> throws it. Visual Studio's shell interfaces fail
        /// for reasons the extension cannot control — a window closing underneath the enumeration,
        /// a COM call refused during shutdown — and the whole design says a branch switch must
        /// survive that.
        /// </summary>
        public Exception? FailOnGetOpenTabs { get; set; }

        /// <summary>When set, <see cref="ApplyTabs"/> throws it.</summary>
        public Exception? FailOnApplyTabs { get; set; }

        public IReadOnlyList<EditorTab> Open => _open;

        public void SetOpen(params string[] paths)
        {
            _open = paths.Select(p => new EditorTab(p)).ToList();
        }

        public void SetOpen(params EditorTab[] tabs)
        {
            _open = tabs.ToList();
        }

        public IReadOnlyList<EditorTab> GetOpenTabs() =>
            FailOnGetOpenTabs is null ? _open.ToList() : throw FailOnGetOpenTabs;

        public int GetActiveTabIndex() => ActiveIndex;

        public void ApplyTabs(IReadOnlyList<EditorTab> tabs, int activeIndex)
        {
            ApplyCallCount++;
            if (FailOnApplyTabs is not null)
            {
                throw FailOnApplyTabs;
            }

            _open = tabs.ToList();
            ActiveIndex = activeIndex;
            DuringApply?.Invoke();
        }
    }
}
