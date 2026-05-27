namespace VideoArchiveManager.Core.Services;

public interface IFileSystemService
{
    bool FileExists(string path);

    bool DirectoryExists(string path);

    IEnumerable<string> EnumerateVideoFiles(string rootPath, IReadOnlyList<string> extensions, CancellationToken cancellationToken = default);

    void OpenFolder(string path);

    void RevealInExplorer(string filePath);

    void OpenWithDefaultPlayer(string filePath);
}
