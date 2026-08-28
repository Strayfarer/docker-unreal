using System;
using System.IO;
using System.Text.Json;

namespace Unreal;

sealed class BuildVersion {
    public int MajorVersion { get; set; }
    public int MinorVersion { get; set; }
    public int PatchVersion { get; set; }

    public string FullVersion => $"{MajorVersion}.{MinorVersion}.{PatchVersion}";

    public static BuildVersion Read(string engineRoot) {
        string path = Path.Combine(engineRoot, "Engine", "Build", "Build.version");
        if (!File.Exists(path)) {
            throw new InvalidOperationException("Unreal Engine build version is missing: " + path);
        }

        return JsonSerializer.Deserialize<BuildVersion>(File.ReadAllText(path))
               ?? throw new InvalidOperationException("Unreal Engine build version is invalid: " + path);
    }

    public void AssertMatches(UnrealVersion requested) {
        if (!requested.Matches(this)) {
            throw new InvalidOperationException("Unreal Engine branch " + requested + " contains version " + FullVersion);
        }
    }
}
