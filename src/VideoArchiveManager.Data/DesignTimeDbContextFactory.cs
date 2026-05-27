using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using VideoArchiveManager.Core.Configuration;

namespace VideoArchiveManager.Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<VideoArchiveDbContext>
{
    public VideoArchiveDbContext CreateDbContext(string[] args)
    {
        var dbPath = AppSettings.DefaultDatabasePath;
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var optionsBuilder = new DbContextOptionsBuilder<VideoArchiveDbContext>();
        optionsBuilder.UseSqlite($"Data Source={dbPath}");
        return new VideoArchiveDbContext(optionsBuilder.Options);
    }
}
