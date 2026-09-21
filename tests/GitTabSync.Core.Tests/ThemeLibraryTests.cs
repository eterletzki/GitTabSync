using System;
using System.IO;
using System.Linq;
using GitTabSync.Settings;
using GitTabSync.Tests.TestSupport;
using GitTabSync.Theming;
using Xunit;

namespace GitTabSync.Tests
{
    /// <summary>
    /// Themes are files a user is invited to add, edit and delete, so every test here uses a real
    /// directory: the behaviour that matters — a file appearing, a file being edited in place, a
    /// file that is not valid XML — is exactly what a mocked filesystem would not reproduce.
    /// </summary>
    public sealed class ThemeLibraryTests
    {
        private const string Xmlns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        private const string XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";
        private const string SystemNs = "clr-namespace:System;assembly=mscorlib";

        private static string Theme(string? name = null) =>
            "<ResourceDictionary xmlns=\"" + Xmlns + "\" xmlns:x=\"" + XamlNs + "\" xmlns:sys=\"" + SystemNs + "\">"
            + (name is null ? string.Empty : "<sys:String x:Key=\"GtsThemeName\">" + name + "</sys:String>")
            + "</ResourceDictionary>";

        private static BuiltInTheme[] BuiltIns() =>
            new[]
            {
                new BuiltInTheme("First", "First theme", Theme("First theme")),
                new BuiltInTheme("Second", "Second theme", Theme("Second theme")),
            };

        private static ThemeLibrary Create(TempDirectory temp, InMemorySettingsStore? store = null) =>
            new ThemeLibrary(store ?? new InMemorySettingsStore(), BuiltIns(), temp.Path);

        private static string ThemesPath(TempDirectory temp, string fileName) =>
            Path.Combine(temp.Path, ThemeLibrary.DirectoryName, fileName);

        [Fact]
        public void Writes_the_built_in_themes_where_a_user_can_find_them()
        {
            using var temp = new TempDirectory();

            var library = Create(temp);

            Assert.True(File.Exists(ThemesPath(temp, "First.xaml")));
            Assert.True(File.Exists(ThemesPath(temp, "Second.xaml")));
            Assert.Equal(new[] { "First", "Second" }, library.Themes.Select(theme => theme.Id));
        }

        /// <summary>
        /// The folder is the documentation. Somebody who opens it and finds two files and nothing
        /// explaining them has to come back here to learn that a third file would be picked up.
        /// </summary>
        [Fact]
        public void Explains_the_folder_in_the_folder()
        {
            using var temp = new TempDirectory();

            Create(temp);

            var readMe = File.ReadAllText(ThemesPath(temp, "README.txt"));
            Assert.Contains(ThemeLibrary.FileExtension, readMe, StringComparison.Ordinal);
        }

        /// <summary>
        /// The whole point of writing themes to disk is that they can be edited. Re-seeding over an
        /// edit would throw that away on the next restart, which is worse than shipping an
        /// improvement nobody asked for.
        /// </summary>
        [Fact]
        public void Never_overwrites_a_theme_that_is_already_there()
        {
            using var temp = new TempDirectory();
            Create(temp);

            File.WriteAllText(ThemesPath(temp, "First.xaml"), Theme("Mine now"));
            var library = Create(temp);

            Assert.Equal("Mine now", library.Find("First")!.DisplayName);
        }

        [Fact]
        public void Restores_a_built_in_theme_that_was_deleted()
        {
            using var temp = new TempDirectory();
            var library = Create(temp);

            File.Delete(ThemesPath(temp, "First.xaml"));
            library.Reload();

            Assert.True(File.Exists(ThemesPath(temp, "First.xaml")));
        }

        [Fact]
        public void Picks_up_a_theme_dropped_into_the_folder()
        {
            using var temp = new TempDirectory();
            var library = Create(temp);

            File.WriteAllText(ThemesPath(temp, "Midnight.xaml"), Theme("Midnight"));

            // Not until it is asked, because that is what the window's Reload button is for.
            Assert.Null(library.Find("Midnight"));

            library.Reload();

            var added = library.Find("Midnight");
            Assert.NotNull(added);
            Assert.Equal("Midnight", added!.DisplayName);
            Assert.False(added.IsBuiltIn);
        }

        [Fact]
        public void A_theme_that_does_not_name_itself_is_listed_under_its_file_name()
        {
            using var temp = new TempDirectory();
            var library = Create(temp);

            File.WriteAllText(ThemesPath(temp, "Nameless.xaml"), Theme());
            library.Reload();

            Assert.Equal("Nameless", library.Find("Nameless")!.DisplayName);
        }

