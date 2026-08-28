using System.IO;
using NUnit.Framework;

namespace Unreal.Tests;

public sealed class RuntimeCacheTests {
    [Test]
    public void CreatesVersionedUbaAndSharedPackageCaches() {
        using var directory = new TemporaryDirectory();
        string root = Path.Combine(directory.Path, "cache");
        var cache = new RuntimeCache(root, UnrealVersion.Parse("VERSION", "5.7"));

        cache.Prepare();

        Assert.Multiple(() => {
            Assert.That(cache.Uba, Is.EqualTo(Path.Combine(root, "uba", "5.7")));
            Assert.That(cache.Environment["UBA_ROOT"], Is.EqualTo(cache.Uba));
            Assert.That(cache.Environment["NUGET_PACKAGES"], Is.EqualTo(Path.Combine(root, "nuget", "packages")));
            Assert.That(cache.Environment["NUGET_PLUGINS_CACHE_PATH"], Is.EqualTo(Path.Combine(root, "nuget", "plugins")));
            Assert.That(cache.Environment["NuGetAudit"], Is.EqualTo("false"));
            Assert.That(cache.Environment["DOTNET_CLI_HOME"], Is.EqualTo(Path.Combine(root, "dotnet")));
            Assert.That(Directory.Exists(cache.GitDependencies), Is.True);
            Assert.That(Directory.Exists(cache.NuGetPackages), Is.True);
        });
    }

    [Test]
    public void ImportsLegacyGitDependenciesWithoutOverwritingSharedEntries() {
        using var directory = new TemporaryDirectory();
        string repository = directory.CreateDirectory("sources/EpicGames.UnrealEngine/.git/ue4-gitdeps/nested");
        File.WriteAllText(Path.Combine(repository, "old.pack"), "legacy");
        string root = Path.Combine(directory.Path, "cache");
        var cache = new RuntimeCache(root, UnrealVersion.Parse("VERSION", "5.0"));
        cache.Prepare();
        string existing = Path.Combine(cache.GitDependencies, "nested", "old.pack");
        Directory.CreateDirectory(Path.GetDirectoryName(existing)!);
        File.WriteAllText(existing, "shared");
        File.WriteAllText(Path.Combine(repository, "new.pack"), "new");

        cache.ImportLegacyGitDependencies(Path.Combine(directory.Path, "sources", "EpicGames.UnrealEngine"));

        Assert.Multiple(() => {
            Assert.That(File.ReadAllText(existing), Is.EqualTo("shared"));
            Assert.That(File.ReadAllText(Path.Combine(cache.GitDependencies, "nested", "new.pack")), Is.EqualTo("new"));
            Assert.That(File.Exists(Path.Combine(root, ".docker-unreal-gitdeps-imported")), Is.True);
        });
    }
}
