using System;
using System.IO;

namespace Unreal;

sealed class RuntimeSetup {
    readonly RuntimeConfiguration _configuration;
    readonly IRepository _repository;
    readonly IEngineCompiler _compiler;
    readonly InstallationStore _store;
    readonly IToolchainConfigurator _toolchain;
    readonly RuntimeCache _cache;

    public RuntimeSetup(RuntimeConfiguration configuration, IRepository repository, IEngineCompiler compiler, InstallationStore store, IToolchainConfigurator toolchain) {
        _configuration = configuration;
        _repository = repository;
        _compiler = compiler;
        _store = store;
        _toolchain = toolchain;
        _cache = new RuntimeCache(configuration.CacheRoot, configuration.Version, configuration.Ddc);
    }

    public InstalledEngine Prepare() {
        Directory.CreateDirectory(_configuration.SourcesRoot);
        Directory.CreateDirectory(_configuration.BinariesRoot);
        _cache.Prepare();
        _toolchain.Configure(_configuration.Version);
        var request = ResolveRequest();
        if (TryGet(request.Marker, request.Resolution, out var existingEngine)) {
            return existingEngine;
        }

        using var installationLock = InstallationLock.Acquire(_store.LockPath);
        request = ResolveRequest();
        if (TryGet(request.Marker, request.Resolution, out existingEngine)) {
            return existingEngine;
        }

        Log("installing Unreal Engine " + _configuration.Version + " from " + request.Resolution.Identifier + " at commit " + request.Resolution.Commit);
        string sourceRoot = _repository.Checkout(_configuration.Source, _configuration.Version, request.Resolution.Commit, _configuration.RepositoryDirectory);
        _cache.ImportLegacyGitDependencies(_configuration.RepositoryDirectory);
        string buildDirectory = _store.PrepareBuildDirectory(request.Marker);
        var installed = _compiler.Compile(_configuration.Version, sourceRoot, request.Resolution.Commit, buildDirectory);
        var completedMarker = request.Marker with { PatchVersion = installed.PatchVersion };
        var published = _store.Publish(buildDirectory, installed.Root, completedMarker);
        Log("published Unreal Engine " + installed.PatchVersion + " from commit " + request.Resolution.Commit);
        return published;
    }

    InstallationRequest ResolveRequest() {
        Log("resolving " + _configuration.Source + " " + _configuration.VersionMode.ToString().ToLowerInvariant() + " " + _configuration.Version);
        var resolution = _repository.Resolve(_configuration.Source, _configuration.Version, _configuration.VersionMode);
        return new InstallationRequest(
            resolution,
            new InstallationMarker(
                _configuration.Version.ToString(),
                string.Empty,
                _configuration.Source,
                resolution.Commit,
                EngineCompiler.BUILD_PROFILE
            )
        );
    }

    bool TryGet(InstallationMarker request, VersionResolution resolution, out InstalledEngine engine) {
        if (!_store.TryGet(request, out engine)) {
            return false;
        }

        Log("using Unreal Engine " + _configuration.Version + " from " + resolution.Identifier + " at commit " + resolution.Commit);
        return true;
    }

    static void Log(string message) => Console.Out.WriteLine("docker-unreal: " + message);

    sealed record InstallationRequest(VersionResolution Resolution, InstallationMarker Marker);
}
