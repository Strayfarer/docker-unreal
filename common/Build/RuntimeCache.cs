using System;
using System.Collections.Generic;
using System.IO;

namespace Unreal;

sealed class RuntimeCache {
    public string Root { get; }
    public string DerivedData { get; }
    public string GitDependencies { get; }
    public string Uba { get; }
    public string NuGetPackages { get; }
    public string NuGetHttp { get; }
    public string NuGetPlugins { get; }
    public string NuGetScratch { get; }
    public string DotNetHome { get; }

    public IReadOnlyDictionary<string, string> Environment { get; }

    public RuntimeCache(string root, UnrealVersion version, string? remoteDdc = null) {
        Root = root;
        DerivedData = Path.Combine(root, "ddc");
        GitDependencies = Path.Combine(root, "gitdeps");
        Uba = Path.Combine(root, "uba", version.ToString());
        NuGetPackages = Path.Combine(root, "nuget", "packages");
        NuGetHttp = Path.Combine(root, "nuget", "http");
        NuGetPlugins = Path.Combine(root, "nuget", "plugins");
        NuGetScratch = Path.Combine(root, "nuget", "scratch");
        DotNetHome = Path.Combine(root, "dotnet");
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["UE-LocalDataCachePath"] = DerivedData,
            ["UBA_ROOT"] = Uba,
            ["NUGET_PACKAGES"] = NuGetPackages,
            ["NUGET_HTTP_CACHE_PATH"] = NuGetHttp,
            ["NUGET_PLUGINS_CACHE_PATH"] = NuGetPlugins,
            ["NUGET_SCRATCH"] = NuGetScratch,
            ["NuGetAudit"] = "false",
            ["DOTNET_CLI_HOME"] = DotNetHome
        };
        if (!string.IsNullOrWhiteSpace(remoteDdc)) {
            environment["UE-ZenSharedDataCacheHost"] = remoteDdc.Trim();
        }
        Environment = environment;
    }

    public void Prepare() {
        Directory.CreateDirectory(DerivedData);
        Directory.CreateDirectory(GitDependencies);
        Directory.CreateDirectory(Uba);
        Directory.CreateDirectory(NuGetPackages);
        Directory.CreateDirectory(NuGetHttp);
        Directory.CreateDirectory(NuGetPlugins);
        Directory.CreateDirectory(NuGetScratch);
        Directory.CreateDirectory(DotNetHome);
    }

    public void ImportLegacyGitDependencies(string repositoryDirectory) {
        string importMarker = Path.Combine(Root, ".docker-unreal-gitdeps-imported");
        if (File.Exists(importMarker)) {
            return;
        }

        string gitDirectory = Path.Combine(repositoryDirectory, ".git");
        foreach (string legacyName in new[] { "ue-gitdeps", "ue4-gitdeps" }) {
            string legacyDirectory = Path.Combine(gitDirectory, legacyName);
            if (!Directory.Exists(legacyDirectory)) {
                continue;
            }

            int importedFiles = 0;
            long importedBytes = 0;
            foreach (string source in Directory.EnumerateFiles(legacyDirectory, "*", SearchOption.AllDirectories)) {
                string relative = Path.GetRelativePath(legacyDirectory, source);
                string destination = Path.Combine(GitDependencies, relative);
                if (File.Exists(destination)) {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                string temporary = destination + ".importing-" + Guid.NewGuid().ToString("N");
                try {
                    File.Copy(source, temporary, false);
                    try {
                        File.Move(temporary, destination, false);
                        importedFiles++;
                        importedBytes += new FileInfo(destination).Length;
                    } catch (IOException) when (File.Exists(destination)) {
                        // Another completed cache entry won the race.
                    }
                } finally {
                    File.Delete(temporary);
                }
            }

            if (importedFiles > 0) {
                Console.Out.WriteLine($"docker-unreal: imported {importedFiles} GitDependencies cache files ({importedBytes / (1024d * 1024d * 1024d):F1} GiB) from {legacyDirectory}");
            }
        }

        string temporaryMarker = importMarker + "." + Guid.NewGuid().ToString("N");
        try {
            File.WriteAllText(temporaryMarker, "Legacy GitDependencies caches imported.");
            File.Move(temporaryMarker, importMarker, true);
        } finally {
            File.Delete(temporaryMarker);
        }
    }
}
