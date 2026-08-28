using System;
using NUnit.Framework;

namespace Unreal.Tests;

public sealed class BuildVersionTests {
    [Test]
    public void ReadsAndMatchesPatchVersion() {
        using var directory = new TemporaryDirectory();
        directory.Write("Engine/Build/Build.version", "{\"MajorVersion\":5,\"MinorVersion\":7,\"PatchVersion\":4}");

        var actual = BuildVersion.Read(directory.Path);

        Assert.Multiple(() => {
            Assert.That(actual.FullVersion, Is.EqualTo("5.7.4"));
            Assert.That(() => actual.AssertMatches(UnrealVersion.Parse("VERSION", "5.7")), Throws.Nothing);
            Assert.That(() => actual.AssertMatches(UnrealVersion.Parse("VERSION", "5.6")), Throws.TypeOf<InvalidOperationException>());
        });
    }
}
