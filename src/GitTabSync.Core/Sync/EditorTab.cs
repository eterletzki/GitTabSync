namespace GitTabSync.Sync
{
    /// <summary>
    /// A document open in the editor right now, with an absolute path.
    /// </summary>
    /// <remarks>
    /// The runtime counterpart of <see cref="Model.TabEntry"/>, which is the persisted form.
    /// They are kept separate so the on-disk schema can change without touching editor code.
    /// </remarks>
    public sealed class EditorTab
    {
        public EditorTab(string absolutePath, int caretLine = 0, int caretColumn = 0, bool isPinned = false)
        {
            AbsolutePath = absolutePath;
            CaretLine = caretLine;
            CaretColumn = caretColumn;
            IsPinned = isPinned;
        }

        public string AbsolutePath { get; }

        /// <summary>1-based caret line, or 0 when unknown.</summary>
        public int CaretLine { get; }

        /// <summary>1-based caret column, or 0 when unknown.</summary>
        public int CaretColumn { get; }

        /// <summary>
        /// True when the document's tab is pinned. Unknown pin state is reported as false: a tab
        /// wrongly pinned on restore is a visible, sticky change to a layout nobody asked for.
        /// </summary>
        public bool IsPinned { get; }

        public override string ToString() => AbsolutePath;
    }
}
