using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace Unreal.Tests;

public sealed class InstalledEngineEnvironmentTests {
    [TestCase("Windows")]
    [TestCase("8.0.300/win-x64")]
    public void ResolvesSupportedBundledDotnetLayout(string relativeDirectory) {
        using var temporary = new TemporaryDirectory();
        string engineRoot = Path.Combine(temporary.Path, "engine");
        string dotnet = Path.Combine([
            engineRoot,
            "Engine",
            "Binaries",
            "ThirdParty",
            "DotNet",
            .. relativeDirectory.Split('/')
        ]);
        Directory.CreateDirectory(dotnet);
        File.WriteAllText(Path.Combine(dotnet, "dotnet.exe"), string.Empty);

        string actual = InstalledEngineEnvironment.FindBundledDotnetDirectory(engineRoot);

        Assert.That(actual, Is.EqualTo(dotnet));
    }

    [Test]
    public void CreatesToolEnvironmentWithBundledDotnet() {
        using var temporary = new TemporaryDirectory();
        string engineRoot = Path.Combine(temporary.Path, "engine");
        string dotnet = Path.Combine(engineRoot, "Engine", "Binaries", "ThirdParty", "DotNet", "8.0.412", "win-x64");
        Directory.CreateDirectory(dotnet);
        File.WriteAllText(Path.Combine(dotnet, "dotnet.exe"), string.Empty);

        var actual = InstalledEngineEnvironment.Create(
            new InstalledEngine(engineRoot, "5.7.4"),
            new Dictionary<string, string> { ["UBA_ROOT"] = "cache-root" }
        );

        Assert.Multiple(() => {
            Assert.That(actual["UBA_ROOT"], Is.EqualTo("cache-root"));
            Assert.That(actual["DOTNET_ROOT"], Is.EqualTo(dotnet));
            Assert.That(actual["DOTNET_MULTILEVEL_LOOKUP"], Is.EqualTo("0"));
            Assert.That(actual["DOTNET_ROLL_FORWARD"], Is.EqualTo("LatestMajor"));
            Assert.That(actual["PATH"], Does.StartWith(dotnet + Path.PathSeparator));
        });
    }

    [Test]
    public void RejectsInstalledBuildWithoutBundledDotnet() {
        using var temporary = new TemporaryDirectory();

        Assert.That(
            () => InstalledEngineEnvironment.FindBundledDotnetDirectory(temporary.Path),
            Throws.InvalidOperationException.With.Message.Contains("bundled x64 dotnet.exe is missing")
        );
    }
}
