using System;
using System.IO;

namespace Unreal;

sealed class RuntimeSetup {
    readonly RuntimeConfiguration _configuration;
    readonly IRepository _repository;
    readonly IEngineCompiler _compiler;
    readonly InstallationStore _store;
    readonly IToolchainConfigurator _toolchain;

    public RuntimeSetup(RuntimeConfiguration configuration, IRepository repository, IEngineCompiler compiler, InstallationStore store, IToolchainConfigurator toolchain) {
        _configuration = configuration;
        _repository = repository;
        _compiler = compiler;
        _store = store;
        _toolchain = toolchain;
    }

    public string Prepare() {
        Directory.CreateDirectory(_configuration.SourcesRoot);
        Directory.CreateDirectory(_configuration.BinariesRoot);
        using var installationLock = InstallationLock.Acquire(_store.LockPath);
        _toolchain.Configure(_configuration.Version);
        Log("resolving " + _configuration.Source + " branch " + _configuration.Version);
        string commit = _repository.ResolveRemoteCommit(_configuration.Source, _configuration.Version);
        var request = new InstallationMarker(_configuration.Version.ToString(), string.Empty, _configuration.Source, commit);
        if (_store.TryGet(request, out string existingBuildBatch)) {
            Log("using Unreal Engine " + _configuration.Version + " from commit " + commit);
            return existingBuildBatch;
        }

        Log("installing Unreal Engine " + _configuration.Version + " from commit " + commit);
        string sourceRoot = _repository.Checkout(_configuration.Source, _configuration.Version, commit, _configuration.SourceDirectory);
        string buildDirectory = _store.PrepareBuildDirectory(request);
        var installed = _compiler.Compile(_configuration.Version, sourceRoot, commit, buildDirectory);
        var completedMarker = request with { PatchVersion = installed.PatchVersion };
        string buildBatch = _store.Publish(buildDirectory, installed.Root, completedMarker);
        Log("published Unreal Engine " + installed.PatchVersion + " from commit " + commit);
        return buildBatch;
    }

    static void Log(string message) => Console.Out.WriteLine("docker-unreal: " + message);
}
