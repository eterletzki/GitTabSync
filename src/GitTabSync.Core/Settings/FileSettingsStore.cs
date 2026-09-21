using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using GitTabSync.Storage;

namespace GitTabSync.Settings
{
    /// <summary>
    /// Stores settings as JSON under the user's local application data, beside the sessions.
    /// </summary>
    /// <remarks>
    /// Outside the working tree for the same reason sessions are: a settings file inside the
    /// repository would be rewritten by the very checkout whose behaviour it configures, and would
    /// show as a pending change on every branch switch. The consequence is deliberate — these
    /// settings are one developer's, not the team's.
    /// </remarks>
    public sealed class FileSettingsStore : ISettingsStore
    {
        /// <remarks>
        /// Cannot collide with a session file in the same directory:
        /// <see cref="StorageKey.ForHead"/> always appends a hash, so no head produces
        /// "settings.json".
        /// </remarks>
        private const string SettingsFileName = "settings.json";

        /// <summary>
        /// Appearance preferences. Beside the defaults rather than under a repository, because
        /// they are the user's and are asked for before any solution is open.
        /// </summary>
        private const string PreferencesFileName = "ui.json";

        private readonly string _rootDirectory;

        /// <param name="rootDirectory">
        /// Storage root. Defaults to <c>%LOCALAPPDATA%\GitTabSync</c>; tests override it.
        /// </param>
        public FileSettingsStore(string? rootDirectory = null)
        {
            _rootDirectory = rootDirectory ?? FileSessionStore.DefaultRootDirectory();
        }

        public string RootDirectory => _rootDirectory;

        public ScopedSettings LoadDefaults() => ReadScopedSettings(GetDefaultsFilePath());

        public void SaveDefaults(ScopedSettings settings) => WriteFile(GetDefaultsFilePath(), settings);

        public ScopedSettings Load(string repositoryWorkingDirectory) =>
            ReadScopedSettings(GetRepositoryFilePath(repositoryWorkingDirectory));

        public void Save(string repositoryWorkingDirectory, ScopedSettings settings) =>
            WriteFile(GetRepositoryFilePath(repositoryWorkingDirectory), settings);

        public UiPreferences LoadPreferences() =>
            ReadFile<UiPreferences>(
                GetPreferencesFilePath(),
                document => document.SchemaVersion > UiPreferences.CurrentSchemaVersion,
                Repair);

        public void SavePreferences(UiPreferences preferences) =>
            WriteFile(GetPreferencesFilePath(), preferences);

        internal string GetDefaultsFilePath() => Path.Combine(_rootDirectory, SettingsFileName);

        internal string GetPreferencesFilePath() => Path.Combine(_rootDirectory, PreferencesFileName);

        internal string GetRepositoryFilePath(string repositoryWorkingDirectory)
        {
            var repositoryKey = StorageKey.ForRepository(repositoryWorkingDirectory);
            return Path.Combine(_rootDirectory, "repos", repositoryKey, SettingsFileName);
        }

        private static ScopedSettings ReadScopedSettings(string path) =>
            ReadFile<ScopedSettings>(
                path,
                document => document.SchemaVersion > ScopedSettings.CurrentSchemaVersion,
                Repair);

        /// <remarks>
        /// One reader for every document this store holds. The recovery policy — a file that is
        /// missing, unreadable, or written by a schema this build does not know is equivalent to
        /// having no file at all — has to be the same for all of them, and two copies of a
        /// judgement like that drift.
        /// </remarks>
        private static T ReadFile<T>(string path, Func<T, bool> isFromNewerSchema, Action<T> repair)
            where T : class, new()
        {
            try
            {
                if (!File.Exists(path))
                {
                    return new T();
                }

                using var stream = File.OpenRead(path);
                var serializer = new DataContractJsonSerializer(typeof(T));
                var document = serializer.ReadObject(stream) as T;

                if (document is null || isFromNewerSchema(document))
                {
                    // Written by a version that may mean something different by these fields.
                    // Falling back to the defaults is the conservative reading.
                    return new T();
                }

                repair(document);
                return document;
            }
            catch (Exception e) when (RecoverableStorageFailure.Matches(e))
            {
                // An unreadable settings file is equivalent to having none: the extension runs on
                // its defaults rather than refusing to run.
                return new T();
            }
        }

        /// <summary>
        /// Puts back what deserialisation leaves null. <c>DataContractJsonSerializer</c> does not
        /// run property initialisers, so a member absent from the JSON comes back as null however
        /// the class declares it.
        /// </summary>
        private static void Repair(ScopedSettings settings) =>
            settings.Scopes ??= new List<ScopeOverrides>();

        /// <inheritdoc cref="Repair(ScopedSettings)"/>
        private static void Repair(UiPreferences preferences) =>
            preferences.ThemeId ??= string.Empty;

        private static void WriteFile<T>(string path, T document)
            where T : class
        {
            if (document is null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Write-then-replace: a crash mid-write must not leave a truncated file where the
                // user's configuration used to be.
                var temporaryPath = path + ".tmp";

                using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var serializer = new DataContractJsonSerializer(typeof(T));
                    serializer.WriteObject(stream, document);
                    stream.Flush();
                }

                if (File.Exists(path))
                {
                    File.Replace(temporaryPath, path, destinationBackupFileName: null);
                }
                else
                {
                    File.Move(temporaryPath, path);
                }
            }
            catch (Exception e) when (RecoverableStorageFailure.Matches(e))
            {
                // Best effort by design: see ReadFile.
            }
        }

        /// <summary>Serialises settings to a JSON string. Exposed for diagnostics and tests.</summary>
        internal static string ToJson(ScopedSettings settings)
        {
            using var stream = new MemoryStream();
            var serializer = new DataContractJsonSerializer(typeof(ScopedSettings));
            serializer.WriteObject(stream, settings);
            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }
}
