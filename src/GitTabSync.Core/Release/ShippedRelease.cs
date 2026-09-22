using System;
using System.IO;
using System.Reflection;

namespace GitTabSync.Release
{
    /// <summary>
    /// What this build is, and what its changelog says — both read from the assembly itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The changelog is an embedded resource rather than a file beside the extension, because an
    /// installed VSIX is unpacked somewhere the extension has no business going looking, and a
    /// file that can go missing would turn "what changed" into "nothing, apparently".
    /// </para>
    /// <para>
    /// The version comes from the assembly, which gets it from <c>Directory.Build.props</c>, which
    /// the VSIX project checks against the manifest at build time. That chain is what stops the
    /// page announcing a version nobody installed.
    /// </para>
    /// </remarks>
    public static class ShippedRelease
    {
        /// <summary>Matches the LogicalName the changelog is embedded under.</summary>
        internal const string ResourceName = "GitTabSync.CHANGELOG.md";

        private static readonly Lazy<ReleaseNotes> LazyNotes =
            new Lazy<ReleaseNotes>(() => ChangelogParser.Parse(ReadChangelog()));

        private static readonly Lazy<string> LazyVersion = new Lazy<string>(ReadVersion);

        /// <summary>
        /// The version of the running build, as text. Empty only if the assembly carries no
        /// version at all, which every caller treats as "do not announce anything".
        /// </summary>
        public static string Version => LazyVersion.Value;

        /// <summary>
        /// The parsed changelog. Parsed once: it is a fixed resource, and the window can be opened
        /// repeatedly.
        /// </summary>
        public static ReleaseNotes Notes => LazyNotes.Value;

        /// <summary>The changelog as it was compiled in, or null if it is not there.</summary>
        internal static string? ReadChangelog()
        {
            using var stream = typeof(ShippedRelease).Assembly.GetManifestResourceStream(ResourceName);
            if (stream is null)
            {
                return null;
            }

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private static string ReadVersion()
        {
            var assembly = typeof(ShippedRelease).Assembly;

            // The informational version is the one that carries "1.1.0" rather than "1.1.0.0",
            // which is what the changelog's headings are written as.
            var informational = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informational))
            {
                // Some SDK configurations append "+<commit>" to the informational version. That is
                // build metadata, not part of what was released, and this string is shown to the
                // user and written to their state file.
                var trimmed = informational!.Trim();
                var metadata = trimmed.IndexOf('+');
                return metadata < 0 ? trimmed : trimmed.Substring(0, metadata);
            }

            return assembly.GetName().Version?.ToString() ?? string.Empty;
        }
    }
}
