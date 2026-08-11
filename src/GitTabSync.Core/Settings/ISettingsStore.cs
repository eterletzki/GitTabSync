namespace GitTabSync.Settings
{
    /// <summary>
    /// Persists settings: one document of defaults shared by every repository, and one document of
    /// overrides per repository.
    /// </summary>
    /// <remarks>
    /// The split matches the two lifetimes. Defaults belong to the user and outlive any particular
    /// clone; branch, solution and project overrides are meaningless without the repository they
    /// name, and are deleted with it.
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
    }
}
