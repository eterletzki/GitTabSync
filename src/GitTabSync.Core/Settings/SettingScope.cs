using System;

namespace GitTabSync.Settings
{
    /// <summary>
    /// One place a setting can be set: a level, plus which branch, solution or project it applies
    /// to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Solution and project scopes carry a head key as well as a path, because they narrow a
    /// branch rather than standing beside it. "Do not sync bookmarks for the generated-code
    /// project" is a statement about that project <em>on that branch</em>; the same project on
    /// another branch is a different scope.
    /// </para>
    /// <para>
    /// Identity follows the same split as
    /// <see cref="Storage.StorageKey"/>: paths compare case-<em>insensitively</em> because Windows
    /// paths do, head keys compare case-<em>sensitively</em> because git refs do — <c>Feature</c>
    /// and <c>feature</c> are two branches. The path half of <see cref="Key"/> is lower-cased when
    /// the scope is built so that ordinal comparison is the whole rule afterwards.
    /// </para>
    /// </remarks>
    public sealed class SettingScope : IEquatable<SettingScope>
    {
        /// <summary>Separates the head key from the path in <see cref="Key"/>.</summary>
        /// <remarks>
        /// '|' is not legal in a Windows path and not legal in a git ref name, so neither half can
        /// contain one and the split is unambiguous.
        /// </remarks>
        private const char KeySeparator = '|';

        private SettingScope(SettingScopeKind kind, string headKey, string path)
        {
            Kind = kind;
            HeadKey = headKey;
            Path = path;

            switch (kind)
            {
                case SettingScopeKind.Global:
                case SettingScopeKind.Repository:
                    Key = string.Empty;
                    break;
                case SettingScopeKind.Branch:
                    Key = headKey;
                    break;
                default:
                    Key = headKey + KeySeparator + path.ToLowerInvariant();
                    break;
            }
        }

        /// <summary>The defaults, applying to every repository.</summary>
        public static SettingScope Global { get; } =
            new SettingScope(SettingScopeKind.Global, string.Empty, string.Empty);

        /// <summary>
        /// The whole repository. There is one settings file per repository, so the scope needs no
        /// key of its own to tell it apart.
        /// </summary>
        public static SettingScope Repository { get; } =
            new SettingScope(SettingScopeKind.Repository, string.Empty, string.Empty);

        public static SettingScope Branch(string headKey)
        {
            if (headKey is null)
            {
                throw new ArgumentNullException(nameof(headKey));
            }

            return new SettingScope(SettingScopeKind.Branch, headKey, string.Empty);
        }

        /// <param name="path">
        /// Repository-relative where possible, with '/' separators — the same form
        /// <see cref="Model.TabEntry.Path"/> uses, and for the same reason: settings survive the
        /// repository being moved or re-cloned.
        /// </param>
        public static SettingScope Solution(string headKey, string path) =>
            ForPath(SettingScopeKind.Solution, headKey, path);

        /// <inheritdoc cref="Solution"/>
        public static SettingScope Project(string headKey, string path) =>
            ForPath(SettingScopeKind.Project, headKey, path);

        private static SettingScope ForPath(SettingScopeKind kind, string headKey, string path)
        {
            if (headKey is null)
            {
                throw new ArgumentNullException(nameof(headKey));
            }

            if (path is null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            return new SettingScope(kind, headKey, path.Replace('\\', '/'));
        }

        public SettingScopeKind Kind { get; }

        /// <summary>The head this scope belongs to, or empty for global and repository scopes.</summary>
        public string HeadKey { get; }

        /// <summary>The solution or project file, or empty for the other kinds.</summary>
        public string Path { get; }

        /// <summary>Stable identity within a settings file. Empty where the kind is enough.</summary>
        public string Key { get; }

        public bool Equals(SettingScope? other) =>
            other is not null
            && other.Kind == Kind
            && string.Equals(other.Key, Key, StringComparison.Ordinal);

        public override bool Equals(object? obj) => Equals(obj as SettingScope);

        public override int GetHashCode()
        {
            unchecked
            {
                // netstandard2.0 has no HashCode.Combine.
                return ((int)Kind * 397) ^ StringComparer.Ordinal.GetHashCode(Key);
            }
        }

        public override string ToString() =>
            Key.Length == 0 ? Kind.ToString() : Kind + ":" + Key;
    }
}
