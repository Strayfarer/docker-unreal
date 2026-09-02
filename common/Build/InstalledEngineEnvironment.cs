using System;
using System.Collections.Generic;
using System.IO;

namespace Unreal;

static class InstalledEngineEnvironment {
    public static IReadOnlyDictionary<string, string> Create(InstalledEngine engine, IReadOnlyDictionary<string, string> environment) {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var variable in environment) {
            result[variable.Key] = variable.Value;
        }

        string dotnet = FindBundledDotnetDirectory(engine.Root);
        string inheritedPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        result["PATH"] = string.IsNullOrEmpty(inheritedPath) ? dotnet : dotnet + Path.PathSeparator + inheritedPath;
        result["DOTNET_ROOT"] = dotnet;
        result["DOTNET_MULTILEVEL_LOOKUP"] = "0";
        result["DOTNET_ROLL_FORWARD"] = "LatestMajor";
        return result;
    }

    public static string FindBundledDotnetDirectory(string engineRoot) {
        string root = Path.Combine(engineRoot, "Engine", "Binaries", "ThirdParty", "DotNet");
        if (!Directory.Exists(root)) {
            throw MissingDotnet(root);
        }

        string? legacy = null;
        foreach (string executable in Directory.EnumerateFiles(root, "dotnet.exe", SearchOption.AllDirectories)) {
            string directory = Path.GetDirectoryName(executable)!;
            string architecture = Path.GetFileName(directory);
            if (architecture.Equals("win-x64", StringComparison.OrdinalIgnoreCase)) {
                return directory;
            }
            if (architecture.Equals("Windows", StringComparison.OrdinalIgnoreCase)) {
                legacy = directory;
            }
        }

        return legacy ?? throw MissingDotnet(root);
    }

    static InvalidOperationException MissingDotnet(string root) => new("Installed Build bundled x64 dotnet.exe is missing under: " + root);
}
