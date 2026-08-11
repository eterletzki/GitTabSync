using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using GitTabSync.Model;

namespace GitTabSync.Sync
{
    /// <summary>
    /// Converts between the editor's view of open tabs and the persisted session form.
    /// </summary>
    public static class TabSessionMapper
    {
        public static TabSession ToSession(
            IReadOnlyList<EditorTab> tabs,
            int activeIndex,
            string repositoryWorkingDirectory,
            string headKey,
            DateTime savedAtUtc)
        {
            if (tabs is null)
            {
                throw new ArgumentNullException(nameof(tabs));
            }

            var session = new TabSession
            {
                SchemaVersion = TabSession.CurrentSchemaVersion,
                HeadKey = headKey,
                SavedAtUtc = savedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                ActiveIndex = activeIndex >= 0 && activeIndex < tabs.Count ? activeIndex : -1,
            };

            foreach (var tab in tabs)
            {
                var isRelative = TryMakeRepositoryRelative(tab.AbsolutePath, repositoryWorkingDirectory, out var stored);

                session.Tabs.Add(new TabEntry
                {
                    Path = stored,
                    IsRepositoryRelative = isRelative,
                    CaretLine = tab.CaretLine,
                    CaretColumn = tab.CaretColumn,
                    IsPinned = tab.IsPinned,
                });
            }

            return session;
        }

        /// <summary>
        /// Rebuilds editor tabs from a stored session, dropping entries whose file is not present.
        /// </summary>
        /// <param name="fileExists">
        /// Existence check; defaults to <see cref="File.Exists"/>. Injectable for tests.
        /// </param>
        /// <remarks>
        /// The filtering is the point, not an optimisation: a branch switch is precisely the
        /// event that makes files appear and disappear, so a session saved on one branch will
        /// routinely name files that do not exist on another. Restoring those would surface as
        /// "file not found" dialogs on every switch.
        /// </remarks>
        public static IReadOnlyList<EditorTab> ToEditorTabs(
            TabSession session,
            string repositoryWorkingDirectory,
            out int activeIndex,
            Func<string, bool>? fileExists = null)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            fileExists ??= File.Exists;

            var result = new List<EditorTab>();
            var previousActive = session.ActiveIndex;
            activeIndex = -1;

            var entries = session.Tabs ?? new List<TabEntry>();
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry is null || string.IsNullOrWhiteSpace(entry.Path))
                {
                    continue;
                }

                var absolute = Resolve(entry, repositoryWorkingDirectory);
                if (absolute is null || !fileExists(absolute))
                {
                    continue;
                }

                if (i == previousActive)
                {
                    // Track where the active document landed after earlier entries were dropped.
                    activeIndex = result.Count;
                }

                result.Add(new EditorTab(absolute, entry.CaretLine, entry.CaretColumn, entry.IsPinned));
            }

            return result;
        }

        private static string? Resolve(TabEntry entry, string repositoryWorkingDirectory)
        {
            try
            {
                if (!entry.IsRepositoryRelative)
                {
                    return Path.GetFullPath(entry.Path);
                }

                var native = entry.Path.Replace('/', Path.DirectorySeparatorChar);
                return Path.GetFullPath(Path.Combine(repositoryWorkingDirectory, native));
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return null;
            }
        }

        internal static bool TryMakeRepositoryRelative(string absolutePath, string repositoryWorkingDirectory, out string result)
        {
            result = absolutePath;

            if (string.IsNullOrEmpty(absolutePath) || string.IsNullOrEmpty(repositoryWorkingDirectory))
            {
                return false;
            }

            string fullPath;
            string root;
            try
            {
                fullPath = Path.GetFullPath(absolutePath);
                root = Path.GetFullPath(repositoryWorkingDirectory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return false;
            }

            if (root.Length == 0 || fullPath.Length <= root.Length + 1)
            {
                return false;
            }

            // Windows paths compare case-insensitively; a repository at C:\Src and a file
            // reported as c:\src\... are the same location.
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var separator = fullPath[root.Length];
            if (separator != Path.DirectorySeparatorChar && separator != Path.AltDirectorySeparatorChar)
            {
                // Guards against "C:\SrcOther\file" being treated as inside "C:\Src".
                return false;
            }

            result = fullPath.Substring(root.Length + 1).Replace(Path.DirectorySeparatorChar, '/');
            return true;
        }
    }
}
