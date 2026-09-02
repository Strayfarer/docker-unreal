using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Unreal.Tests;

public sealed class RuntimeSetupTests {
    [Test]
    public void FirstCallCompilesAndPublishesRequestedEngine() {
        using var directory = new TemporaryDirectory();
        var repository = new FakeRepository("1111111111111111111111111111111111111111");
        var compiler = new FakeCompiler("5.7.4");
        var setup = CreateSetup(directory, repository, compiler, out _);

        var engine = setup.Prepare();
        string buildBatch = Path.Combine(engine.Root, "Engine", "Build", "BatchFiles", "Build.bat");

        Assert.Multiple(() => {
            Assert.That(compiler.Calls, Is.EqualTo(1));
            Assert.That(engine.PatchVersion, Is.EqualTo("5.7.4"));
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

        var first = setup.Prepare();
        var second = setup.Prepare();

        Assert.Multiple(() => {
            Assert.That(second, Is.EqualTo(first));
            Assert.That(compiler.Calls, Is.EqualTo(1));
            Assert.That(repository.Checkouts, Is.EqualTo(1));
        });
    }

    [Test]
    public void LegacyBuildProfileRecompilesPublishedEngine() {
        using var directory = new TemporaryDirectory();
        const string commit = "1111111111111111111111111111111111111111";
        var repository = new FakeRepository(commit);
        var compiler = new FakeCompiler("5.7.4");
        var setup = CreateSetup(directory, repository, compiler, out _);
        setup.Prepare();
        string markerPath = Path.Combine(directory.Path, "binaries", "5.7", ".docker-unreal.json");
        File.WriteAllText(markerPath, JsonSerializer.Serialize(new {
            Version = "5.7",
            PatchVersion = "5.7.4",
            Source = "https://example.invalid/UnrealEngine",
            Commit = commit
        }));

        var rebuilt = setup.Prepare();

        Assert.Multiple(() => {
            Assert.That(compiler.Calls, Is.EqualTo(2));
            Assert.That(rebuilt.PatchVersion, Is.EqualTo("5.7.4"));
            Assert.That(JsonSerializer.Deserialize<InstallationMarker>(File.ReadAllText(markerPath))!.BuildProfile, Is.EqualTo(EngineCompiler.BUILD_PROFILE));
        });
    }

    [Test]
    public void PublishedBranchCommitDoesNotTouchSourcesOrCompiler() {
        using var directory = new TemporaryDirectory();
        const string commit = "1111111111111111111111111111111111111111";
        var firstSetup = CreateSetup(directory, new FakeRepository(commit), new FakeCompiler("5.7.4"), out var store);
        var first = firstSetup.Prepare();
        var repository = new FakeRepository(commit);
        var compiler = new FakeCompiler("5.7.4");
        var secondSetup = CreateSetup(directory, repository, compiler, out _, out var toolchain);

        using var heldInstallerLock = new FileStream(store.LockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var preparation = Task.Run(secondSetup.Prepare);
        Assert.That(preparation.Wait(TimeSpan.FromSeconds(2)), Is.True, "an exact published commit must not wait for the installer lock");
        var second = preparation.Result;

        Assert.Multiple(() => {
            Assert.That(second, Is.EqualTo(first));
            Assert.That(repository.ResolveCalls, Is.EqualTo(1));
            Assert.That(repository.Checkouts, Is.Zero);
            Assert.That(compiler.Calls, Is.Zero);
            Assert.That(toolchain.Calls, Is.EqualTo(1));
        });
    }

    [Test]
    public void ConcurrentContainersQueueOnSourceFileAndReusePublishedResult() {
        using var directory = new TemporaryDirectory();
        const string commit = "1111111111111111111111111111111111111111";
        using var compileStarted = new ManualResetEventSlim();
        using var releaseCompile = new ManualResetEventSlim();
        var firstCompiler = new FakeCompiler("5.7.4") {
            BeforeCompile = () => {
                compileStarted.Set();
                releaseCompile.Wait();
            }
        };
        var firstSetup = CreateSetup(directory, new FakeRepository(commit), firstCompiler, out _);
        var waitingRepository = new FakeRepository(commit);
        var waitingCompiler = new FakeCompiler("5.7.4");
        var waitingSetup = CreateSetup(directory, waitingRepository, waitingCompiler, out _, out var waitingToolchain);

        var first = Task.Run(firstSetup.Prepare);
        Assert.That(compileStarted.Wait(TimeSpan.FromSeconds(2)), Is.True);
        var waiting = Task.Run(waitingSetup.Prepare);
        try {
            Thread.Sleep(250);
            Assert.Multiple(() => {
                Assert.That(waitingRepository.ResolveCalls, Is.EqualTo(1), "the initial remote check should not require the source/build lock");
                Assert.That(waitingRepository.Checkouts, Is.Zero);
                Assert.That(waitingCompiler.Calls, Is.Zero);
                Assert.That(waitingToolchain.Calls, Is.EqualTo(1));
            });
        } finally {
            releaseCompile.Set();
        }
        Assert.That(Task.WaitAll([first, waiting], TimeSpan.FromSeconds(5)), Is.True);

        Assert.Multiple(() => {
            Assert.That(waiting.Result, Is.EqualTo(first.Result));
            Assert.That(waitingRepository.ResolveCalls, Is.EqualTo(2));
            Assert.That(waitingRepository.Checkouts, Is.Zero);
            Assert.That(waitingCompiler.Calls, Is.Zero);
            Assert.That(waitingToolchain.Calls, Is.EqualTo(1));
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

        var engine = setup.Prepare();
        string buildBatch = Path.Combine(engine.Root, "Engine", "Build", "BatchFiles", "Build.bat");
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
        var engine = setup.Prepare();
        string buildBatch = Path.Combine(engine.Root, "Engine", "Build", "BatchFiles", "Build.bat");
        repository.Commit = "2222222222222222222222222222222222222222";
        compiler.Failure = new InvalidOperationException("compiler failed");

        Assert.That(() => setup.Prepare(), Throws.TypeOf<InvalidOperationException>());
        var previous = new InstallationMarker(
            "5.7",
            string.Empty,
            "https://example.invalid/UnrealEngine",
            firstCommit,
            EngineCompiler.BUILD_PROFILE
        );
        Assert.Multiple(() => {
            Assert.That(store.TryGet(previous, out var preserved), Is.True);
            Assert.That(preserved, Is.EqualTo(engine));
            Assert.That(File.Exists(buildBatch), Is.True);
        });
    }

    static RuntimeSetup CreateSetup(TemporaryDirectory directory, FakeRepository repository, FakeCompiler compiler, out InstallationStore store) {
        return CreateSetup(directory, repository, compiler, out store, out _);
    }

    static RuntimeSetup CreateSetup(TemporaryDirectory directory, FakeRepository repository, FakeCompiler compiler, out InstallationStore store, out FakeToolchainConfigurator toolchain) {
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
        toolchain = new FakeToolchainConfigurator();
        return new RuntimeSetup(configuration, repository, compiler, store, toolchain);
    }

    sealed class FakeRepository : IRepository {
        public string Commit { get; set; }
        public int ResolveCalls { get; private set; }
        public int Checkouts { get; private set; }

        public FakeRepository(string commit) => Commit = commit;

        public string ResolveRemoteCommit(string source, UnrealVersion version) {
            ResolveCalls++;
            return Commit;
        }

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
        public Action? BeforeCompile { get; init; }

        public FakeCompiler(string patchVersion) => PatchVersion = patchVersion;

        public InstalledEngine Compile(UnrealVersion version, string sourceRoot, string commit, string buildDirectory) {
            Calls++;
            BeforeCompile?.Invoke();
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
        public int Calls { get; private set; }

        public void Configure(UnrealVersion version) {
            Calls++;
        }
    }
}
