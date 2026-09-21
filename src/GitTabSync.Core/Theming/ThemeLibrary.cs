using System;
using System.Collections.Generic;
using System.IO;
using GitTabSync.Settings;
using GitTabSync.Storage;
using GitTabSync.Sync;

namespace GitTabSync.Theming
{
    /// <summary>
    /// The themes available to the settings window, and which one the user picked.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Themes are files in <c>%LOCALAPPDATA%\GitTabSync\themes</c>, one <c>.xaml</c> file each.
    /// Adding a theme means putting a file there: no registration, no manifest, no restart. That
    /// is the point of the folder — the built-ins are written into it as working examples
    /// precisely so that "make my own" is "copy one of these".
    /// </para>
    /// <para>
    /// Beside the sessions and settings rather than in the working tree, for the same reason they
    /// are: a checkout must not be able to change how the IDE looks, and a theme file inside the
    /// repository would show as a pending change on every branch switch.
    /// </para>
    /// <para>
    /// Failures are swallowed and logged, following the storage rule. An unwritable themes folder
    /// costs the user their choice of colours, which is not worth failing a window over.
    /// </para>
    /// </remarks>
    public sealed class ThemeLibrary
    {
        /// <summary>Beside <c>repos</c> under the storage root.</summary>
        public const string DirectoryName = "themes";

        public const string FileExtension = ".xaml";

        private const string ReadMeFileName = "README.txt";

        private readonly ISettingsStore _store;
        private readonly IReadOnlyList<BuiltInTheme> _builtIns;
        private readonly ITabSyncLog _log;
        private readonly string _directory;

        private IReadOnlyList<ThemeInfo> _themes = new ThemeInfo[0];
        private string _selectedId;

        /// <param name="store">Where the choice is persisted.</param>
        /// <param name="builtIns">
        /// The themes to write out when they are missing, in the order they should be offered. The
        /// first is the default.
        /// </param>
        /// <param name="rootDirectory">Storage root. Defaults to the one the stores use.</param>
        public ThemeLibrary(
            ISettingsStore store,
            IReadOnlyList<BuiltInTheme>? builtIns = null,
            string? rootDirectory = null,
            ITabSyncLog? log = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _builtIns = builtIns ?? new BuiltInTheme[0];
            _log = log ?? NullTabSyncLog.Instance;
            _directory = Path.Combine(rootDirectory ?? FileSessionStore.DefaultRootDirectory(), DirectoryName);
            _selectedId = _store.LoadPreferences().ThemeId ?? string.Empty;

            Reload();
        }

        /// <summary>
        /// Raised when the list or the selection changed, on the calling thread — which is the UI
        /// thread for every caller there is.
        /// </summary>
        public event EventHandler? Changed;

        /// <summary>The folder themes are read from, whether or not it exists yet.</summary>
        public string Directory => _directory;

        public IReadOnlyList<ThemeInfo> Themes => _themes;

        /// <summary>The first built-in: used when nothing is chosen, or the choice is gone.</summary>
        public string DefaultThemeId => _builtIns.Count == 0 ? string.Empty : _builtIns[0].Id;

        /// <summary>
        /// The theme to apply: the chosen one, else the default, else whatever is there, else
        /// nothing — in which case the window renders unstyled rather than failing.
        /// </summary>
        public ThemeInfo? Current =>
            Find(_selectedId) ?? Find(DefaultThemeId) ?? (_themes.Count == 0 ? null : _themes[0]);

