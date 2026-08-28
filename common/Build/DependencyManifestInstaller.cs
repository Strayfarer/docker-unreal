using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace Unreal;

sealed record DependencyManifestProfile(Uri AssetUri, long Size, string Sha256);

sealed class DependencyManifestInstaller : IDisposable {
    static readonly IReadOnlyDictionary<string, DependencyManifestProfile> Profiles = new Dictionary<string, DependencyManifestProfile>(StringComparer.Ordinal) {
        ["5.0"] = new(
            new Uri("https://api.github.com/repos/EpicGames/UnrealEngine/releases/assets/107274251"),
            23781954,
            "6ADB2516308553AD69025BD6684F7982113D664F428927736EBFFC2D503F53DB"
        )
    };

    readonly GitCredentials? _credentials;
    readonly HttpClient _client;

    public DependencyManifestInstaller(GitCredentials? credentials) {
        _credentials = credentials;
        _client = new HttpClient();
    }

    internal DependencyManifestInstaller(GitCredentials? credentials, HttpMessageHandler handler) {
        _credentials = credentials;
        _client = new HttpClient(handler);
    }

    public void InstallIfRequired(UnrealVersion version, string sourceRoot) {
        if (!Profiles.TryGetValue(version.ToString(), out var profile)) {
            return;
        }
        if (_credentials is null) {
            throw new InvalidOperationException("Unreal Engine " + version + " requires GitHub credentials to download Epic's repaired dependency manifest");
        }

        string destination = Path.Combine(sourceRoot, "Engine", "Build", "Commit.gitdeps.xml");
        string temporary = destination + ".downloading";
        File.Delete(temporary);
        try {
            using var request = new HttpRequestMessage(HttpMethod.Get, profile.AssetUri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
            request.Headers.UserAgent.ParseAdd("docker-unreal/1.0");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            string basicCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(_credentials.Username + ":" + _credentials.Password));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicCredentials);
            using var response = _client.Send(request, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            using var input = response.Content.ReadAsStream();
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                input.CopyTo(output);
            }

            VerifyFile(temporary, profile.Size, profile.Sha256);
            File.Move(temporary, destination, true);
        } finally {
            File.Delete(temporary);
        }
    }

    internal static void VerifyFile(string path, long expectedSize, string expectedSha256) {
        var file = new FileInfo(path);
        if (file.Length != expectedSize) {
            throw new InvalidOperationException("dependency manifest has unexpected size " + file.Length);
        }

        using var input = File.OpenRead(path);
        string actualSha256 = Convert.ToHexString(SHA256.HashData(input));
        if (!actualSha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException("dependency manifest checksum mismatch: " + actualSha256);
        }
    }

    internal static bool RequiresReplacement(UnrealVersion version) => Profiles.ContainsKey(version.ToString());

    public void Dispose() => _client.Dispose();
}
