namespace GitTabSync.Theming
{
    /// <summary>
    /// One theme found on disk: a file of styles the settings window can be dressed in.
    /// </summary>
    /// <remarks>
    /// Core deliberately knows nothing about what is <em>in</em> the file. It is WPF markup, which
    /// only the VSIX can parse, so the rule here is the same one that keeps sync decisions out of
    /// the shell: this side owns discovery, naming and the user's choice — all of which are
    /// testable without Visual Studio — and hands the VSIX a path.
    /// </remarks>
    public sealed class ThemeInfo
    {
        internal ThemeInfo(string id, string displayName, string filePath, bool isBuiltIn)
        {
            Id = id;
            DisplayName = displayName;
            FilePath = filePath;
            IsBuiltIn = isBuiltIn;
        }

        /// <summary>
        /// The file name without its extension. This is what is persisted, so renaming a theme
        /// file is renaming the theme: the stored choice stops matching and falls back to the
        /// default.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// The label shown in the picker — from the theme file when it names itself, otherwise
        /// <see cref="Id"/>.
        /// </summary>
        public string DisplayName { get; }

        public string FilePath { get; }

        /// <summary>
        /// True for a theme the extension ships and writes out. It is an ordinary file like any
        /// other and can be edited or deleted; the only difference is that a missing one is
        /// written again the next time the library is loaded.
        /// </summary>
        public bool IsBuiltIn { get; }

        /// <summary>So a picker with no <c>DisplayMemberPath</c> still reads correctly.</summary>
        public override string ToString() => DisplayName;
    }
}
