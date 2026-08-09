using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace GitTabSync.Storage
{
    /// <summary>
    /// Turns repository paths and HEAD keys into filesystem-safe directory and file names.
    /// </summary>
    /// <remarks>
    /// Every key is "readable prefix + hash of the original". Branch names legitimately contain
    /// characters that are illegal in file names ('/' in <c>feature/foo</c>, ':' in some remotes),
    /// and sanitising alone would map <c>feature/foo</c> and <c>feature-foo</c> onto the same
    /// file. The hash carries the identity; the prefix only exists so the cache can be read by a
    /// human.
    /// </remarks>
    internal static class StorageKey
    {
        private const int MaxPrefixLength = 40;
        private const int HashLength = 16;

        /// <summary>
        /// Key for a repository, identified by its working directory. Case-insensitive, because
        /// Windows paths are.
        /// </summary>
        public static string ForRepository(string workingDirectory)
        {
            if (workingDirectory is null)
            {
                throw new ArgumentNullException(nameof(workingDirectory));
            }

            var normalized = workingDirectory
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

            var leaf = normalized.Length == 0
                ? "repo"
                : normalized.Substring(normalized.LastIndexOf(Path.DirectorySeparatorChar) + 1);

            if (leaf.Length == 0)
            {
                leaf = "repo";
            }

            // Both halves are lower-cased: the hash so that C:\Repo and c:\repo resolve to one
            // cache, and the readable prefix for the same reason — otherwise the same repository
            // reported with different casing would land in two directories despite matching hashes.
            var hash = ShortHash(normalized.ToLowerInvariant());
            return Sanitize(leaf).ToLowerInvariant() + "-" + hash;
        }

        /// <summary>
        /// Key for a HEAD. Case-<em>sensitive</em>, because git ref names are: <c>Feature</c> and
        /// <c>feature</c> are different branches and must not share a session file.
        /// </summary>
        public static string ForHead(string headKey)
        {
            if (headKey is null)
            {
                throw new ArgumentNullException(nameof(headKey));
            }

            var hash = ShortHash(headKey);
            return Sanitize(headKey) + "-" + hash;
        }

        private static string Sanitize(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);

            foreach (var c in value)
            {
                var isInvalid = Array.IndexOf(invalid, c) >= 0
                    || c == Path.DirectorySeparatorChar
                    || c == Path.AltDirectorySeparatorChar
                    || char.IsControl(c);

                builder.Append(isInvalid ? '_' : c);
            }

            var result = builder.ToString().Trim('.', ' ');
            if (result.Length > MaxPrefixLength)
            {
                result = result.Substring(0, MaxPrefixLength);
            }

            // Trailing dots and spaces are stripped by Windows; re-trim after truncation so the
            // name we build is the name that lands on disk.
            result = result.TrimEnd('.', ' ');

            return result.Length == 0 ? "_" : result;
        }

        private static string ShortHash(string value)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value));

            var builder = new StringBuilder(HashLength);
            for (var i = 0; i < HashLength / 2; i++)
            {
                builder.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }
    }
}
