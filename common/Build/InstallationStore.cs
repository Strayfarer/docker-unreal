using System;
using System.IO;
using System.Text.Json;

namespace Unreal;

sealed class InstallationStore {
    const string INSTALLATION_MARKER = ".docker-unreal.json";
    const string REQUEST_MARKER = ".docker-unreal-request.json";

    readonly string _root;

    public InstallationStore(string root) => _root = root;

    public string LockPath => Path.Combine(_root, ".docker-unreal.lock");

    public bool TryGet(InstallationMarker request, out string buildBatch) {
        string target = TargetDirectory(request.Version);
        buildBatch = BuildBatch(target);
        string markerPath = Path.Combine(target, INSTALLATION_MARKER);
        if (!File.Exists(markerPath) || !File.Exists(buildBatch)) {
            return false;
        }

        try {
            var marker = JsonSerializer.Deserialize<InstallationMarker>(File.ReadAllText(markerPath));
            if (marker is null
                || marker.Version != request.Version
                || marker.Source != request.Source
                || marker.Commit != request.Commit) {
                return false;
            }

            var version = BuildVersion.Read(target);
            return version.FullVersion == marker.PatchVersion;
        } catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException) {
            return false;
        }
    }

    public string PrepareBuildDirectory(InstallationMarker request) {
        string installingRoot = Path.Combine(_root, ".installing");
        string buildDirectory = Path.Combine(installingRoot, request.Version);
        string requestPath = Path.Combine(buildDirectory, REQUEST_MARKER);
        if (Directory.Exists(buildDirectory) && !MarkerMatches(requestPath, request)) {
            ManagedDirectory.DeleteIfPresent(buildDirectory, installingRoot);
        }

        Directory.CreateDirectory(buildDirectory);
        WriteJsonAtomically(requestPath, request);
        return buildDirectory;
    }

    public string Publish(string buildDirectory, string installedRoot, InstallationMarker marker) {
        string expectedInstalledRoot = Path.Combine(buildDirectory, "Windows");
        if (!Path.GetFullPath(installedRoot).Equals(Path.GetFullPath(expectedInstalledRoot), PathComparison())) {
            throw new IOException("installed engine is outside its managed build directory: " + installedRoot);
        }

        string buildBatch = BuildBatch(installedRoot);
        if (!File.Exists(buildBatch)) {
            throw new InvalidOperationException("installed Unreal Engine Build.bat is missing: " + buildBatch);
        }

        WriteJsonAtomically(Path.Combine(installedRoot, INSTALLATION_MARKER), marker);
        string target = TargetDirectory(marker.Version);
        string replacedRoot = Path.Combine(_root, ".replaced");
        string replaced = Path.Combine(replacedRoot, marker.Version + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(replacedRoot);
        bool movedPrevious = false;
        try {
            if (Directory.Exists(target)) {
                Directory.Move(target, replaced);
                movedPrevious = true;
            }
            Directory.Move(installedRoot, target);
        } catch {
            if (movedPrevious && !Directory.Exists(target) && Directory.Exists(replaced)) {
                Directory.Move(replaced, target);
            }
            throw;
        }

        ManagedDirectory.DeleteIfPresent(replaced, replacedRoot);
        ManagedDirectory.DeleteIfPresent(buildDirectory, Path.Combine(_root, ".installing"));
        return BuildBatch(target);
    }

    string TargetDirectory(string version) => Path.Combine(_root, version);

    static string BuildBatch(string engineRoot) => Path.Combine(engineRoot, "Engine", "Build", "BatchFiles", "Build.bat");

    static bool MarkerMatches(string path, InstallationMarker expected) {
        if (!File.Exists(path)) {
            return false;
        }

        try {
            return JsonSerializer.Deserialize<InstallationMarker>(File.ReadAllText(path)) == expected;
        } catch (Exception exception) when (exception is IOException or JsonException) {
            return false;
        }
    }

    static void WriteJsonAtomically(string path, InstallationMarker marker) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N");
        try {
            File.WriteAllText(temporary, JsonSerializer.Serialize(marker));
            File.Move(temporary, path, true);
        } finally {
            File.Delete(temporary);
        }
    }

    static StringComparison PathComparison() => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
