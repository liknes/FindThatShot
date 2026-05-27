namespace VideoArchiveManager.Core.Configuration;

public interface ISettingsStore
{
    AppSettings Current { get; }

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);

    AppSettings Load();
}
