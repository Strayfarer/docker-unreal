using System;
using System.IO;

namespace Unreal;

static class ManagedDirectory {
    public static void DeleteIfPresent(string path, string managedRoot) {
        if (!Directory.Exists(path)) {
            return;
        }

        string root = Path.GetFullPath(managedRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string candidate = Path.GetFullPath(path);
        if (!candidate.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) {
            throw new IOException("refusing to remove directory outside its managed root: " + candidate);
        }

        Directory.Delete(candidate, true);
    }
}
