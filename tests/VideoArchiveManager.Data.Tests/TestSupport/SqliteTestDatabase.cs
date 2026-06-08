// Find That Shot - organize and search a large local video archive.
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Ingve Moss Liknes <findthatshot@ingve.no>
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using VideoArchiveManager.Data;

namespace VideoArchiveManager.Data.Tests.TestSupport;

// A real, file-backed SQLite catalog migrated with the production EF Core
// migrations, exposed through the same IDbContextFactory<VideoArchiveDbContext>
// abstraction the services consume. Using a real file (not the InMemory
// provider) means cascade deletes, ExecuteDelete, unique indexes and
// transactions behave EXACTLY as they do in production — essential when the
// whole point is verifying data-destruction semantics.
public sealed class SqliteTestDatabase : IDbContextFactory<VideoArchiveDbContext>, IDisposable
{
    private readonly DbContextOptions<VideoArchiveDbContext> _options;

    public string DatabasePath { get; }
    public string RootDirectory { get; }

    public SqliteTestDatabase(IEnumerable<IInterceptor>? interceptors = null)
    {
        RootDirectory = Path.Combine(
            Path.GetTempPath(), "fts-data-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootDirectory);
        DatabasePath = Path.Combine(RootDirectory, "catalog.db");

        var builder = new DbContextOptionsBuilder<VideoArchiveDbContext>()
            .UseSqlite($"Data Source={DatabasePath}");

        if (interceptors is not null)
        {
            builder.AddInterceptors(interceptors);
        }

        _options = builder.Options;

        using var ctx = CreateDbContext();
        ctx.Database.Migrate();
    }

    public VideoArchiveDbContext CreateDbContext() => new(_options);

    public Task<VideoArchiveDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(CreateDbContext());

    public void Dispose()
    {
        // SQLite keeps a connection pool keyed on the file; clear it so the temp
        // file can be deleted on Windows.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(RootDirectory)) Directory.Delete(RootDirectory, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }
}
