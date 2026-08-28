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

        string checkout = PrimaryWorktreeMatches(destination, version)
            ? destination
            : WorktreeDirectory(destination, version);
        if (!Directory.Exists(checkout)) {
            EnsureCommit(destination, commit);
            CreateWorktree(destination, checkout, commit);
            return checkout;
        }

        ValidateWorktree(destination, checkout);
        string existingCommit = RunGitCapture(checkout, ["rev-parse", "HEAD"]).StandardOutput.Trim().ToLowerInvariant();
        if (existingCommit == commit) {
            RunGit(checkout, ["reset", "--hard", commit]);
            Log("reusing Unreal " + version + " source worktree at commit " + commit);
            return checkout;
        }

        EnsureCommit(destination, commit);
        if (PathsEqual(checkout, destination)) {
            RunGit(checkout, ["checkout", "--force", "-B", version.ToString(), commit]);
        } else {
            RunGit(checkout, ["checkout", "--force", "--detach", commit]);
        }
        RunGit(checkout, ["reset", "--hard", commit]);
        RunGit(checkout, ["clean", "-ffdx"]);
        string actualCommit = RunGitCapture(checkout, ["rev-parse", "HEAD"]).StandardOutput.Trim().ToLowerInvariant();
        if (actualCommit != commit) {
            throw new InvalidOperationException("Unreal source checkout resolved to " + actualCommit + " instead of " + commit);
        }

        return checkout;
    }

    bool PrimaryWorktreeMatches(string repository, UnrealVersion version) {
        var buildVersion = BuildVersion.Read(repository);
        return version.Matches(buildVersion);
    }

    void EnsureCommit(string repository, string commit) {
        var existing = RunGitCapture(repository, ["cat-file", "-e", commit + "^{commit}"], false);
        if (existing.ExitCode != 0) {
            RunGit(repository, ["fetch", "--depth", "1", "--no-tags", "origin", commit]);
        }
    }

    void CreateWorktree(string repository, string worktree, string commit) {
        string worktreeRoot = Path.GetDirectoryName(worktree)!;
        Directory.CreateDirectory(worktreeRoot);
        RunGit(repository, ["worktree", "prune"]);
        if (Directory.Exists(worktree)) {
            throw new InvalidOperationException("Unreal worktree directory is not registered with the shared repository: " + worktree);
        }

        RunGit(repository, ["worktree", "add", "--force", "--detach", worktree, commit]);
        Log("created Unreal source worktree " + worktree + " at commit " + commit);
    }

    void ValidateWorktree(string repository, string worktree) {
        string worktreeGit = Path.Combine(worktree, ".git");
        if (PathsEqual(repository, worktree)) {
            if (!Directory.Exists(worktreeGit)) {
                throw new InvalidOperationException("Unreal source directory is not a Git checkout: " + worktree);
            }
            return;
        }

        if (!File.Exists(worktreeGit)) {
            throw new InvalidOperationException("Unreal source worktree is not linked to the shared repository: " + worktree);
        }

        string commonDirectory = RunGitCapture(worktree, ["rev-parse", "--path-format=absolute", "--git-common-dir"]).StandardOutput.Trim();
        string expected = Path.Combine(repository, ".git");
        if (!PathsEqual(commonDirectory, expected)) {
            throw new InvalidOperationException("Unreal source worktree uses a different shared repository. Expected " + expected + ", found " + commonDirectory);
        }
    }

    static string WorktreeDirectory(string repository, UnrealVersion version) =>
        Path.Combine(Path.GetDirectoryName(repository)!, "worktrees", version.ToString());

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

    ProcessResult RunGitCapture(string workingDirectory, IEnumerable<string> arguments, bool requireSuccess = true) {
        string[] completeArguments = GitArguments(arguments).ToArray();
        Log("git " + string.Join(' ', completeArguments));
        var start = ProcessRunner.CreateStartInfo("git.exe", completeArguments, workingDirectory);
        ConfigureAuthentication(start);
        return ProcessRunner.Capture(start, requireSuccess);
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

    static bool PathsEqual(string expected, string actual) =>
        Path.GetFullPath(expected).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Equals(
                Path.GetFullPath(actual).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal
            );

    static string NormalizeSource(string source) {
        string normalized = source.Trim().TrimEnd('/', '\\');
        return normalized.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? normalized[..^4] : normalized;
    }

    static void Log(string message) => Console.Out.WriteLine("docker-unreal: " + message);

    [GeneratedRegex("^[0-9a-fA-F]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex CommitExpression();
}
