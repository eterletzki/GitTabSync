using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace GitTabSync.Theming
{
    /// <summary>
    /// Backs the settings window's Appearance section: the list of themes, the chosen one, and
    /// where the folder is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In Core with no WPF reference, for the same reason <see cref="Settings.SettingsViewModel"/>
    /// is: the Visual Studio layer has no coverage at all, so anything worth testing has to be on
    /// this side of the seam. The window binds and marshals; it decides nothing.
    /// </para>
    /// <para>
    /// Unlike the settings view model, this one exists whether or not a repository is open. The
    /// choice of theme is the user's and is answerable before any solution has been loaded, so the
    /// Appearance section stays usable on the "no git repository" placeholder.
    /// </para>
    /// <para>
    /// Not thread-safe, and not meant to be: it feeds data bindings, so it belongs to the UI
    /// thread.
    /// </para>
    /// </remarks>
    public sealed class ThemeSelectionViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly ThemeLibrary _library;

        private bool _disposed;

        public ThemeSelectionViewModel(ThemeLibrary library)
        {
            _library = library ?? throw new ArgumentNullException(nameof(library));

            // The library outlives the window — it is one per process, the window comes and goes —
            // so this subscription has to be given back, which is what Dispose is for.
            _library.Changed += OnLibraryChanged;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Raised after the applied theme may have changed, so the shell can re-dress the window.
        /// Also raised by <see cref="Reload"/>, because a theme file edited in place is a new
        /// theme even though the selection did not move.
        /// </summary>
        public event EventHandler? ThemeChanged;

        public IReadOnlyList<ThemeInfo> Themes => _library.Themes;

        /// <summary>
        /// The theme that is actually applied, not the raw stored id: a stored choice whose file
        /// has been deleted shows as the fallback rather than leaving the picker blank, while the
        /// id itself stays stored in case the file comes back.
        /// </summary>
        public ThemeInfo? SelectedTheme
        {
            get => _library.Current;
            set
            {
                if (value is null || ReferenceEquals(value, _library.Current))
                {
                    return;
                }

                // Raises Changed on the library, which comes back through OnLibraryChanged and
                // notifies the bindings — so there is exactly one path that announces a change,
                // whoever caused it.
                _library.SelectedThemeId = value.Id;
            }
        }

        /// <summary>The folder to put a new theme in.</summary>
        public string ThemesDirectory => _library.Directory;

        public string FolderText =>
            "Themes are " + ThemeLibrary.FileExtension + " files in " + _library.Directory
            + ". Copy one to make your own, then reload.";

        /// <summary>
        /// Re-reads the folder. The window offers this as a button rather than watching the
        /// folder: a half-saved theme file is a normal state while somebody is editing one, and
        /// re-dressing the window on every keystroke in their editor would make writing a theme
        /// harder, not easier.
        /// </summary>
        public void Reload() => _library.Reload();

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _library.Changed -= OnLibraryChanged;
        }

        private void OnLibraryChanged(object sender, EventArgs e)
        {
            Raise(nameof(Themes));
            Raise(nameof(SelectedTheme));
            Raise(nameof(FolderText));
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }

        private void Raise(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
