using System.IO;
using NUnit.Framework;

namespace Unreal.Tests;

[Platform("Win")]
public sealed class GitRepositoryTests {
    [Test]
    public void ReusesPrimaryCheckoutAndCreatesLinkedWorktreeForAnotherMinor() {
        using var directory = new TemporaryDirectory();
        var fixture = CreateOrigin(directory);
        var repository = new GitRepository(null);
        string repositoryDirectory = Path.Combine(directory.Path, "sources", "EpicGames.UnrealEngine");
        var version50 = UnrealVersion.Parse("VERSION", "5.0");
        var version57 = UnrealVersion.Parse("VERSION", "5.7");

        string checkout50 = repository.Checkout(fixture.Source, version50, repository.ResolveRemoteCommit(fixture.Source, version50), repositoryDirectory);
        string retained50 = Path.Combine(checkout50, "Engine", "Intermediate", "retained.obj");
        Directory.CreateDirectory(Path.GetDirectoryName(retained50)!);
        File.WriteAllText(retained50, "5.0");
        string checkout57 = repository.Checkout(fixture.Source, version57, repository.ResolveRemoteCommit(fixture.Source, version57), repositoryDirectory);
        string retained57 = Path.Combine(checkout57, "Engine", "Intermediate", "retained.obj");
        Directory.CreateDirectory(Path.GetDirectoryName(retained57)!);
        File.WriteAllText(retained57, "5.7");
        File.Delete(Path.Combine(checkout50, "Engine", "Source.txt"));

        string reused50 = repository.Checkout(fixture.Source, version50, repository.ResolveRemoteCommit(fixture.Source, version50), repositoryDirectory);

        Assert.Multiple(() => {
            Assert.That(checkout50, Is.EqualTo(repositoryDirectory));
            Assert.That(checkout57, Is.EqualTo(Path.Combine(directory.Path, "sources", "worktrees", "5.7")));
            Assert.That(reused50, Is.EqualTo(checkout50));
            Assert.That(File.ReadAllText(Path.Combine(reused50, "Engine", "Source.txt")), Is.EqualTo("5.0"));
            Assert.That(File.ReadAllText(retained50), Is.EqualTo("5.0"));
            Assert.That(File.ReadAllText(retained57), Is.EqualTo("5.7"));
        });
    }

    [Test]
    public void MovedBranchCleansOnlyItsOwnMinorWorktree() {
        using var directory = new TemporaryDirectory();
        var fixture = CreateOrigin(directory);
        var repository = new GitRepository(null);
        string repositoryDirectory = Path.Combine(directory.Path, "sources", "EpicGames.UnrealEngine");
        var version50 = UnrealVersion.Parse("VERSION", "5.0");
        var version57 = UnrealVersion.Parse("VERSION", "5.7");
        string checkout50 = repository.Checkout(fixture.Source, version50, repository.ResolveRemoteCommit(fixture.Source, version50), repositoryDirectory);
        string checkout57 = repository.Checkout(fixture.Source, version57, repository.ResolveRemoteCommit(fixture.Source, version57), repositoryDirectory);
        string retained50 = WriteIntermediate(checkout50, "5.0");
        string stale57 = WriteIntermediate(checkout57, "old 5.7");

        Git(fixture.Seed, "checkout", "5.7");
        File.WriteAllText(Path.Combine(fixture.Seed, "Engine", "Source.txt"), "moved");
        Git(fixture.Seed, "add", ".");
        Git(fixture.Seed, "commit", "-m", "move 5.7");
        Git(fixture.Seed, "push", "origin", "5.7");
        string movedCommit = repository.ResolveRemoteCommit(fixture.Source, version57);

        string updated57 = repository.Checkout(fixture.Source, version57, movedCommit, repositoryDirectory);

        Assert.Multiple(() => {
            Assert.That(updated57, Is.EqualTo(checkout57));
            Assert.That(File.Exists(stale57), Is.False);
            Assert.That(File.ReadAllText(retained50), Is.EqualTo("5.0"));
            Assert.That(File.ReadAllText(Path.Combine(updated57, "Engine", "Source.txt")), Is.EqualTo("moved"));
        });
    }

    static OriginFixture CreateOrigin(TemporaryDirectory directory) {
        string seed = directory.CreateDirectory("seed");
        Git(seed, "init");
        Git(seed, "config", "user.name", "docker-unreal tests");
        Git(seed, "config", "user.email", "docker-unreal@example.invalid");
        WriteBuildVersion(seed, 5, 0, 3);
        File.WriteAllText(Path.Combine(seed, "Engine", "Source.txt"), "5.0");
        Git(seed, "add", ".");
        Git(seed, "commit", "-m", "5.0");
        Git(seed, "branch", "5.0");
        WriteBuildVersion(seed, 5, 7, 4);
        File.WriteAllText(Path.Combine(seed, "Engine", "Source.txt"), "5.7");
        Git(seed, "add", ".");
        Git(seed, "commit", "-m", "5.7");
        Git(seed, "branch", "5.7");

        string origin = Path.Combine(directory.Path, "origin.git");
        Git(directory.Path, "clone", "--bare", seed, origin);
        string source = origin.Replace('\\', '/');
        Git(seed, "remote", "add", "origin", source);
        return new OriginFixture(seed, source);
    }

    static string WriteIntermediate(string checkout, string contents) {
        string path = Path.Combine(checkout, "Engine", "Intermediate", "retained.obj");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        return path;
    }

    static void WriteBuildVersion(string root, int major, int minor, int patch) {
        string path = Path.Combine(root, "Engine", "Build", "Build.version");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $"{{\"MajorVersion\":{major},\"MinorVersion\":{minor},\"PatchVersion\":{patch}}}");
    }

    static void Git(string workingDirectory, params string[] arguments) =>
        ProcessRunner.Run("git.exe", arguments, workingDirectory, true);

    sealed record OriginFixture(string Seed, string Source);
}
