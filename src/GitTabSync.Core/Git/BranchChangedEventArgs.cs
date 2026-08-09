using System;

namespace GitTabSync.Git
{
    public sealed class BranchChangedEventArgs : EventArgs
    {
        public BranchChangedEventArgs(GitHead? previous, GitHead current)
        {
            Previous = previous;
            Current = current;
        }

        /// <summary>
        /// The HEAD that was current until now, or <c>null</c> if there was no known previous
        /// state (first observation, or HEAD was unreadable). When it is <c>null</c> there is no
        /// branch to attribute the currently open tabs to, so they must not be saved anywhere.
        /// </summary>
        public GitHead? Previous { get; }

        public GitHead Current { get; }
    }
}
