using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using GitTabSync.Model;

namespace GitTabSync.Storage
{
    /// <summary>
    /// Stores sessions as JSON under the user's local application data.
    /// </summary>
    /// <remarks>
    /// The location is deliberately outside the working tree. Anything kept inside the repository
    /// would itself be rewritten by the checkout that the session is trying to survive, and would
    /// show up as a pending change on every branch switch.
    /// </remarks>
    public sealed class FileSessionStore : ISessionStore
    {
        private const string SessionFileExtension = ".json";

        private readonly string _rootDirectory;

        /// <param name="rootDirectory">
        /// Storage root. Defaults to <c>%LOCALAPPDATA%\GitTabSync</c>; tests override it.
        /// </param>
        public FileSessionStore(string? rootDirectory = null)
        {
            _rootDirectory = rootDirectory ?? DefaultRootDirectory();
        }

        public static string DefaultRootDirectory()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "GitTabSync");
        }

        public string RootDirectory => _rootDirectory;

        public TabSession? Load(string repositoryWorkingDirectory, string headKey)
        {
            var path = GetSessionFilePath(repositoryWorkingDirectory, headKey);

            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                using var stream = File.OpenRead(path);
                var serializer = new DataContractJsonSerializer(typeof(TabSession));
                var session = serializer.ReadObject(stream) as TabSession;

                if (session is null)
                {
                    return null;
                }

                // Written by a newer version that may mean something different by these fields.
                if (session.SchemaVersion > TabSession.CurrentSchemaVersion)
                {
                    return null;
                }

                session.Tabs ??= new System.Collections.Generic.List<TabEntry>();
                return session;
            }
            catch (Exception e) when (IsRecoverableStorageFailure(e))
            {
                // A corrupt or unreadable session is equivalent to not having one. Losing a
                // remembered tab set is a far better outcome than failing a branch switch.
                return null;
            }
        }

        public void Save(string repositoryWorkingDirectory, TabSession session)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            var path = GetSessionFilePath(repositoryWorkingDirectory, session.HeadKey);
            var directory = Path.GetDirectoryName(path);

            try
            {
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Write-then-replace: a crash mid-write must not leave a truncated file where a
                // previously good session used to be.
                var temporaryPath = path + ".tmp";

                using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var serializer = new DataContractJsonSerializer(typeof(TabSession));
                    serializer.WriteObject(stream, session);
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
            catch (Exception e) when (IsRecoverableStorageFailure(e))
            {
                // Best effort by design: see Load.
            }
        }

        public void Delete(string repositoryWorkingDirectory, string headKey)
        {
            try
            {
                var path = GetSessionFilePath(repositoryWorkingDirectory, headKey);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception e) when (IsRecoverableStorageFailure(e))
            {
            }
        }

        internal string GetSessionFilePath(string repositoryWorkingDirectory, string headKey)
        {
            var repositoryKey = StorageKey.ForRepository(repositoryWorkingDirectory);
            var fileName = StorageKey.ForHead(headKey) + SessionFileExtension;
            return Path.Combine(_rootDirectory, "repos", repositoryKey, fileName);
        }

        private static bool IsRecoverableStorageFailure(Exception e)
        {
            return e is IOException
                || e is UnauthorizedAccessException
                || e is System.Runtime.Serialization.SerializationException
                || e is System.Xml.XmlException
                || e is ArgumentException
                || e is NotSupportedException;
        }

        /// <summary>
        /// Serialises a session to a JSON string. Exposed for diagnostics and tests.
        /// </summary>
        internal static string ToJson(TabSession session)
        {
            using var stream = new MemoryStream();
            var serializer = new DataContractJsonSerializer(typeof(TabSession));
            serializer.WriteObject(stream, session);
            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }
}
