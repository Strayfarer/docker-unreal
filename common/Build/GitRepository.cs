using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Unreal;

interface IRepository {
    string ResolveRemoteCommit(string source, UnrealVersion version);
    string Checkout(string source, UnrealVersion version, string commit, string destination);
}

sealed partial class GitRepository : IRepository {
    readonly GitCredentials? _credentials;

    public GitRepository(GitCredentials? credentials) => _credentials = credentials;

    public string ResolveRemoteCommit(string source, UnrealVersion version) {
        string branchReference = "refs/heads/" + version;
        var result = RunGitCapture(Environment.CurrentDirectory, [
            "ls-remote",
            "--exit-code",
            "--heads",
            source,
            branchReference
        ]);
        string[] fields = result.StandardOutput.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 2 || !CommitExpression().IsMatch(fields[0]) || fields[1] != branchReference) {
            throw new InvalidOperationException("Git returned an invalid branch reference for Unreal Engine " + version);
        }

        return fields[0].ToLowerInvariant();
    }

    public string Checkout(string source, UnrealVersion version, string commit, string destination) {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (!Directory.Exists(destination)) {
            Clone(source, version, destination);
        }

        string gitDirectory = Path.Combine(destination, ".git");
        if (!Directory.Exists(gitDirectory)) {
            throw new InvalidOperationException("Unreal source directory is not a Git checkout: " + destination);
        }

        string configuredSource = RunGitCapture(destination, ["remote", "get-url", "origin"]).StandardOutput.Trim();
        if (!SourcesEqual(source, configuredSource)) {
            throw new InvalidOperationException("Unreal source checkout uses a different origin. Expected " + source + ", found " + configuredSource);
        }

        string existingCommit = RunGitCapture(destination, ["rev-parse", "HEAD"]).StandardOutput.Trim().ToLowerInvariant();
        if (existingCommit == commit) {
            Log("reusing Unreal source worktree at commit " + commit);
            return destination;
        }

        RunGit(destination, ["fetch", "--depth", "1", "--no-tags", "origin", commit]);
        RunGit(destination, ["checkout", "--force", "-B", version.ToString(), commit]);
        RunGit(destination, ["reset", "--hard", commit]);
        RunGit(destination, ["clean", "-ffdx"]);
        string actualCommit = RunGitCapture(destination, ["rev-parse", "HEAD"]).StandardOutput.Trim().ToLowerInvariant();
        if (actualCommit != commit) {
            throw new InvalidOperationException("Unreal source checkout resolved to " + actualCommit + " instead of " + commit);
        }

        return destination;
    }

    void Clone(string source, UnrealVersion version, string destination) {
        string parent = Path.GetDirectoryName(destination)!;
        string staging = Path.Combine(parent, ".EpicGames.UnrealEngine.cloning-" + Guid.NewGuid().ToString("N"));
        try {
            RunGit(parent, [
                "clone",
                "--depth",
                "1",
                "--no-tags",
                "--single-branch",
                "--branch",
                version.ToString(),
                source,
                staging
            ]);
            Directory.Move(staging, destination);
        } finally {
            ManagedDirectory.DeleteIfPresent(staging, parent);
        }
    }

    void RunGit(string workingDirectory, IEnumerable<string> arguments) {
        string[] completeArguments = GitArguments(arguments).ToArray();
        Log("git " + string.Join(' ', completeArguments));
        var start = ProcessRunner.CreateStartInfo("git.exe", completeArguments, workingDirectory);
        ConfigureAuthentication(start);
        ProcessRunner.Run(start, true);
    }

    ProcessResult RunGitCapture(string workingDirectory, IEnumerable<string> arguments) {
        string[] completeArguments = GitArguments(arguments).ToArray();
        Log("git " + string.Join(' ', completeArguments));
        var start = ProcessRunner.CreateStartInfo("git.exe", completeArguments, workingDirectory);
        ConfigureAuthentication(start);
        return ProcessRunner.Capture(start, true);
    }

    IEnumerable<string> GitArguments(IEnumerable<string> arguments) {
        yield return "-c";
        yield return "credential.helper=";
        yield return "-c";
        yield return "core.longpaths=true";
        yield return "-c";
        yield return "safe.directory=*";
        foreach (string argument in arguments) {
            yield return argument;
        }
    }

    void ConfigureAuthentication(ProcessStartInfo start) {
        if (_credentials is null) {
            start.Environment["GIT_TERMINAL_PROMPT"] = "0";
            return;
        }

        start.Environment["GIT_ASKPASS"] = Environment.ProcessPath
                                             ?? throw new InvalidOperationException("cannot locate Build.exe for Git authentication");
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment[EnvironmentVariableNames.DOCKER_UNREAL_ASKPASS] = "1";
        start.Environment[EnvironmentVariableNames.UNREAL_CREDENTIALS_USR] = _credentials.Username;
        start.Environment[EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW] = _credentials.Password;
    }

    static bool SourcesEqual(string expected, string actual) => NormalizeSource(expected).Equals(NormalizeSource(actual), StringComparison.OrdinalIgnoreCase);

    static string NormalizeSource(string source) {
        string normalized = source.Trim().TrimEnd('/', '\\');
        return normalized.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? normalized[..^4] : normalized;
    }

    static void Log(string message) => Console.Out.WriteLine("docker-unreal: " + message);

    [GeneratedRegex("^[0-9a-fA-F]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex CommitExpression();
}
