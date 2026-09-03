using NUnit.Framework;

namespace Unreal.Tests;

public sealed class SemanticVersionTests {
    [TestCase("0.0.0")]
    [TestCase("5.8.2-release")]
    [TestCase("5.8.2-release+build.001")]
    [TestCase("999999999999999999999999999999.0.1")]
    public void ParsesStrictSemanticVersions(string value) {
        Assert.That(SemanticVersion.TryParse(value, out _), Is.True);
    }

    [TestCase("")]
    [TestCase("v5.8.2-release")]
    [TestCase("5.8")]
    [TestCase("5.8.02-release")]
    [TestCase("5.8.2-release.01")]
    [TestCase("5.8.2-")]
    [TestCase("5.8.2+")]
    [TestCase("5.8.2_release")]
    public void RejectsInvalidSemanticVersions(string value) {
        Assert.That(SemanticVersion.TryParse(value, out _), Is.False);
    }

    [TestCase("5.8.9-release", "5.8.10-release", -1)]
    [TestCase("5.8.10-release", "5.8.10", -1)]
    [TestCase("5.8.10-release+one", "5.8.10-release+two", 0)]
    [TestCase("5.8.10-9", "5.8.10-release", -1)]
    public void ComparesUsingSemanticVersionPrecedence(string left, string right, int expectedSign) {
        Assert.That(SemanticVersion.TryParse(left, out var leftVersion), Is.True);
        Assert.That(SemanticVersion.TryParse(right, out var rightVersion), Is.True);

        Assert.That(leftVersion!.CompareTo(rightVersion), Is.EqualTo(expectedSign));
    }
}
