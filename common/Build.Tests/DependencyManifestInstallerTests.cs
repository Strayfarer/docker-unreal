using System;
using NUnit.Framework;

namespace Unreal.Tests;

public sealed class DependencyManifestInstallerTests {
    [Test]
    public void VerifiesSizeAndChecksum() {
        using var directory = new TemporaryDirectory();
        string path = directory.Write("manifest", "test");

        Assert.Multiple(() => {
            Assert.That(() => DependencyManifestInstaller.VerifyFile(path, 4, "9F86D081884C7D659A2FEAA0C55AD015A3BF4F1B2B0B822CD15D6C15B0F00A08"), Throws.Nothing);
            Assert.That(() => DependencyManifestInstaller.VerifyFile(path, 5, "9F86D081884C7D659A2FEAA0C55AD015A3BF4F1B2B0B822CD15D6C15B0F00A08"), Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => DependencyManifestInstaller.VerifyFile(path, 4, new string('0', 64)), Throws.TypeOf<InvalidOperationException>());
        });
    }

    [TestCase("5.0", true)]
    [TestCase("5.1", false)]
    [TestCase("5.7", false)]
    public void SelectsOnlyTheRetiredDependencyManifest(string version, bool expected) {
        Assert.That(DependencyManifestInstaller.RequiresReplacement(UnrealVersion.Parse("VERSION", version)), Is.EqualTo(expected));
    }
}
