using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Unreal;

sealed record ToolchainProfile(string Compiler, string VisualStudioRoot, string ToolchainPrefix, string WindowsSdkVersion);

interface IToolchainConfigurator {
    void Configure(UnrealVersion version);
}

sealed class ToolchainConfigurator : IToolchainConfigurator {
    readonly string _applicationData;
    readonly string _visualStudio2019Root;
    readonly string _visualStudio2022Root;

    public ToolchainConfigurator()
        : this(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"C:\BuildTools\2019",
            @"C:\BuildTools\2022"
        ) {
    }

    internal ToolchainConfigurator(string applicationData, string visualStudio2019Root, string visualStudio2022Root) {
        _applicationData = applicationData;
        _visualStudio2019Root = visualStudio2019Root;
        _visualStudio2022Root = visualStudio2022Root;
    }

    public void Configure(UnrealVersion version) {
        var profile = Profile(version, _visualStudio2019Root, _visualStudio2022Root);
        string toolchainRoot = Path.Combine(profile.VisualStudioRoot, "VC", "Tools", "MSVC");
        string toolchainVersion = FindToolchain(toolchainRoot, profile.ToolchainPrefix);
        XNamespace schema = "https://www.unrealengine.com/BuildConfiguration";
        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(schema + "Configuration",
                new XElement(schema + "WindowsPlatform",
                    new XElement(schema + "Compiler", profile.Compiler),
                    new XElement(schema + "CompilerVersion", toolchainVersion),
                    new XElement(schema + "WindowsSdkVersion", profile.WindowsSdkVersion)
                )
            )
        );
        string directory = Path.Combine(_applicationData, "Unreal Engine", "UnrealBuildTool");
        string path = Path.Combine(directory, "BuildConfiguration.xml");
        Directory.CreateDirectory(directory);
        string temporary = path + "." + Guid.NewGuid().ToString("N");
        try {
            document.Save(temporary);
            File.Move(temporary, path, true);
        } finally {
            File.Delete(temporary);
        }

        Console.Out.WriteLine("docker-unreal: selected " + profile.Compiler + " MSVC " + toolchainVersion + " and Windows SDK " + profile.WindowsSdkVersion);
    }

    internal static ToolchainProfile Profile(UnrealVersion version, string visualStudio2019Root, string visualStudio2022Root) {
        if (version.Major == 5 && version.Minor <= 1) {
            return new ToolchainProfile("VisualStudio2019", visualStudio2019Root, "14.29.", "10.0.18362.0");
        }
        if (version.Major == 5 && version.Minor <= 6) {
            return new ToolchainProfile("VisualStudio2022", visualStudio2022Root, "14.38.", "10.0.22621.0");
        }

        return new ToolchainProfile("VisualStudio2022", visualStudio2022Root, "14.44.", "10.0.22621.0");
    }

    static string FindToolchain(string root, string prefix) {
        if (!Directory.Exists(root)) {
            throw new InvalidOperationException("Visual Studio toolchain directory is missing: " + root);
        }

        var candidates = new List<(Version Version, string Name)>();
        foreach (string directory in Directory.GetDirectories(root)) {
            string name = Path.GetFileName(directory);
            if (name.StartsWith(prefix, StringComparison.Ordinal)
                && Version.TryParse(name, out var parsed)) {
                candidates.Add((parsed, name));
            }
        }
        if (candidates.Count == 0) {
            string available = string.Join(", ", Directory.GetDirectories(root).Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal));
            throw new InvalidOperationException("MSVC " + prefix + "x is required but not installed. Found: " + available);
        }

        return candidates.OrderByDescending(candidate => candidate.Version).First().Name;
    }
}
