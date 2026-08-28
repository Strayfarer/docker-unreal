using System;
using System.IO;

namespace Unreal.Tests;

sealed class TemporaryDirectory : IDisposable {
    public readonly string Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "docker-unreal-tests", Guid.NewGuid().ToString("N"));

    public TemporaryDirectory() => Directory.CreateDirectory(Path);

    public string CreateDirectory(string relativePath) {
        string path = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    public string Write(string relativePath, string contents) {
        string path = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        return path;
    }

    public void Dispose() {
        if (Directory.Exists(Path)) {
            Directory.Delete(Path, true);
        }
    }
}
