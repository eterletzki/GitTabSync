using System;
using System.Collections.Generic;
using GitTabSync.Model;
using GitTabSync.Storage;

namespace GitTabSync.Tests.TestSupport
{
    public sealed class InMemorySessionStore : ISessionStore
    {
        private readonly Dictionary<string, TabSession> _sessions =
            new Dictionary<string, TabSession>(StringComparer.Ordinal);

        public int SaveCount { get; private set; }

        public IReadOnlyDictionary<string, TabSession> Sessions => _sessions;

        /// <summary>
        /// When set, <see cref="Load"/> throws it — a session file the user's disk, antivirus or
        /// roaming profile made unreadable at the worst possible moment.
        /// </summary>
        public Exception? FailOnLoad { get; set; }

        /// <summary>When set, <see cref="Save"/> throws it.</summary>
        public Exception? FailOnSave { get; set; }

        public TabSession? Load(string repositoryWorkingDirectory, string headKey)
        {
            if (FailOnLoad is not null)
            {
                throw FailOnLoad;
            }

            return _sessions.TryGetValue(Key(repositoryWorkingDirectory, headKey), out var session) ? session : null;
        }

        public void Save(string repositoryWorkingDirectory, TabSession session)
        {
            if (FailOnSave is not null)
            {
                throw FailOnSave;
            }

            SaveCount++;
            _sessions[Key(repositoryWorkingDirectory, session.HeadKey)] = session;
        }

        public void Delete(string repositoryWorkingDirectory, string headKey)
        {
            _sessions.Remove(Key(repositoryWorkingDirectory, headKey));
        }

        public TabSession? Get(string repositoryWorkingDirectory, string headKey) =>
            Load(repositoryWorkingDirectory, headKey);

        private static string Key(string repositoryWorkingDirectory, string headKey) =>
            repositoryWorkingDirectory + "|" + headKey;
    }
}
