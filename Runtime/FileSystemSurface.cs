using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyFrame.Runtime
{
    /// <summary>
    /// Inner implementation of local filesystem access exposed to mods as <c>jf.fs</c>.
    /// Requires the <c>filesystem</c> permission.
    /// </summary>
    public class FileSystemSurface
    {
        private readonly ILogger _logger;
        private readonly string _modId;

        public FileSystemSurface(string modId, ILogger logger)
        {
            _modId = modId;
            _logger = logger;
        }

        /// <summary>Read the entire contents of a file as a UTF-8 string.</summary>
        public string ReadFile(string path)
        {
            ValidatePath(path);
            return File.ReadAllText(path);
        }

        /// <summary>Read a file as a Base64-encoded string (useful for binary data).</summary>
        public string ReadFileBase64(string path)
        {
            ValidatePath(path);
            return Convert.ToBase64String(File.ReadAllBytes(path));
        }

        /// <summary>Write (overwrite) a UTF-8 string to a file.</summary>
        public void WriteFile(string path, string content)
        {
            ValidatePath(path);
            File.WriteAllText(path, content);
        }

        /// <summary>Append a UTF-8 string to a file, creating it if it doesn't exist.</summary>
        public void AppendFile(string path, string content)
        {
            ValidatePath(path);
            File.AppendAllText(path, content);
        }

        /// <summary>Write raw bytes supplied as a Base64-encoded string.</summary>
        public void WriteFileBase64(string path, string base64)
        {
            ValidatePath(path);
            File.WriteAllBytes(path, Convert.FromBase64String(base64));
        }

        /// <summary>Delete a file. Returns false if the file did not exist.</summary>
        public bool DeleteFile(string path)
        {
            ValidatePath(path);
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }

        /// <summary>Move / rename a file.</summary>
        public void MoveFile(string source, string dest)
        {
            ValidatePath(source);
            ValidatePath(dest);
            File.Move(source, dest, overwrite: true);
        }

        /// <summary>Copy a file.</summary>
        public void CopyFile(string source, string dest)
        {
            ValidatePath(source);
            ValidatePath(dest);
            File.Copy(source, dest, overwrite: true);
        }

        /// <summary>
        /// List the entries in a directory.
        /// Returns an array of objects with <c>name</c>, <c>path</c>, and <c>type</c>
        /// ("file" | "directory").
        /// </summary>
        public object[] ListDir(string path)
        {
            ValidatePath(path);
            var entries = Directory.GetFileSystemEntries(path);
            return entries.Select(e =>
            {
                bool isDir = Directory.Exists(e);
                return (object)new
                {
                    name = Path.GetFileName(e),
                    path = e,
                    type = isDir ? "directory" : "file"
                };
            }).ToArray();
        }

        /// <summary>Create a directory (and any missing parent directories).</summary>
        public void MakeDir(string path)
        {
            ValidatePath(path);
            Directory.CreateDirectory(path);
        }

        /// <summary>Delete a directory. Pass <c>recursive = true</c> to delete contents.</summary>
        public bool DeleteDir(string path, bool recursive = false)
        {
            ValidatePath(path);
            if (!Directory.Exists(path)) return false;
            Directory.Delete(path, recursive);
            return true;
        }

        /// <summary>Returns true if the path exists (file or directory).</summary>
        public bool Exists(string path)
        {
            ValidatePath(path);
            return File.Exists(path) || Directory.Exists(path);
        }

        /// <summary>Returns true if the path is an existing file.</summary>
        public bool IsFile(string path)
        {
            ValidatePath(path);
            return File.Exists(path);
        }

        /// <summary>Returns true if the path is an existing directory.</summary>
        public bool IsDir(string path)
        {
            ValidatePath(path);
            return Directory.Exists(path);
        }

        /// <summary>
        /// Returns an object with metadata about the file or directory:
        /// <c>size</c>, <c>createdAt</c>, <c>modifiedAt</c>, <c>type</c>.
        /// Returns <c>null</c> if the path does not exist.
        /// </summary>
        public object Stat(string path)
        {
            ValidatePath(path);

            if (File.Exists(path))
            {
                var fi = new FileInfo(path);
                return new
                {
                    type = "file",
                    size = fi.Length,
                    createdAt = fi.CreationTimeUtc.ToString("o"),
                    modifiedAt = fi.LastWriteTimeUtc.ToString("o"),
                    name = fi.Name,
                    directory = fi.DirectoryName
                };
            }

            if (Directory.Exists(path))
            {
                var di = new DirectoryInfo(path);
                return new
                {
                    type = "directory",
                    size = (long)0,
                    createdAt = di.CreationTimeUtc.ToString("o"),
                    modifiedAt = di.LastWriteTimeUtc.ToString("o"),
                    name = di.Name,
                    directory = di.Parent?.FullName
                };
            }

            return null;
        }

        /// <summary>
        /// Resolve a path using <see cref="Path.GetFullPath"/>.
        /// Useful for normalising relative segments (e.g. "../") before passing to other calls.
        /// </summary>
        public string ResolvePath(string path) => Path.GetFullPath(path);

        /// <summary>Join path segments using the OS directory separator.</summary>
        public string JoinPath(string a, string b) => Path.Combine(a, b);

        private static void ValidatePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path must not be null or empty.");

            if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                throw new ArgumentException($"Path contains invalid characters: {path}");
        }
    }
}
