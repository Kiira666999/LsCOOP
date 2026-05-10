using System;
using System.IO;

namespace LsrCoop.Server
{
    internal static class SafeJsonFileStore
    {
        public static string BackupPath(string path)
        {
            return path + ".bak";
        }

        public static void WriteAllText(string path, string content)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            string backupPath = BackupPath(path);

            File.WriteAllText(tempPath, content);
            try
            {
                if (File.Exists(path))
                {
                    TryReplace(tempPath, path, backupPath);
                }
                else
                {
                    File.Move(tempPath, path);
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        private static void TryReplace(string tempPath, string targetPath, string backupPath)
        {
            try
            {
                File.Replace(tempPath, targetPath, backupPath, true);
            }
            catch
            {
                File.Copy(targetPath, backupPath, true);
                File.Delete(targetPath);
                File.Move(tempPath, targetPath);
            }
        }
    }
}
