using System;
using NUnit.Framework;

namespace Unreal.Tests;

public sealed class UnrealVersionTests {
    [TestCase("5.0")]
    [TestCase("5.7")]
    [TestCase("6.0")]
    [TestCase("6.42")]
    public void ParsesSupportedMinorBranches(string value) {
        var actual = UnrealVersion.Parse("VERSION", value);

        Assert.That(actual.ToString(), Is.EqualTo(value));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("5")]
    [TestCase("5.0.3")]
    [TestCase("05.0")]
    [TestCase("4.27")]
    [TestCase("7.0")]
    [TestCase("release")]
    public void RejectsUnsupportedBranches(string? value) {
        Assert.That(() => UnrealVersion.Parse("VERSION", value), Throws.TypeOf<InvalidOperationException>());
    }
}
