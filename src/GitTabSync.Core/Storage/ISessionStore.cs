using GitTabSync.Model;

namespace GitTabSync.Storage
{
    /// <summary>
    /// Persists tab sessions per (repository, HEAD).
    /// </summary>
    public interface ISessionStore
    {
        /// <summary>
        /// Returns the stored session, or <c>null</c> when none exists or the stored data is
        /// unreadable. A missing session is normal — it just means this branch has not been
        /// visited since the extension was installed.
        /// </summary>
        TabSession? Load(string repositoryWorkingDirectory, string headKey);

        void Save(string repositoryWorkingDirectory, TabSession session);

        /// <summary>Removes a stored session. No-op when it does not exist.</summary>
        void Delete(string repositoryWorkingDirectory, string headKey);
    }
}
