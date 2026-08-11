namespace GitTabSync.Settings
{
    /// <summary>
    /// The levels a setting can be set at, ordered from broadest to narrowest.
    /// </summary>
    /// <remarks>
    /// The numeric values order the cascade: <see cref="SyncContext"/> asks the narrowest level
    /// first and takes the first answer it finds. They are never persisted — the settings file
    /// stores the member name — so they can be renumbered to insert a level in between.
    /// </remarks>
    public enum SettingScopeKind
    {
        /// <summary>Applies to every repository. The "Defaults" the user edits once.</summary>
        Global = 0,

        /// <summary>Applies to one repository, on every branch.</summary>
        Repository = 1,

        /// <summary>
        /// Applies to one branch of one repository. This is the level the extension is really
        /// about: a branch is the unit of work, so it is the unit settings reach over by default.
        /// </summary>
        Branch = 2,

        /// <summary>Narrows a branch to one solution file within it.</summary>
        Solution = 3,

        /// <summary>Narrows a branch to one project file within it.</summary>
        Project = 4,
    }
}
