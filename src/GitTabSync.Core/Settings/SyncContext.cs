using System;
using System.Collections.Generic;
using GitTabSync.Sync;

namespace GitTabSync.Settings
{
    /// <summary>
    /// Where the extension is when it makes a decision: which repository, which branch, and
    /// optionally which solution and project. Turns that into the ordered list of scopes to ask.
    /// </summary>
    /// <remarks>
    /// The cascade lives here, as data, rather than as a chain of if-statements inside the
    /// resolver. Adding a level — a project scope that applies on every branch, say — is then one
    /// entry in <see cref="ScopeChain"/> and a test, instead of a rewrite of the lookup.
    /// </remarks>
    public sealed class SyncContext
    {
        /// <param name="headKey">
        /// <see cref="Git.GitHead.SessionKey"/>. Empty when HEAD could not be read, which drops the
        /// branch level out of the chain rather than inventing one.
        /// </param>
        /// <param name="solutionPath">Absolute path to the open solution, if any.</param>
        /// <param name="projectPath">Absolute path to the project in question, if any.</param>
        public SyncContext(
            string repositoryWorkingDirectory,
            string headKey,
            string? solutionPath = null,
            string? projectPath = null)
        {
            RepositoryWorkingDirectory = repositoryWorkingDirectory
                ?? throw new ArgumentNullException(nameof(repositoryWorkingDirectory));
            HeadKey = headKey ?? string.Empty;
            SolutionPath = solutionPath;
            ProjectPath = projectPath;

            ScopeChain = BuildChain();
        }

        public string RepositoryWorkingDirectory { get; }

        public string HeadKey { get; }

        public string? SolutionPath { get; }

        public string? ProjectPath { get; }

        /// <summary>
        /// The scopes to consult, narrowest first. The first one holding a value for a setting
        /// wins; if none does, the built-in default applies.
        /// </summary>
        public IReadOnlyList<SettingScope> ScopeChain { get; }

        private IReadOnlyList<SettingScope> BuildChain()
        {
            var chain = new List<SettingScope>(5);

            if (HeadKey.Length != 0)
            {
                if (!string.IsNullOrEmpty(ProjectPath))
                {
                    chain.Add(SettingScope.Project(HeadKey, Store(ProjectPath!)));
                }

                if (!string.IsNullOrEmpty(SolutionPath))
                {
                    chain.Add(SettingScope.Solution(HeadKey, Store(SolutionPath!)));
                }

                chain.Add(SettingScope.Branch(HeadKey));
            }

            chain.Add(SettingScope.Repository);
            chain.Add(SettingScope.Global);

            return chain;
        }

        /// <summary>
        /// Repository-relative with '/' separators where the file is inside the repository,
        /// absolute where it is not — the same rule <see cref="TabSessionMapper"/> applies to tabs.
        /// </summary>
        private string Store(string absolutePath) =>
            TabSessionMapper.TryMakeRepositoryRelative(absolutePath, RepositoryWorkingDirectory, out var relative)
                ? relative
                : absolutePath;
    }
}
