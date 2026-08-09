using System.Collections.Generic;

namespace GitTabSync.Sync
{
    /// <summary>
    /// The editor, as much of it as tab syncing needs. Implemented by the VSIX over the Visual
    /// Studio shell; substituted in tests.
    /// </summary>
    public interface IEditorTabs
    {
        /// <summary>
        /// The currently open documents in tab order. Documents without a path on disk
        /// (designers, settings pages, unsaved new files) are expected to be omitted.
        /// </summary>
        IReadOnlyList<EditorTab> GetOpenTabs();

        /// <summary>
        /// Index into <see cref="GetOpenTabs"/> of the focused document, or -1.
        /// </summary>
        int GetActiveTabIndex();

        /// <summary>
        /// Makes the open set match <paramref name="tabs"/>: opens what is missing, closes what
        /// is not listed, and leaves documents that appear in both untouched rather than
        /// closing and reopening them.
        /// </summary>
        /// <param name="activeIndex">Index into <paramref name="tabs"/> to focus, or -1.</param>
        /// <remarks>
        /// Implementations touch editor windows and must marshal to the UI thread themselves;
        /// callers invoke this from a background thread.
        /// </remarks>
        void ApplyTabs(IReadOnlyList<EditorTab> tabs, int activeIndex);
    }
}
