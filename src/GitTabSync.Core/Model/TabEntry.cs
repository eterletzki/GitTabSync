using System.Runtime.Serialization;

namespace GitTabSync.Model
{
    /// <summary>
    /// One document that was open in the editor.
    /// </summary>
    [DataContract(Name = "tab", Namespace = "")]
    public sealed class TabEntry
    {
        [DataMember(Name = "path", Order = 0)]
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// True when <see cref="Path"/> is relative to the repository working directory and uses
        /// '/' separators. Files outside the repository are stored absolute.
        /// </summary>
        /// <remarks>
        /// Storing repository-relative paths keeps saved sessions valid when the repository is
        /// moved or cloned to a different directory.
        /// </remarks>
        [DataMember(Name = "relative", Order = 1)]
        public bool IsRepositoryRelative { get; set; }

        /// <summary>1-based caret line, or 0 when unknown.</summary>
        [DataMember(Name = "line", Order = 2)]
        public int CaretLine { get; set; }

        /// <summary>1-based caret column, or 0 when unknown.</summary>
        [DataMember(Name = "column", Order = 3)]
        public int CaretColumn { get; set; }

        /// <summary>True when the tab was pinned.</summary>
        /// <remarks>
        /// Added after the first release without bumping
        /// <see cref="TabSession.CurrentSchemaVersion"/>: an appended optional member is compatible
        /// both ways. Sessions written before it deserialise as unpinned, and an older build
        /// ignores the member rather than failing to read the file.
        /// </remarks>
        [DataMember(Name = "pinned", Order = 4)]
        public bool IsPinned { get; set; }
    }
}
