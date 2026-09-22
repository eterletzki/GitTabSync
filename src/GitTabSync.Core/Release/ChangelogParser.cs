using System;
using System.Collections.Generic;

namespace GitTabSync.Release
{
    /// <summary>
    /// Reads CHANGELOG.md into <see cref="ReleaseNotes"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately not a markdown parser. It understands exactly the three things the What's New
    /// page shows — which release, when, and what changed — and treats everything else in the file
    /// as prose for human readers. That keeps the changelog a document somebody can write
    /// naturally, with a preamble and notes between releases, rather than a data file that happens
    /// to render.
    /// </para>
    /// <para>
    /// The rules, which CHANGELOG.md states at the top so that whoever edits it is reading the same
    /// contract:
    /// </para>
    /// <list type="bullet">
    /// <item><c>## &lt;version&gt; [separator] [date]</c> begins a release.</item>
    /// <item><c>### &lt;heading&gt;</c> begins a group of changes within it.</item>
    /// <item><c>-</c>, <c>*</c> or <c>+</c> begins one change; an indented line continues it.</item>
    /// </list>
    /// <para>
    /// A release whose heading does not begin with a version — an "Unreleased" section, most
    /// often — is skipped along with everything under it. Skipping is the conservative reading:
    /// the page exists to say what is in the build the user just installed, and a section with no
    /// version is by definition not in it.
    /// </para>
    /// </remarks>
    public static class ChangelogParser
    {
        private const string EntryPrefix = "## ";
        private const string GroupPrefix = "### ";

        public static ReleaseNotes Parse(string? markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown))
            {
                return ReleaseNotes.Empty;
            }

            var entries = new List<ReleaseEntry>();
            EntryBuilder? current = null;

            // Whether the previous line was a change, and so whether an indented line now is a
            // continuation of it rather than prose.
            var inChange = false;

            foreach (var raw in markdown!.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                var trimmed = line.Trim();

                if (trimmed.Length == 0)
                {
                    inChange = false;
                    continue;
                }

                if (trimmed.StartsWith(GroupPrefix, StringComparison.Ordinal))
                {
                    current?.StartGroup(trimmed.Substring(GroupPrefix.Length).Trim());
                    inChange = false;
                    continue;
                }

                if (trimmed.StartsWith(EntryPrefix, StringComparison.Ordinal))
                {
                    Close(entries, current);
                    current = StartEntry(trimmed.Substring(EntryPrefix.Length));
                    inChange = false;
                    continue;
                }

                if (trimmed[0] == '#')
                {
                    // The file's title, or a heading deeper than a group. Neither is content.
                    inChange = false;
                    continue;
                }

                if (IsBullet(trimmed))
                {
                    current?.AddChange(trimmed.Substring(1).Trim());
                    inChange = current is not null;
                    continue;
                }

                if (inChange && char.IsWhiteSpace(line[0]))
                {
                    // A wrapped bullet. Joined with a space: the line break is the changelog
                    // staying inside a margin, not something the reader is meant to see.
                    current?.AppendToLastChange(trimmed);
                    continue;
                }

                inChange = false;
            }

            Close(entries, current);
            return entries.Count == 0 ? ReleaseNotes.Empty : new ReleaseNotes(entries);
        }

        private static bool IsBullet(string trimmed) =>
            trimmed.Length > 1
            && (trimmed[0] == '-' || trimmed[0] == '*' || trimmed[0] == '+')
            && char.IsWhiteSpace(trimmed[1]);

        private static void Close(List<ReleaseEntry> entries, EntryBuilder? builder)
        {
            if (builder is not null)
            {
                entries.Add(builder.Build());
            }
        }

        /// <summary>
        /// Splits a release heading into the version and whatever follows it. The separator is not
        /// prescribed — an em dash, a hyphen or a colon all read as one in a heading, and telling
        /// somebody their changelog is wrong because they typed the wrong dash would be a poor
        /// trade for the one line of code it saves.
        /// </summary>
        private static EntryBuilder? StartEntry(string heading)
        {
            var text = heading.Trim();
            if (text.Length == 0)
            {
                return null;
            }

            // Not just whitespace: a separator can be written against the version with no space
            // before it. A hyphen is deliberately absent, because "1.2.0-beta" is one token —
            // ReleaseVersion is what decides what to do with the suffix.
            var cut = text.IndexOfAny(new[] { ' ', '\t', ':', '—', '–', '·' });
            var token = cut < 0 ? text : text.Substring(0, cut);

            if (!ReleaseVersion.TryParse(token, out var version))
            {
                return null;
            }

            var date = cut < 0
                ? string.Empty
                : text.Substring(cut).TrimStart(' ', '\t', ':', '—', '–', '·', '-').Trim();

            return new EntryBuilder(token.Trim('[', ']', '(', ')'), version, date);
        }

        /// <summary>
        /// Collects one release as its lines arrive. A group with no changes under it is dropped
        /// rather than shown as an empty heading.
        /// </summary>
        private sealed class EntryBuilder
        {
            private readonly string _versionText;
            private readonly Version _version;
            private readonly string _date;
            private readonly List<ReleaseChangeGroup> _groups = new List<ReleaseChangeGroup>();

            private List<string> _changes = new List<string>();
            private string _heading = string.Empty;

            public EntryBuilder(string versionText, Version version, string date)
            {
                _versionText = versionText;
                _version = version;
                _date = date;
            }

            public void StartGroup(string heading)
            {
                CloseGroup();
                _heading = heading;
            }

            public void AddChange(string text)
            {
                if (text.Length != 0)
                {
                    _changes.Add(text);
                }
            }

            public void AppendToLastChange(string text)
            {
                if (_changes.Count != 0)
                {
                    _changes[_changes.Count - 1] = _changes[_changes.Count - 1] + " " + text;
                }
            }

            public ReleaseEntry Build()
            {
                CloseGroup();
                return new ReleaseEntry(_versionText, _version, _date, _groups);
            }

            private void CloseGroup()
            {
                if (_changes.Count != 0)
                {
                    _groups.Add(new ReleaseChangeGroup(_heading, _changes));
                    _changes = new List<string>();
                }

                _heading = string.Empty;
            }
        }
    }
}
