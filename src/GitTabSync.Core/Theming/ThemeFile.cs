using System;
using System.IO;
using System.Xml;
using GitTabSync.Storage;

namespace GitTabSync.Theming
{
    /// <summary>
    /// The little that Core reads out of a theme file: the name it calls itself.
    /// </summary>
    /// <remarks>
    /// A theme file is WPF markup, but it is also XML, and a name is the one thing a picker needs
    /// before anything has been loaded. Reading it with <see cref="XmlReader"/> keeps the theme
    /// list — including a list containing a theme that will turn out not to parse as WPF — testable
    /// without Visual Studio.
    /// </remarks>
    internal static class ThemeFile
    {
        /// <summary>The resource key a theme uses to name itself.</summary>
        internal const string DisplayNameKey = "GtsThemeName";

        private const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

        /// <summary>
        /// The theme's own name, or <c>null</c> when it does not give one — including when the
        /// file is not readable as XML at all, which is not this method's problem to report.
        /// </summary>
        internal static string? ReadDisplayName(string path)
        {
            try
            {
                var settings = new XmlReaderSettings
                {
                    // A theme is a file a user can edit or download. Neither entity expansion nor
                    // a DTD fetch has any business running because somebody opened a settings
                    // window.
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    IgnoreComments = true,
                    IgnoreWhitespace = true,
                };

                using var stream = File.OpenRead(path);
                using var reader = XmlReader.Create(stream, settings);

                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element)
                    {
                        continue;
                    }

                    var key = reader.GetAttribute("Key", XamlNamespace);
                    if (!string.Equals(key, DisplayNameKey, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (reader.IsEmptyElement)
                    {
                        return null;
                    }

                    var name = reader.ReadElementContentAsString().Trim();
                    return name.Length == 0 ? null : name;
                }

                return null;
            }
            catch (Exception e) when (RecoverableStorageFailure.Matches(e) || e is InvalidOperationException)
            {
                // Malformed markup, a locked file, a name element with children. The theme still
                // appears in the list under its file name; whether it renders is decided when the
                // shell tries to load it, which is where that failure belongs.
                return null;
            }
        }
    }
}
