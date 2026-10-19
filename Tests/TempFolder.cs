using System;
using System.IO;

namespace BPGPUpdater.Tests
{
    /// <summary>A fresh folder under the temp folder, removed again after the test</summary>
    internal sealed class TempFolder : IDisposable
    {
        public string Path { get; private set; }

        public TempFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BPGPUpdater.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        /// <summary>Creates a file (and its folders) below this folder; returns its full path</summary>
        public string File(string relativePath, string content)
        {
            var full = Combine(relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full));
            System.IO.File.WriteAllText(full, content);
            return full;
        }

        public string Folder(string relativePath)
        {
            var full = Combine(relativePath);
            Directory.CreateDirectory(full);
            return full;
        }

        /// <summary>Backslashes in test paths work on every OS</summary>
        public string Combine(string relativePath)
        {
            return System.IO.Path.Combine(Path, relativePath.Replace('\\', System.IO.Path.DirectorySeparatorChar));
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
