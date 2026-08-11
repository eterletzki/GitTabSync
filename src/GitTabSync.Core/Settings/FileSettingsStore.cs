using System;
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

        private readonly string _rootDirectory;

        /// <param name="rootDirectory">
        /// Storage root. Defaults to <c>%LOCALAPPDATA%\GitTabSync</c>; tests override it.
        /// </param>
        public FileSettingsStore(string? rootDirectory = null)
        {
            _rootDirectory = rootDirectory ?? FileSessionStore.DefaultRootDirectory();
        }

        public string RootDirectory => _rootDirectory;

        public ScopedSettings LoadDefaults() => ReadFile(GetDefaultsFilePath());

        public void SaveDefaults(ScopedSettings settings) => WriteFile(GetDefaultsFilePath(), settings);

        public ScopedSettings Load(string repositoryWorkingDirectory) =>
            ReadFile(GetRepositoryFilePath(repositoryWorkingDirectory));

        public void Save(string repositoryWorkingDirectory, ScopedSettings settings) =>
            WriteFile(GetRepositoryFilePath(repositoryWorkingDirectory), settings);

        internal string GetDefaultsFilePath() => Path.Combine(_rootDirectory, SettingsFileName);

        internal string GetRepositoryFilePath(string repositoryWorkingDirectory)
        {
            var repositoryKey = StorageKey.ForRepository(repositoryWorkingDirectory);
            return Path.Combine(_rootDirectory, "repos", repositoryKey, SettingsFileName);
        }

        private static ScopedSettings ReadFile(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return new ScopedSettings();
                }

                using var stream = File.OpenRead(path);
                var serializer = new DataContractJsonSerializer(typeof(ScopedSettings));
                var settings = serializer.ReadObject(stream) as ScopedSettings;

                if (settings is null || settings.SchemaVersion > ScopedSettings.CurrentSchemaVersion)
                {
                    // Written by a version that may mean something different by these fields.
                    // Falling back to the defaults is the conservative reading.
                    return new ScopedSettings();
                }

                settings.Scopes ??= new System.Collections.Generic.List<ScopeOverrides>();
                return settings;
            }
            catch (Exception e) when (RecoverableStorageFailure.Matches(e))
            {
                // An unreadable settings file is equivalent to having none: the extension runs on
                // its defaults rather than refusing to run.
                return new ScopedSettings();
            }
        }

        private static void WriteFile(string path, ScopedSettings settings)
        {
            if (settings is null)
            {
                throw new ArgumentNullException(nameof(settings));
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
                    var serializer = new DataContractJsonSerializer(typeof(ScopedSettings));
                    serializer.WriteObject(stream, settings);
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
