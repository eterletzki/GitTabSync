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

        public TabSession? Load(string repositoryWorkingDirectory, string headKey)
        {
            return _sessions.TryGetValue(Key(repositoryWorkingDirectory, headKey), out var session) ? session : null;
        }

        public void Save(string repositoryWorkingDirectory, TabSession session)
        {
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