        /// <summary>
        /// Whether a theme <em>renders</em> is decided by the shell when it loads it, and reported
        /// there. Dropping it from the list here would leave a user who mistyped one character with
        /// a theme that had silently vanished instead of one that reports why.
        /// </summary>
        [Fact]
        public void A_theme_that_is_not_even_valid_xml_is_still_listed()
        {
            using var temp = new TempDirectory();
            var library = Create(temp);

            File.WriteAllText(ThemesPath(temp, "Broken.xaml"), "<ResourceDictionary");
            library.Reload();

            Assert.Equal("Broken", library.Find("Broken")!.DisplayName);
        }

        [Fact]
        public void Lists_the_built_ins_first_and_then_the_rest_by_name()
        {
            using var temp = new TempDirectory();
            var library = Create(temp);

            File.WriteAllText(ThemesPath(temp, "Zinc.xaml"), Theme("Zinc"));
            File.WriteAllText(ThemesPath(temp, "Amber.xaml"), Theme("Amber"));
            library.Reload();

            Assert.Equal(
                new[] { "First theme", "Second theme", "Amber", "Zinc" },
                library.Themes.Select(theme => theme.DisplayName));
        }

        [Fact]
        public void Applies_the_first_built_in_when_nothing_has_been_chosen()
        {
            using var temp = new TempDirectory();

            Assert.Equal("First", Create(temp).Current!.Id);
        }

        [Fact]
        public void Remembers_the_choice()
        {
            using var temp = new TempDirectory();
            var store = new InMemorySettingsStore();

            Create(temp, store).SelectedThemeId = "Second";

            Assert.Equal("Second", Create(temp, store).Current!.Id);
        }

        /// <summary>
        /// A theme file can be missing for a moment — the user is editing it, or it is on a drive
        /// that has not come back yet. Treating that as "they changed their mind" would silently
        /// reset a choice that was never withdrawn.
        /// </summary>
        [Fact]
        public void A_choice_whose_file_is_missing_falls_back_without_being_forgotten()
        {
            using var temp = new TempDirectory();
            var store = new InMemorySettingsStore();
            var library = Create(temp, store);

            library.SelectedThemeId = "Midnight";

            Assert.Equal("First", library.Current!.Id);
            Assert.Equal("Midnight", library.SelectedThemeId);

            File.WriteAllText(ThemesPath(temp, "Midnight.xaml"), Theme("Midnight"));
            library.Reload();

            Assert.Equal("Midnight", library.Current!.Id);
        }

        [Fact]
        public void Announces_a_change_of_choice()
        {
            using var temp = new TempDirectory();
            var library = Create(temp);
            var changes = 0;
            library.Changed += (sender, e) => changes++;

            library.SelectedThemeId = "Second";
            library.SelectedThemeId = "Second";

            Assert.Equal(1, changes);
        }

        /// <summary>
        /// An unwritable storage root is a bad day, not a crash: the extension still syncs tabs,
        /// and the window still opens with no theme applied.
        /// </summary>
        [Fact]
        public void Survives_a_themes_folder_it_cannot_write()
        {
            using var temp = new TempDirectory();

            // A file where the folder should be: creating the directory fails, and so does every
            // read of it.
            File.WriteAllText(Path.Combine(temp.Path, ThemeLibrary.DirectoryName), "not a folder");

            var library = Create(temp);

            Assert.Empty(library.Themes);
            Assert.Null(library.Current);
        }

        [Fact]
        public void The_view_model_selects_through_the_library()
        {
            using var temp = new TempDirectory();
            var store = new InMemorySettingsStore();
            var library = Create(temp, store);
            using var model = new ThemeSelectionViewModel(library);

            var applied = 0;
            model.ThemeChanged += (sender, e) => applied++;

            model.SelectedTheme = library.Find("Second");

            Assert.Equal(1, applied);
            Assert.Equal("Second", model.SelectedTheme!.Id);
            Assert.Equal("Second", store.LoadPreferences().ThemeId);
        }

        /// <summary>
        /// Reloading has to re-dress the window even though the selection did not move: a theme
        /// file edited in place is a different theme under the same name, and picking it again is
        /// not something the user can do.
        /// </summary>
        [Fact]
        public void The_view_model_announces_a_reload_as_a_theme_change()
        {
            using var temp = new TempDirectory();
            using var model = new ThemeSelectionViewModel(Create(temp));

            var applied = 0;
            model.ThemeChanged += (sender, e) => applied++;

            model.Reload();

            Assert.Equal(1, applied);
        }

        /// <summary>
        /// The library is one per process and the window is not, so a window that forgot to let go
        /// would be re-dressed for the rest of the session — and so would every window before it.
        /// </summary>
        [Fact]
        public void A_disposed_view_model_stops_listening()
        {
            using var temp = new TempDirectory();
            var library = Create(temp);
            var model = new ThemeSelectionViewModel(library);

            var applied = 0;
            model.ThemeChanged += (sender, e) => applied++;
            model.Dispose();

            library.SelectedThemeId = "Second";

            Assert.Equal(0, applied);
        }
    }
}
