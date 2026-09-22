using System.Runtime.Serialization;

namespace GitTabSync.Settings
{
    /// <summary>
    /// What this installation has already been told, as opposed to what it has been configured to
    /// do.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Beside <see cref="UiPreferences"/> and outside the <see cref="ScopedSettings"/> cascade, for
    /// a sharper version of the same reason. A scoped setting answers "what should happen on this
    /// branch"; this answers "has this user seen this release's notes", which is a fact about the
    /// install and is the same on every branch of every repository. A per-branch answer to it
    /// would mean the What's New page opening again the first time you checked out each branch.
    /// </para>
    /// <para>
    /// Its own document — <c>state.json</c> — rather than a member of the preferences, because the
    /// two have different owners. A preference is something the user chose and may want to keep
    /// across a reinstall; this is bookkeeping the extension did to itself, and deleting it is a
    /// reasonable way to ask to see the page again. It goes through <see cref="ISettingsStore"/>
    /// all the same, so it inherits the write-then-replace and the recovery policy rather than
    /// growing a second store that would have to be kept in step with the first.
    /// </para>
    /// </remarks>
    [DataContract(Name = "state", Namespace = "")]
    public sealed class InstallState
    {
        /// <inheritdoc cref="ScopedSettings.CurrentSchemaVersion"/>
        public const int CurrentSchemaVersion = 1;

        [DataMember(Name = "schema", Order = 0)]
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        /// <summary>
        /// The version whose release notes have been shown, as the assembly wrote it. Empty means
        /// none have been, which the landing page reads as a first install.
        /// </summary>
        /// <remarks>
        /// Stored as the text it came in as rather than as a normalised number, so that a value
        /// this build cannot parse survives being read by a build that can. Comparing is
        /// <c>ReleaseVersion</c>'s job, and it is lenient on purpose; storing its output here would
        /// bake one build's idea of the format into the file.
        /// </remarks>
        [DataMember(Name = "lastSeenVersion", Order = 1)]
        public string LastSeenVersion { get; set; } = string.Empty;
    }
}
