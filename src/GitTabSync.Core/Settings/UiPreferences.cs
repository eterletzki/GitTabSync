using System.Runtime.Serialization;

namespace GitTabSync.Settings
{
    /// <summary>
    /// The user's preferences about how the extension looks, as opposed to what it syncs.
    /// </summary>
    /// <remarks>
    /// Deliberately outside the <see cref="ScopedSettings"/> cascade. Everything in that cascade is
    /// a three-state boolean answering "what should happen on this branch"; appearance is neither
    /// boolean nor per-branch, and pretending otherwise would mean a user could end up with the
    /// window changing colour when they checked out a release branch. One value, one file, one
    /// answer everywhere.
    /// </remarks>
    [DataContract(Name = "preferences", Namespace = "")]
    public sealed class UiPreferences
    {
        /// <inheritdoc cref="ScopedSettings.CurrentSchemaVersion"/>
        public const int CurrentSchemaVersion = 1;

        [DataMember(Name = "schema", Order = 0)]
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        /// <summary>
        /// The <see cref="Theming.ThemeInfo.Id"/> of the chosen theme, or empty for the built-in
        /// default.
        /// </summary>
        /// <remarks>
        /// Stored by id rather than by path so that moving the storage root — or a theme file
        /// briefly going missing — does not silently reset the choice. An id naming no file on
        /// disk resolves to the default and stays stored, so putting the file back restores the
        /// choice.
        /// </remarks>
        [DataMember(Name = "theme", Order = 1)]
        public string ThemeId { get; set; } = string.Empty;
    }
}
