using System;
using System.IO;
using IOPath = System.IO.Path;

namespace GitTabSync.Tests.TestSupport
{
    /// <summary>
    /// A scratch directory that deletes itself. Several of these tests exercise real filesystem
    /// behaviour (locking, renames, path casing) that a mocked filesystem would not reproduce.
    /// </summary>
    public sealed class TempDirectory : IDisposable
    {
        private readonly string _path;

        public TempDirectory(string prefix = "gts")
        {
            // GetFullPath normalises the temp root so path comparisons in tests match what the
            // production code computes.
            var root = IOPath.GetFullPath(IOPath.GetTempPath());
            _path = IOPath.Combine(root, prefix + "-" + Guid.NewGuid().ToString("N").Substring(0, 12));
            Directory.CreateDirectory(_path);
        }

        public string Path => _path;

        public string Combine(params string[] parts)
        {
            var result = _path;
            foreach (var part in parts)
            {
                result = IOPath.Combine(result, part);
            }

            return result;
        }

        public string CreateFile(string relativePath, string content = "")
        {
            var full = Combine(relativePath.Split('/'));
            var directory = IOPath.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(full, content);
            return full;
        }

        public string CreateDirectory(string relativePath)
        {
            var full = Combine(relativePath.Split('/'));
            Directory.CreateDirectory(full);
            return full;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_path))
                {
                    Directory.Delete(_path, recursive: true);
                }
            }
            catch (IOException)
            {
                // A watcher may still hold a handle; leaving a temp directory behind is not
                // worth failing a test over.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
