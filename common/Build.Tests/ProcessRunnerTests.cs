using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace Unreal.Tests;

public sealed class ProcessRunnerTests {
    [Test]
    [Platform("Win")]
    public void BatchRunnerPreservesArgumentsAndSiblingWorkingDirectory() {
        using var directory = new TemporaryDirectory();
        directory.Write("batch files/sibling.txt", string.Empty);
        string batch = directory.Write("batch files/check.bat", "@echo off\r\nif not exist sibling.txt exit /b 8\r\nif not \"%~1\"==\"hello world\" exit /b 9\r\nexit /b 0\r\n");
        string batchDirectory = Path.GetDirectoryName(batch)!;

        int exitCode = ProcessRunner.RunBatch(batch, ["hello world"], batchDirectory, false);

        Assert.That(exitCode, Is.Zero);
    }

    [Test]
    public void BatchStartInfoUsesCmdWithoutShellExecution() {
        using var directory = new TemporaryDirectory();

        var actual = ProcessRunner.CreateBatchStartInfo("Build.bat", ["-Help"], directory.Path);

        Assert.Multiple(() => {
            Assert.That(actual.FileName, Is.EqualTo("cmd.exe"));
            Assert.That(actual.UseShellExecute, Is.False);
            Assert.That(actual.WorkingDirectory, Is.EqualTo(directory.Path));
            Assert.That(actual.ArgumentList, Is.EqualTo(new[] { "/D", "/S", "/C", "call", "Build.bat", "-Help" }));
        });
    }

    [Test]
    [Platform("Win")]
    [NonParallelizable]
    public void BatchRunnerRemovesSourceCredentials() {
        using var directory = new TemporaryDirectory();
        string batch = directory.Write("check-credentials.bat", "@echo off\r\nif defined UNREAL_CREDENTIALS_USR exit /b 8\r\nif defined UNREAL_CREDENTIALS_PSW exit /b 9\r\nexit /b 0\r\n");
        string? previousUsername = Environment.GetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_USR);
        string? previousPassword = Environment.GetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW);
        try {
            Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_USR, "username");
            Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW, "password");

            int exitCode = ProcessRunner.RunBatch(batch, [], directory.Path, false, true);

            Assert.That(exitCode, Is.Zero);
        } finally {
            Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_USR, previousUsername);
            Environment.SetEnvironmentVariable(EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW, previousPassword);
        }
    }

    [Test]
    [Platform("Win")]
    public void BatchRunnerAppliesPersistentCacheEnvironment() {
        using var directory = new TemporaryDirectory();
        string batch = directory.Write("check-cache.bat", "@echo off\r\nif not \"%UBA_ROOT%\"==\"cache-root\" exit /b 8\r\nexit /b 0\r\n");

        int exitCode = ProcessRunner.RunBatchWithEnvironment(
            batch,
            [],
            directory.Path,
            false,
            new Dictionary<string, string> { ["UBA_ROOT"] = "cache-root" }
        );

        Assert.That(exitCode, Is.Zero);
    }
}
