namespace GitTabSync.Settings
{
    /// <summary>
    /// Persists settings: one document of defaults shared by every repository, one document of
    /// overrides per repository, and one document of appearance preferences.
    /// </summary>
    /// <remarks>
    /// The split matches the lifetimes. Defaults belong to the user and outlive any particular
    /// clone; branch, solution and project overrides are meaningless without the repository they
    /// name, and are deleted with it. Preferences are neither scoped nor per-repository — they are
    /// how the user wants the window to look, which is true before any solution is open, so they
    /// live in a document of their own rather than as a scope nothing else could read.
    /// </remarks>
    public interface ISettingsStore
    {
        /// <summary>
        /// The defaults, holding <see cref="SettingScopeKind.Global"/> scopes only. Returns an
        /// empty document rather than <c>null</c> when nothing has been stored: "no overrides" is
        /// the normal state, and making every caller null-check it buys nothing.
        /// </summary>
        ScopedSettings LoadDefaults();

        void SaveDefaults(ScopedSettings settings);

        /// <inheritdoc cref="LoadDefaults"/>
        ScopedSettings Load(string repositoryWorkingDirectory);

        void Save(string repositoryWorkingDirectory, ScopedSettings settings);

        /// <inheritdoc cref="LoadDefaults"/>
        UiPreferences LoadPreferences();

        void SavePreferences(UiPreferences preferences);

        /// <summary>
        /// What this install has already been shown. A third document rather than a member of the
        /// preferences: see <see cref="InstallState"/>.
        /// </summary>
        /// <inheritdoc cref="LoadDefaults"/>
        InstallState LoadInstallState();

        void SaveInstallState(InstallState state);
    }
}
