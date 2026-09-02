using System;
using System.IO;
using NUnit.Framework;

namespace Unreal.Tests;

public sealed class UnrealCommandTests {
    [TestCase("Build", "Build")]
    [TestCase("build", "Build")]
    [TestCase("RunUAT", "RunUAT")]
    [TestCase("Cmd", "Cmd")]
    public void ParsesToolSelectorAndPreservesRemainingArguments(string selector, string expected) {
        string[] toolArguments = ["first", "two words", "-Flag=value"];

        var actual = UnrealCommand.Parse([selector, .. toolArguments]);

        Assert.Multiple(() => {
            Assert.That(actual.Name.ToString(), Is.EqualTo(expected));
            Assert.That(actual.Arguments, Is.EqualTo(toolArguments));
        });
    }

    [TestCase("--version", "Version")]
    [TestCase("--help", "Help")]
    public void ParsesStandaloneInformationCommand(string selector, string expected) {
        var actual = UnrealCommand.Parse([selector]);

        Assert.Multiple(() => {
            Assert.That(actual.Name.ToString(), Is.EqualTo(expected));
            Assert.That(actual.Arguments, Is.Empty);
        });
    }

    [Test]
    public void RepairsWindowsDockerSplitOptionExtension() {
        var actual = UnrealCommand.Parse([
            "Cmd",
            "project.uproject",
            "-AllowListFile=Config/BlueprintAllowList",
            ".txt",
            "-Unattended"
        ]);

        Assert.That(actual.Arguments, Is.EqualTo(new[] {
            "project.uproject",
            "-AllowListFile=Config/BlueprintAllowList.txt",
            "-Unattended"
        }));
    }

    [Test]
    public void RepairsWindowsDockerEmbeddedOptionExtension() {
        var actual = UnrealCommand.Parse([
            "Cmd",
            "project.uproject",
            "-AllowListFile=Config/BlueprintAllowList .txt",
            "-Unattended"
        ]);

        Assert.That(actual.Arguments, Is.EqualTo(new[] {
            "project.uproject",
            "-AllowListFile=Config/BlueprintAllowList.txt",
            "-Unattended"
        }));
    }

    [Test]
    public void PreservesStandaloneDottedArgument() {
        var actual = UnrealCommand.Parse(["Cmd", "project.uproject", ".txt"]);

        Assert.That(actual.Arguments, Is.EqualTo(new[] { "project.uproject", ".txt" }));
    }

    [TestCase("Build", "Engine/Build/BatchFiles/Build.bat", true)]
    [TestCase("RunUAT", "Engine/Build/BatchFiles/RunUAT.bat", true)]
    [TestCase("Cmd", "Engine/Binaries/Win64/UnrealEditor-Cmd.exe", false)]
    public void ResolvesInstalledEngineTool(string selector, string relativePath, bool expectedBatch) {
        string root = Path.GetFullPath("engine-root");
        var request = UnrealCommand.Parse([selector]);

        var actual = request.Resolve(new InstalledEngine(root, "5.7.4"));

        Assert.Multiple(() => {
            Assert.That(actual.Executable, Is.EqualTo(Path.Combine([root, .. relativePath.Split('/')])));
            Assert.That(actual.IsBatch, Is.EqualTo(expectedBatch));
        });
    }

    [TestCase]
    [TestCase("Unknown")]
    [TestCase("--help", "unexpected")]
    [TestCase("--version", "unexpected")]
    public void RejectsMissingOrInvalidSelector(params string[] arguments) {
        Assert.That(
            () => UnrealCommand.Parse(arguments),
            Throws.TypeOf<InvalidOperationException>().With.Message.Contains("Unreal --help")
        );
    }

    [Test]
    public void LauncherAssemblyIsNamedUnreal() {
        Assert.That(typeof(UnrealCommand).Assembly.GetName().Name, Is.EqualTo("Unreal"));
    }
}
