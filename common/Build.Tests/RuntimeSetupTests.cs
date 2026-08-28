using System;
using System.IO;
using System.Text.Json;
using NUnit.Framework;

namespace Unreal.Tests;

public sealed class RuntimeSetupTests {
    [Test]
    public void FirstCallCompilesAndPublishesRequestedEngine() {
        using var directory = new TemporaryDirectory();
        var repository = new FakeRepository("1111111111111111111111111111111111111111");
        var compiler = new FakeCompiler("5.7.4");
        var setup = CreateSetup(directory, repository, compiler, out _);

        string buildBatch = setup.Prepare();

        Assert.Multiple(() => {
            Assert.That(compiler.Calls, Is.EqualTo(1));
            Assert.That(buildBatch, Is.EqualTo(Path.Combine(directory.Path, "binaries", "5.7", "Engine", "Build", "BatchFiles", "Build.bat")));
            Assert.That(File.Exists(buildBatch), Is.True);
        });
    }

    [Test]
    public void SameBranchCommitReusesPublishedEngine() {
        using var directory = new TemporaryDirectory();
        var repository = new FakeRepository("1111111111111111111111111111111111111111");
        var compiler = new FakeCompiler("5.7.4");
        var setup = CreateSetup(directory, repository, compiler, out _);

        string first = setup.Prepare();
        string second = setup.Prepare();

        Assert.Multiple(() => {
            Assert.That(second, Is.EqualTo(first));
            Assert.That(compiler.Calls, Is.EqualTo(1));
            Assert.That(repository.Checkouts, Is.EqualTo(1));
        });
    }

    [Test]
    public void MovedBranchReplacesPreviousPatchAndCommit() {
        using var directory = new TemporaryDirectory();
        var repository = new FakeRepository("1111111111111111111111111111111111111111");
        var compiler = new FakeCompiler("5.7.4");
        var setup = CreateSetup(directory, repository, compiler, out _);
        setup.Prepare();
        repository.Commit = "2222222222222222222222222222222222222222";
        compiler.PatchVersion = "5.7.5";

        string buildBatch = setup.Prepare();
        string markerPath = Path.Combine(directory.Path, "binaries", "5.7", ".docker-unreal.json");
        var marker = JsonSerializer.Deserialize<InstallationMarker>(File.ReadAllText(markerPath))!;

        Assert.Multiple(() => {
            Assert.That(compiler.Calls, Is.EqualTo(2));
            Assert.That(marker.Commit, Is.EqualTo(repository.Commit));
            Assert.That(marker.PatchVersion, Is.EqualTo("5.7.5"));
            Assert.That(File.Exists(buildBatch), Is.True);
            Assert.That(Directory.Exists(Path.Combine(directory.Path, "binaries", ".replaced")), Is.True);
            Assert.That(Directory.GetDirectories(Path.Combine(directory.Path, "binaries", ".replaced")), Is.Empty);
        });
    }

    [Test]
    public void FailedRebuildPreservesLastCompleteInstallation() {
        using var directory = new TemporaryDirectory();
        const string firstCommit = "1111111111111111111111111111111111111111";
        var repository = new FakeRepository(firstCommit);
        var compiler = new FakeCompiler("5.7.4");
        var setup = CreateSetup(directory, repository, compiler, out var store);
        string buildBatch = setup.Prepare();
        repository.Commit = "2222222222222222222222222222222222222222";
        compiler.Failure = new InvalidOperationException("compiler failed");

        Assert.That(() => setup.Prepare(), Throws.TypeOf<InvalidOperationException>());
        var previous = new InstallationMarker("5.7", string.Empty, "https://example.invalid/UnrealEngine", firstCommit);
        Assert.Multiple(() => {
            Assert.That(store.TryGet(previous, out string preserved), Is.True);
            Assert.That(preserved, Is.EqualTo(buildBatch));
            Assert.That(File.Exists(buildBatch), Is.True);
        });
    }

    static RuntimeSetup CreateSetup(TemporaryDirectory directory, FakeRepository repository, FakeCompiler compiler, out InstallationStore store) {
        string sources = directory.CreateDirectory("sources");
        string binaries = directory.CreateDirectory("binaries");
        var configuration = new RuntimeConfiguration(
            UnrealVersion.Parse("VERSION", "5.7"),
            "https://example.invalid/UnrealEngine",
            null,
            sources,
            binaries
        );
        store = new InstallationStore(binaries);
        return new RuntimeSetup(configuration, repository, compiler, store, new FakeToolchainConfigurator());
    }

    sealed class FakeRepository : IRepository {
        public string Commit { get; set; }
        public int Checkouts { get; private set; }

        public FakeRepository(string commit) => Commit = commit;

        public string ResolveRemoteCommit(string source, UnrealVersion version) => Commit;

        public string Checkout(string source, UnrealVersion version, string commit, string destination) {
            Checkouts++;
            Directory.CreateDirectory(destination);
            return destination;
        }
    }

    sealed class FakeCompiler : IEngineCompiler {
        public string PatchVersion { get; set; }
        public int Calls { get; private set; }
        public Exception? Failure { get; set; }

        public FakeCompiler(string patchVersion) => PatchVersion = patchVersion;

        public InstalledEngine Compile(UnrealVersion version, string sourceRoot, string commit, string buildDirectory) {
            Calls++;
            if (Failure is not null) {
                throw Failure;
            }

            string installedRoot = Path.Combine(buildDirectory, "Windows");
            string buildBatch = Path.Combine(installedRoot, "Engine", "Build", "BatchFiles", "Build.bat");
            Directory.CreateDirectory(Path.GetDirectoryName(buildBatch)!);
            File.WriteAllText(buildBatch, "@exit /b 0");
            string[] components = PatchVersion.Split('.');
            string buildVersion = JsonSerializer.Serialize(new {
                MajorVersion = int.Parse(components[0]),
                MinorVersion = int.Parse(components[1]),
                PatchVersion = int.Parse(components[2])
            });
            string buildVersionPath = Path.Combine(installedRoot, "Engine", "Build", "Build.version");
            File.WriteAllText(buildVersionPath, buildVersion);
            return new InstalledEngine(installedRoot, PatchVersion);
        }
    }

    sealed class FakeToolchainConfigurator : IToolchainConfigurator {
        public void Configure(UnrealVersion version) {
        }
    }
}
