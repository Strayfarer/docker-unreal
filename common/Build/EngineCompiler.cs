using System;
using System.Collections.Generic;
using System.IO;

namespace Unreal;

sealed record InstalledEngine(string Root, string PatchVersion);

interface IEngineCompiler {
    InstalledEngine Compile(UnrealVersion version, string sourceRoot, string commit, string buildDirectory);
}

sealed class EngineCompiler : IEngineCompiler {
    readonly DependencyManifestInstaller _manifestInstaller;
    readonly RuntimeCache _cache;

    public EngineCompiler(DependencyManifestInstaller manifestInstaller, RuntimeCache cache) {
        _manifestInstaller = manifestInstaller;
        _cache = cache;
    }

    public InstalledEngine Compile(UnrealVersion version, string sourceRoot, string commit, string buildDirectory) {
        var sourceVersion = BuildVersion.Read(sourceRoot);
        sourceVersion.AssertMatches(version);
        Log("preparing Unreal Engine " + sourceVersion.FullVersion + " dependencies at commit " + commit);
        _manifestInstaller.InstallIfRequired(version, sourceRoot);
        string gitDependencies = FindGitDependencies(sourceRoot);
        ProcessRunner.Run(gitDependencies, [
            "--force",
            "--cache=" + _cache.GitDependencies,
            "--cache-days=90",
            "--cache-size-multiplier=8",
            "--exclude=Android",
            "--exclude=Linux",
            "--exclude=Mac"
        ], sourceRoot, true, true);

        string installedBuildScript = Path.Combine(sourceRoot, "Engine", "Build", "InstalledEngineBuild.xml");
        if (!File.Exists(installedBuildScript)) {
            throw new InvalidOperationException("Unreal Installed Build script is missing: " + installedBuildScript);
        }

        var arguments = new List<string> {
            "BuildGraph",
            "-target=Make Installed Build Win64",
            "-script=Engine/Build/InstalledEngineBuild.xml",
            "-set:BuiltDirectory=" + buildDirectory.Replace('\\', '/'),
            "-set:HostPlatformOnly=true",
            "-set:WithWin64=true",
            "-set:WithClient=false",
            "-set:WithServer=false",
            "-set:WithDDC=false",
            "-set:WithFullDebugInfo=false",
            "-set:GameConfigurations=Development",
            "-set:CompileDatasmithPlugins=false",
            "-set:SignExecutables=false",
            "-set:EmbedSrcSrvInfo=false",
            "-nosign"
        };
        string installedBuildContents = File.ReadAllText(installedBuildScript);
        if (installedBuildContents.Contains("Name=\"BuildIdOverride\"", StringComparison.Ordinal)) {
            arguments.Add("-set:BuildIdOverride=UE_" + version);
        }
        if (installedBuildContents.Contains("Name=\"WithWin64NoPCH\"", StringComparison.Ordinal)) {
            arguments.Add("-set:WithWin64NoPCH=false");
        }

        Log("compiling the Unreal Engine " + sourceVersion.FullVersion + " Installed Build");
        string runUat = Path.Combine(sourceRoot, "Engine", "Build", "BatchFiles", "RunUAT.bat");
        ProcessRunner.RunBatchWithEnvironment(runUat, arguments, sourceRoot, true, _cache.Environment, true);
        string installedRoot = Path.Combine(buildDirectory, "Windows");
        ValidateInstalledBuild(installedRoot, version, sourceVersion.FullVersion);
        return new InstalledEngine(installedRoot, sourceVersion.FullVersion);
    }

    static string FindGitDependencies(string sourceRoot) {
        string[] candidates = [
            Path.Combine(sourceRoot, "Engine", "Binaries", "DotNET", "GitDependencies", "win-x64", "GitDependencies.exe"),
            Path.Combine(sourceRoot, "Engine", "Binaries", "DotNET", "GitDependencies.exe")
        ];
        foreach (string candidate in candidates) {
            if (File.Exists(candidate)) {
                return candidate;
            }
        }

        throw new InvalidOperationException("GitDependencies.exe was not found in the Unreal source checkout");
    }

    static void ValidateInstalledBuild(string installedRoot, UnrealVersion requested, string expectedPatchVersion) {
        string[] requiredFiles = [
            Path.Combine(installedRoot, "Engine", "Build", "InstalledBuild.txt"),
            Path.Combine(installedRoot, "Engine", "Build", "BatchFiles", "Build.bat"),
            Path.Combine(installedRoot, "Engine", "Binaries", "Win64", "UnrealEditor.exe")
        ];
        foreach (string requiredFile in requiredFiles) {
            if (!File.Exists(requiredFile)) {
                throw new InvalidOperationException("Installed Build output is missing: " + requiredFile);
            }
        }

        var installedVersion = BuildVersion.Read(installedRoot);
        installedVersion.AssertMatches(requested);
        if (installedVersion.FullVersion != expectedPatchVersion) {
            throw new InvalidOperationException("Installed Build version " + installedVersion.FullVersion + " does not match source version " + expectedPatchVersion);
        }
    }

    static void Log(string message) => Console.Out.WriteLine("docker-unreal: " + message);
}
