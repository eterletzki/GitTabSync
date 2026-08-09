using System.Collections.Generic;
using System.Runtime.Serialization;

namespace GitTabSync.Model
{
    /// <summary>
    /// The set of open documents belonging to one HEAD, in tab order.
    /// </summary>
    [DataContract(Name = "session", Namespace = "")]
    public sealed class TabSession
    {
        /// <summary>
        /// Bumped when the shape changes incompatibly. A session written by a newer schema is
        /// ignored rather than half-understood.
        /// </summary>
        public const int CurrentSchemaVersion = 1;

        [DataMember(Name = "schema", Order = 0)]
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        /// <summary>The <see cref="Git.GitHead.SessionKey"/> these tabs belong to.</summary>
        [DataMember(Name = "head", Order = 1)]
        public string HeadKey { get; set; } = string.Empty;

        /// <summary>ISO-8601 round-trip UTC timestamp of the last save.</summary>
        [DataMember(Name = "savedAtUtc", Order = 2)]
        public string SavedAtUtc { get; set; } = string.Empty;

        /// <summary>Index into <see cref="Tabs"/> of the document that had focus, or -1.</summary>
        [DataMember(Name = "activeIndex", Order = 3)]
        public int ActiveIndex { get; set; } = -1;

        [DataMember(Name = "tabs", Order = 4)]
        public List<TabEntry> Tabs { get; set; } = new List<TabEntry>();
    }
}
