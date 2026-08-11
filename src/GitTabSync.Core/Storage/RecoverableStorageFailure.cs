using System;
using System.IO;

namespace GitTabSync.Storage
{
    /// <summary>
    /// The failures the stores swallow instead of throwing.
    /// </summary>
    /// <remarks>
    /// Losing a remembered tab set — or a settings override — is recoverable; failing a branch
    /// switch is not. Kept in one place because sessions and settings have to make the same
    /// judgement, and two copies of a policy like this drift.
    /// </remarks>
    internal static class RecoverableStorageFailure
    {
        public static bool Matches(Exception e)
        {
            return e is IOException
                || e is UnauthorizedAccessException
                || e is System.Runtime.Serialization.SerializationException
                || e is System.Xml.XmlException
                || e is ArgumentException
                || e is NotSupportedException;
        }
    }
}