        /// <summary>
        /// The stored choice, which is not the same thing as <see cref="Current"/>: an id naming a
        /// file that is not there stays stored, so putting the file back restores the choice
        /// rather than having silently reset it.
        /// </summary>
        public string SelectedThemeId
        {
            get => _selectedId;
            set
            {
                var id = value ?? string.Empty;
                if (string.Equals(id, _selectedId, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                _selectedId = id;
                Persist();
                _log.Info("Theme set to " + (id.Length == 0 ? DefaultThemeId : id) + ".");
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Re-reads the folder, writing out any built-in that is missing. This is what the Reload
        /// button calls: a theme dropped into the folder while the window was open appears, and a
        /// file that was edited is read again.
        /// </summary>
        public void Reload()
        {
            Seed();
            _themes = Enumerate();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public ThemeInfo? Find(string? id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            foreach (var theme in _themes)
            {
                // Ordinal-ignore-case: the id is a Windows file name, which is the same rule
                // StorageKey applies to the path half of its keys.
                if (string.Equals(theme.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return theme;
                }
            }

            return null;
        }

        private void Persist()
        {
            var preferences = _store.LoadPreferences();
            preferences.ThemeId = _selectedId;

            try
            {
                _store.SavePreferences(preferences);
            }
            catch (Exception e)
            {
                // The file store swallows the failures it expects; this is for a store that does
                // not, so that picking a theme cannot bring a window down.
                _log.Error("Failed to save the theme choice.", e);
            }
        }

        /// <summary>
        /// Writes out every built-in that is not on disk, and the note explaining the folder.
        /// </summary>
        /// <remarks>
        /// Existing files are never overwritten: a theme the user edited is theirs. The cost of
        /// that rule is that an improvement to a shipped theme does not reach a file somebody has
        /// already changed — deleting the file is how they ask for the new one, and the note in
        /// the folder says so.
        /// </remarks>
        private void Seed()
        {
            try
            {
                System.IO.Directory.CreateDirectory(_directory);

                foreach (var builtIn in _builtIns)
                {
                    var path = PathFor(builtIn.Id);
                    if (!File.Exists(path))
                    {
                        File.WriteAllText(path, builtIn.Content);
                        _log.Info("Wrote the built-in theme " + builtIn.Id + " to " + path + ".");
                    }
                }

                var readMe = Path.Combine(_directory, ReadMeFileName);
                if (!File.Exists(readMe))
                {
                    File.WriteAllText(readMe, ReadMeText());
                }
            }
            catch (Exception e) when (RecoverableStorageFailure.Matches(e))
            {
                _log.Error("Failed to write the themes folder at " + _directory + ".", e);
            }
        }

        /// <summary>
        /// Built-ins first, in the order they were given, then everything else by name. Someone
        /// looking for the theme they just added finds it at the bottom rather than somewhere in
        /// an alphabetical middle.
        /// </summary>
        private IReadOnlyList<ThemeInfo> Enumerate()
        {
            var found = new List<ThemeInfo>();

            try
            {
                if (!System.IO.Directory.Exists(_directory))
                {
                    return found;
                }

                var byId = new Dictionary<string, ThemeInfo>(StringComparer.OrdinalIgnoreCase);
                var rest = new List<ThemeInfo>();

                foreach (var path in System.IO.Directory.GetFiles(_directory, "*" + FileExtension))
                {
                    var id = Path.GetFileNameWithoutExtension(path);
                    if (string.IsNullOrEmpty(id) || byId.ContainsKey(id))
                    {
                        continue;
                    }

                    var theme = new ThemeInfo(
                        id,
                        ThemeFile.ReadDisplayName(path) ?? DisplayNameOfBuiltIn(id) ?? id,
                        path,
                        DisplayNameOfBuiltIn(id) is not null);

                    byId.Add(id, theme);
                    rest.Add(theme);
                }

                foreach (var builtIn in _builtIns)
                {
                    if (byId.TryGetValue(builtIn.Id, out var theme))
                    {
                        found.Add(theme);
                        rest.Remove(theme);
                    }
                }

                rest.Sort((left, right) =>
                    string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase));

                found.AddRange(rest);
            }
            catch (Exception e) when (RecoverableStorageFailure.Matches(e))
            {
                _log.Error("Failed to read the themes folder at " + _directory + ".", e);
            }

            return found;
        }

        private string PathFor(string id) => Path.Combine(_directory, id + FileExtension);

        private string? DisplayNameOfBuiltIn(string id)
        {
            foreach (var builtIn in _builtIns)
            {
                if (string.Equals(builtIn.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return builtIn.DisplayName;
                }
            }

            return null;
        }

        private static string ReadMeText()
        {
            var newLine = Environment.NewLine;

            return "Themes for the Git Tab Sync settings window." + newLine + newLine
                + "Every " + FileExtension + " file in this folder is offered in the window's"
                + " Appearance picker. To make your own, copy one of these files, rename the copy,"
                + " edit it, and press Reload in the window. The file name is the theme's identity;"
                + " the name shown in the picker comes from the " + ThemeFile.DisplayNameKey
                + " entry inside the file." + newLine + newLine
                + "A theme is a WPF ResourceDictionary, read at run time. A key it leaves out goes"
                + " unstyled rather than breaking the window, and a file that does not parse is"
                + " reported in the Git Tab Sync pane of the Output window and skipped." + newLine + newLine
                + "The themes this extension ships are written here whenever they are missing, so"
                + " deleting one restores it and editing one keeps your edit.";
        }
    }
}
