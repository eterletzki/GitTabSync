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

        public IReadOnlyList<EditorTab> Open => _open;

        public void SetOpen(params string[] paths)
        {
            _open = paths.Select(p => new EditorTab(p)).ToList();
        }

        public IReadOnlyList<EditorTab> GetOpenTabs() => _open.ToList();

        public int GetActiveTabIndex() => ActiveIndex;

        public void ApplyTabs(IReadOnlyList<EditorTab> tabs, int activeIndex)
        {
            ApplyCallCount++;
            _open = tabs.ToList();
            ActiveIndex = activeIndex;
            DuringApply?.Invoke();
        }
    }
}
