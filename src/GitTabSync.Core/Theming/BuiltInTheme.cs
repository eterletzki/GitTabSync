using System;

namespace GitTabSync.Theming
{
    /// <summary>
    /// A theme the extension ships, as the text that is written to the themes folder when no file
    /// of that name is there.
    /// </summary>
    /// <remarks>
    /// The content is supplied by the caller rather than held here because it is WPF markup, and
    /// Core has no WPF. The seam is the same shape as <c>IEditorTabs</c>: the VSIX supplies what
    /// only it can, Core does everything that can be tested without Visual Studio.
    /// <para>
    /// Built-ins are written to disk instead of being loaded from inside the assembly so that
    /// there is exactly one way a theme is loaded. A user editing "the theme that ships with it"
    /// and a user writing their own are then the same case, and the one that is exercised on every
    /// run is the one users' themes go through.
    /// </para>
    /// </remarks>
    public sealed class BuiltInTheme
    {
        public BuiltInTheme(string id, string displayName, string content)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A theme needs an id.", nameof(id));
            }

            if (string.IsNullOrEmpty(content))
            {
                throw new ArgumentException("A theme needs content.", nameof(content));
            }

            Id = id;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? id : displayName;
            Content = content;
        }

        /// <inheritdoc cref="ThemeInfo.Id"/>
        public string Id { get; }

        /// <summary>Used only when the written file does not name itself.</summary>
        public string DisplayName { get; }

        /// <summary>The file's text.</summary>
        public string Content { get; }
    }
}
