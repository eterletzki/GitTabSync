using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Markup;
using GitTabSync.Settings;
using GitTabSync.Sync;
using GitTabSync.Theming;

namespace GitTabSync
{
    /// <summary>
    /// The shell half of theming: turns a theme file into WPF resources and dresses a control in
    /// them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything that can be decided without WPF — where themes live, which ones exist, what they
    /// are called, which one the user picked — is in <see cref="ThemeLibrary"/> in Core, and
    /// tested. What is left here is the one thing only this side can do: parse the markup.
    /// </para>
    /// <para>
    /// One per process rather than one per session, because a theme is the user's and is answerable
    /// with no solution open. The settings window can be constructed before the package has
    /// finished initialising, so this cannot depend on the package existing.
    /// </para>
    /// </remarks>
    internal sealed class ThemeHost
    {
        /// <summary>The theme with no Visual Studio dependencies, used as the last resort.</summary>
        private const string FallbackThemeId = "Plain";

        /// <summary>Matches the LogicalName the theme files are embedded under.</summary>
        private const string ResourcePrefix = "GitTabSync.Theming.";

        private static readonly object Gate = new object();

        private static ThemeHost? _instance;

        private ThemeHost()
        {
            Library = new ThemeLibrary(new FileSettingsStore(), ReadBuiltIns(), log: new ForwardingLog(this));
        }

        public static ThemeHost Instance
        {
            get
            {
                lock (Gate)
                {
                    return _instance ??= new ThemeHost();
                }
            }
        }

        /// <summary>
        /// Where themes come from. Assigned once and shared by every window that opens.
        /// </summary>
        public ThemeLibrary Library { get; }

        /// <summary>
        /// The output pane, once the package has one. Until then this is the null log: a theme
        /// loaded while Visual Studio is still starting has nowhere to report to, and a settings
        /// window is not worth blocking on a log.
        /// </summary>
        public ITabSyncLog Log { get; set; } = NullTabSyncLog.Instance;

        /// <summary>
        /// Replaces <paramref name="element"/>'s merged resources with the chosen theme's.
        /// </summary>
        /// <remarks>
        /// Only the merged dictionaries are replaced, so resources the control declares itself —
        /// its converter — survive a theme change. Everything the window styles is looked up with
        /// <c>DynamicResource</c>, which is what lets this be called again on a live window: the
        /// bindings re-resolve, and a key the new theme does not define falls back to the control's
        /// own default rather than keeping the old theme's value.
        /// </remarks>
        public void ApplyTo(FrameworkElement element)
        {
            if (element is null)
            {
                throw new ArgumentNullException(nameof(element));
            }

            var dictionary = LoadCurrent();

            element.Resources.MergedDictionaries.Clear();
            element.Resources.MergedDictionaries.Add(dictionary);
        }

        /// <summary>
        /// The chosen theme's resources, or the nearest thing that loads.
        /// </summary>
        /// <remarks>
        /// A theme is a file a user can edit, so "it does not parse" is an ordinary state, not an
        /// exceptional one. Falling back through the default to the theme that has no Visual Studio
        /// dependencies, and finally to nothing at all, means the worst a broken theme can do is
        /// make the window look wrong — never fail to open. Which one was used, and why, goes to
        /// the output pane.
        /// </remarks>
        private ResourceDictionary LoadCurrent()
        {
            var current = Library.Current;

            foreach (var candidate in Candidates(current))
            {
                var dictionary = TryLoad(candidate);
                if (dictionary is null)
                {
                    continue;
                }

                if (current is not null && !ReferenceEquals(candidate, current))
                {
                    Log.Info("Theme " + current.Id + " could not be loaded; using " + candidate.Id + " instead.");
                }

                return dictionary;
            }

            if (current is not null)
            {
                Log.Info("No theme could be loaded; the settings window will use unstyled controls.");
            }

            return new ResourceDictionary();
        }

        private IEnumerable<ThemeInfo> Candidates(ThemeInfo? current)
        {
            if (current is not null)
            {
                yield return current;
            }

            foreach (var id in new[] { Library.DefaultThemeId, FallbackThemeId })
            {
                var theme = Library.Find(id);
                if (theme is not null && !ReferenceEquals(theme, current))
                {
                    yield return theme;
                }
            }
        }

        private ResourceDictionary? TryLoad(ThemeInfo theme)
        {
            try
            {
                // The base URI is what lets one theme merge another by file name: Compact.xaml is
                // VisualStudio.xaml plus a handful of overrides, and a user's theme can do the
                // same. Without it a relative Source in a theme has nothing to resolve against.
                var context = new ParserContext { BaseUri = new Uri(theme.FilePath) };

                using var stream = File.OpenRead(theme.FilePath);
                return XamlReader.Load(stream, context) as ResourceDictionary;
            }
            catch (Exception e)
            {
                // Deliberately broad. This is markup the user is invited to edit, and the parser
                // reports the many ways that can go wrong with as many exception types. None of
                // them is worth a failed window.
                Log.Error("Failed to load the theme at " + theme.FilePath + ".", e);
                return null;
            }
        }

        /// <summary>
        /// The themes this build ships, in the order they are offered. The first is the default;
        /// <see cref="FallbackThemeId"/> is last because it is the one with no Visual Studio
        /// dependencies, so it is the one most likely to load when another did not.
        /// </summary>
        private static IReadOnlyList<BuiltInTheme> ReadBuiltIns()
        {
            var themes = new List<BuiltInTheme>();

            foreach (var (id, displayName) in new[]
            {
                ("VisualStudio", "Visual Studio"),
                ("Compact", "Compact"),
                (FallbackThemeId, "Plain"),
            })
            {
                var content = ReadResource(id);
                if (content is not null)
                {
                    themes.Add(new BuiltInTheme(id, displayName, content));
                }
            }

            return themes;
        }

        /// <summary>
        /// The theme file as it was compiled into this assembly, which is the copy written to the
        /// themes folder when none is there.
        /// </summary>
        private static string? ReadResource(string id)
        {
            var assembly = typeof(ThemeHost).Assembly;
            using var stream = assembly.GetManifestResourceStream(
                ResourcePrefix + id + ThemeLibrary.FileExtension);

            if (stream is null)
            {
                return null;
            }

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        /// <summary>
        /// Lets the library log through whatever <see cref="Log"/> happens to be by the time it
        /// writes, rather than through whatever it was when the library was built — which, for a
        /// library built during startup, is nothing.
        /// </summary>
        private sealed class ForwardingLog : ITabSyncLog
        {
            private readonly ThemeHost _host;

            public ForwardingLog(ThemeHost host) => _host = host;

            public void Info(string message) => _host.Log.Info(message);

            public void Error(string message, Exception? exception) => _host.Log.Error(message, exception);
        }
    }
}
