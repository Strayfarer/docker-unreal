using System;
using NUnit.Framework;

namespace Unreal.Tests;

public sealed class UnrealVersionModeTests {
    [Test]
    public void DefaultsToTagMode() {
        Assert.That(UnrealVersionMode.Parse("MODE", null), Is.EqualTo(EUnrealVersionMode.Tag));
    }

    [TestCase("tag", "Tag")]
    [TestCase("TAG", "Tag")]
    [TestCase("branch", "Branch")]
    public void ParsesNamedMode(string value, string expected) {
        Assert.That(UnrealVersionMode.Parse("MODE", value).ToString(), Is.EqualTo(expected));
    }

    [TestCase("")]
    [TestCase("release")]
    [TestCase(" tag ")]
    public void RejectsUnknownMode(string value) {
        Assert.That(() => UnrealVersionMode.Parse("MODE", value), Throws.TypeOf<InvalidOperationException>());
    }
}
