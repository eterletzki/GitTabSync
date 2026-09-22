using System;
using System.Collections.Generic;

namespace GitTabSync.Release
{
    /// <summary>
    /// The changelog, parsed: every release this build knows about, in the order the file lists
    /// them.
    /// </summary>
    /// <remarks>
    /// A structured shape rather than the markdown itself, because the window renders it with
    /// ordinary WPF controls. Taking a markdown renderer as a dependency would put a third
    /// assembly in the VSIX for the sake of one page — the same trade that
    /// <c>DataContractJsonSerializer</c> was chosen to avoid — and it would make the content
    /// untestable, since "does this look right" is not a question Core can ask.
    /// </remarks>
    public sealed class ReleaseNotes
    {
        /// <summary>What a changelog that is missing, empty or unparseable comes back as.</summary>
        public static readonly ReleaseNotes Empty = new ReleaseNotes(new ReleaseEntry[0]);

        public ReleaseNotes(IReadOnlyList<ReleaseEntry> entries)
        {
            Entries = entries ?? throw new ArgumentNullException(nameof(entries));
        }

        /// <summary>In file order, which is newest first by convention but is not relied on.</summary>
        public IReadOnlyList<ReleaseEntry> Entries { get; }

        public bool IsEmpty => Entries.Count == 0;

        /// <summary>
        /// The highest version in the file, found by comparing rather than by taking the first
        /// entry — a changelog is hand-edited, and an entry added in the wrong place should not
        /// change what "latest" means.
        /// </summary>
        public ReleaseEntry? Latest
        {
            get
            {
                ReleaseEntry? latest = null;

                foreach (var entry in Entries)
                {
                    if (latest is null || entry.Version > latest.Version)
                    {
                        latest = entry;
                    }
                }

                return latest;
            }
        }

        /// <summary>
        /// Every release above <paramref name="lastSeen"/> and no higher than
        /// <paramref name="current"/>, newest first.
        /// </summary>
        /// <remarks>
        /// Bounded at both ends on purpose. The lower bound is what makes this "what changed since
        /// you last looked" rather than the whole history. The upper bound matters when a
        /// changelog has been written ahead of the release it describes: announcing a version the
        /// user does not have is worse than saying nothing, because every item in it is a promise
        /// the installed build cannot keep.
        /// </remarks>
        public IReadOnlyList<ReleaseEntry> Between(Version lastSeen, Version current)
        {
            if (lastSeen is null)
            {
                throw new ArgumentNullException(nameof(lastSeen));
            }

            if (current is null)
            {
                throw new ArgumentNullException(nameof(current));
            }

            var found = new List<ReleaseEntry>();

            foreach (var entry in Entries)
            {
                if (entry.Version > lastSeen && entry.Version <= current)
                {
                    found.Add(entry);
                }
            }

            found.Sort((left, right) => right.Version.CompareTo(left.Version));
            return found;
        }
    }

    /// <summary>One release: a version, when it happened, and what changed in it.</summary>
    public sealed class ReleaseEntry
    {
        public ReleaseEntry(
            string versionText,
            Version version,
            string date,
            IReadOnlyList<ReleaseChangeGroup> groups)
        {
            VersionText = versionText ?? string.Empty;
            Version = version ?? throw new ArgumentNullException(nameof(version));
            Date = date ?? string.Empty;
            Groups = groups ?? throw new ArgumentNullException(nameof(groups));
        }

        /// <summary>The version as the changelog writes it, for showing.</summary>
        public string VersionText { get; }

        /// <summary>
        /// The same version, for comparing. Parsed once here so that nothing downstream has to
        /// decide what "1.1" means next to "1.1.0" — see <see cref="ReleaseVersion"/>.
        /// </summary>
        public Version Version { get; }

        /// <summary>As written in the heading, or empty when it carries none.</summary>
        public string Date { get; }

        public IReadOnlyList<ReleaseChangeGroup> Groups { get; }
    }

    /// <summary>The changes under one heading — "Added", "Fixed", and so on.</summary>
    public sealed class ReleaseChangeGroup
    {
        public ReleaseChangeGroup(string heading, IReadOnlyList<string> changes)
        {
            Heading = heading ?? string.Empty;
            Changes = changes ?? throw new ArgumentNullException(nameof(changes));
        }

        /// <summary>
        /// Empty when the release listed its changes without grouping them, which the window
        /// renders as a list with no heading rather than inventing one.
        /// </summary>
        public string Heading { get; }

        public bool HasHeading => Heading.Length != 0;

        public IReadOnlyList<string> Changes { get; }
    }
}
