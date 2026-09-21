using System.Collections.Generic;
using System.Runtime.Serialization;

namespace GitTabSync.Settings
{
    /// <summary>
    /// The persisted shape of a settings file: a flat list of scopes, each holding the settings
    /// that were explicitly set there.
    /// </summary>
    /// <remarks>
    /// Serialised with <c>DataContractJsonSerializer</c> for the same reason sessions are — the
    /// VSIX ships no extra assemblies and cannot hit a binding redirect against a copy of
    /// Newtonsoft that Visual Studio already has loaded.
    /// </remarks>
    [DataContract(Name = "settings", Namespace = "")]
    public sealed class ScopedSettings
    {
        /// <summary>
        /// Bumped when the shape changes incompatibly. A file written by a newer schema is ignored
        /// rather than half-understood — which for settings means falling back to the defaults,
        /// not to somebody else's idea of what a field meant.
        /// </summary>
        public const int CurrentSchemaVersion = 1;

        [DataMember(Name = "schema", Order = 0)]
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        [DataMember(Name = "scopes", Order = 1)]
        public List<ScopeOverrides> Scopes { get; set; } = new List<ScopeOverrides>();
    }

    /// <summary>Everything explicitly set at one <see cref="SettingScope"/>.</summary>
    [DataContract(Name = "scope", Namespace = "")]
    public sealed class ScopeOverrides
    {
        /// <summary>
        /// <see cref="SettingScopeKind"/> by name. Stored as text rather than as the enum's number
        /// so that renumbering the enum cannot silently reinterpret a file: an unrecognised kind is
        /// skipped on load.
        /// </summary>
        [DataMember(Name = "kind", Order = 0)]
        public string Kind { get; set; } = string.Empty;

        /// <summary>The head this scope belongs to; empty for global and repository scopes.</summary>
        [DataMember(Name = "head", Order = 1)]
        public string HeadKey { get; set; } = string.Empty;

        /// <summary>The solution or project file; empty for the other kinds.</summary>
        [DataMember(Name = "path", Order = 2)]
        public string Path { get; set; } = string.Empty;

        [DataMember(Name = "values", Order = 3)]
        public List<SettingOverride> Values { get; set; } = new List<SettingOverride>();
    }

    /// <summary>One setting, explicitly set at the scope that contains it.</summary>
    /// <remarks>
    /// This is how the three states are encoded: an entry that is present means on or off, and an
    /// entry that is absent means inherit. There is deliberately no third value — "set to off" and
    /// "not set here" have to be distinguishable or the whole cascade collapses into whichever
    /// level was written last, and a nullable field that round-trips through
    /// <c>DataContractJsonSerializer</c> is a worse way to say the same thing than simply leaving
    /// the entry out.
    /// </remarks>
    [DataContract(Name = "override", Namespace = "")]
    public sealed class SettingOverride
    {
        /// <summary>
        /// <see cref="SyncSetting"/> by name, per <see cref="SyncSettingCatalog.NameOf"/>. An
        /// unrecognised name is skipped, so a file written by a build that knows about a setting
        /// this one does not still loads.
        /// </summary>
        [DataMember(Name = "setting", Order = 0)]
        public string Setting { get; set; } = string.Empty;

        [DataMember(Name = "on", Order = 1)]
        public bool On { get; set; }
    }
}
