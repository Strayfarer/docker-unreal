using System.IO;
using System.Xml.Linq;
using NUnit.Framework;

namespace Unreal.Tests;

public sealed class ToolchainConfiguratorTests {
    [TestCase("5.0", "VisualStudio2019", "14.29.")]
    [TestCase("5.1", "VisualStudio2019", "14.29.")]
    [TestCase("5.2", "VisualStudio2022", "14.38.")]
    [TestCase("5.6", "VisualStudio2022", "14.38.")]
    [TestCase("5.7", "VisualStudio2022", "14.44.")]
    [TestCase("6.0", "VisualStudio2022", "14.44.")]
    public void SelectsVersionSpecificCompiler(string version, string compiler, string prefix) {
        var actual = ToolchainConfigurator.Profile(UnrealVersion.Parse("VERSION", version), "vs2019", "vs2022");

        Assert.Multiple(() => {
            Assert.That(actual.Compiler, Is.EqualTo(compiler));
            Assert.That(actual.ToolchainPrefix, Is.EqualTo(prefix));
        });
    }

    [Test]
    public void WritesUnrealBuildToolConfiguration() {
        using var directory = new TemporaryDirectory();
        string vs2019 = directory.CreateDirectory("vs2019/VC/Tools/MSVC/14.29.30136");
        string vs2022 = directory.CreateDirectory("vs2022/VC/Tools/MSVC/14.44.35207");
        vs2019 = Path.GetFullPath(Path.Combine(vs2019, "../../../.."));
        vs2022 = Path.GetFullPath(Path.Combine(vs2022, "../../../.."));
        var configurator = new ToolchainConfigurator(directory.Path, vs2019, vs2022);

        configurator.Configure(UnrealVersion.Parse("VERSION", "5.7"));

        string path = Path.Combine(directory.Path, "Unreal Engine", "UnrealBuildTool", "BuildConfiguration.xml");
        var document = XDocument.Load(path);
        XNamespace schema = "https://www.unrealengine.com/BuildConfiguration";
        Assert.Multiple(() => {
            Assert.That(document.Root?.Element(schema + "WindowsPlatform")?.Element(schema + "Compiler")?.Value, Is.EqualTo("VisualStudio2022"));
            Assert.That(document.Root?.Element(schema + "WindowsPlatform")?.Element(schema + "CompilerVersion")?.Value, Is.EqualTo("14.44.35207"));
            Assert.That(document.Root?.Element(schema + "WindowsPlatform")?.Element(schema + "WindowsSdkVersion")?.Value, Is.EqualTo("10.0.22621.0"));
        });
    }
}
