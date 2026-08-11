using System;
using System.Collections.Generic;
using GitTabSync.Settings;

namespace GitTabSync.Tests.TestSupport
{
    public sealed class InMemorySettingsStore : ISettingsStore
    {
        private readonly Dictionary<string, ScopedSettings> _repositories =
            new Dictionary<string, ScopedSettings>(StringComparer.OrdinalIgnoreCase);

        private ScopedSettings _defaults = new ScopedSettings();

        public int SaveCount { get; private set; }

        /// <summary>When set, both save methods throw it.</summary>
        public Exception? FailOnSave { get; set; }

        public ScopedSettings LoadDefaults() => _defaults;

        public void SaveDefaults(ScopedSettings settings)
        {
            if (FailOnSave is not null)
            {
                throw FailOnSave;
            }

            SaveCount++;
            _defaults = settings;
        }

        public ScopedSettings Load(string repositoryWorkingDirectory) =>
            _repositories.TryGetValue(repositoryWorkingDirectory, out var settings) ? settings : new ScopedSettings();

        public void Save(string repositoryWorkingDirectory, ScopedSettings settings)
        {
            if (FailOnSave is not null)
            {
                throw FailOnSave;
            }

            SaveCount++;
            _repositories[repositoryWorkingDirectory] = settings;
        }

        /// <summary>Puts a document in place without going through a resolver, for load tests.</summary>
        public void Seed(string repositoryWorkingDirectory, ScopedSettings settings) =>
            _repositories[repositoryWorkingDirectory] = settings;

        /// <inheritdoc cref="Seed"/>
        public void SeedDefaults(ScopedSettings settings) => _defaults = settings;
    }
}
