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

        private UiPreferences _preferences = new UiPreferences();

        public int SaveCount { get; private set; }

        /// <summary>When set, every save method throws it.</summary>
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

        public UiPreferences LoadPreferences() =>
            new UiPreferences { SchemaVersion = _preferences.SchemaVersion, ThemeId = _preferences.ThemeId };

        public void SavePreferences(UiPreferences preferences)
        {
            if (FailOnSave is not null)
            {
                throw FailOnSave;
            }

            SaveCount++;
            _preferences = preferences;
        }

        private InstallState _installState = new InstallState();

        public InstallState LoadInstallState() =>
            new InstallState
            {
                SchemaVersion = _installState.SchemaVersion,
                LastSeenVersion = _installState.LastSeenVersion,
            };

        public void SaveInstallState(InstallState state)
        {
            if (FailOnSave is not null)
            {
                throw FailOnSave;
            }

            if (DropsInstallStateSaves)
            {
                // A save that is accepted and kept nowhere. This is what FileSettingsStore looks
                // like from the outside when the write fails: it swallows the IO failure and
                // returns void, so "succeeded" and "did nothing" are the same observation. The
                // landing page gate is the one caller that must tell them apart, so it must be
                // testable against a store that lies this way and not only against one that
                // throws.
                SaveCount++;
                return;
            }

            SaveCount++;
            _installState = state;
        }

        /// <summary>
        /// When set, <see cref="SaveInstallState"/> reports success and keeps nothing.
        /// </summary>
        public bool DropsInstallStateSaves { get; set; }

        /// <inheritdoc cref="Seed"/>
        public void SeedInstallState(InstallState state) => _installState = state;

        /// <summary>Puts a document in place without going through a resolver, for load tests.</summary>
        public void Seed(string repositoryWorkingDirectory, ScopedSettings settings) =>
            _repositories[repositoryWorkingDirectory] = settings;

        /// <inheritdoc cref="Seed"/>
        public void SeedDefaults(ScopedSettings settings) => _defaults = settings;
    }
}
