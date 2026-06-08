// Find That Shot - organize and search a large local video archive.
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Ingve Moss Liknes <findthatshot@ingve.no>
using VideoArchiveManager.Core.Models;
using VideoArchiveManager.Core.Models.Enums;
using VideoArchiveManager.Data;

namespace VideoArchiveManager.Data.Tests.TestSupport;

// Small fluent-ish helpers for seeding a catalog in tests.
public static class CatalogSeed
{
    public static VideoItem AddVideo(
        this VideoArchiveDbContext ctx,
        string filePath,
        string? camera = null,
        int rating = 0,
        VideoStatus status = VideoStatus.Unreviewed,
        bool fileExists = true,
        double? durationSeconds = null,
        long fileSize = 1000,
        int? width = null,
        int? height = null,
        DateTime? modifiedAtFile = null,
        DateTime? folderDate = null,
        string? notes = null)
    {
        var video = new VideoItem
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            FolderPath = Path.GetDirectoryName(filePath) ?? string.Empty,
            Extension = Path.GetExtension(filePath),
            Camera = camera,
            Rating = rating,
            Status = status,
            FileExists = fileExists,
            DurationSeconds = durationSeconds,
            FileSize = fileSize,
            Width = width,
            Height = height,
            ModifiedAtFile = modifiedAtFile ?? new DateTime(2026, 1, 1),
            FolderDate = folderDate,
            Notes = notes,
        };
        ctx.VideoItems.Add(video);
        ctx.SaveChanges();
        return video;
    }

    public static Tag GetOrAddTag(this VideoArchiveDbContext ctx, string name, TagType type = TagType.Subject)
    {
        var existing = ctx.Tags.FirstOrDefault(t => t.Name == name && t.Type == type);
        if (existing is not null) return existing;
        var tag = new Tag { Name = name, Type = type };
        ctx.Tags.Add(tag);
        ctx.SaveChanges();
        return tag;
    }

    public static VideoTag AttachTag(this VideoArchiveDbContext ctx, int videoId, int tagId, bool background = false)
    {
        var vt = new VideoTag { VideoItemId = videoId, TagId = tagId, IsBackground = background };
        ctx.VideoTags.Add(vt);
        ctx.SaveChanges();
        return vt;
    }

    public static VideoMoment AddMoment(
        this VideoArchiveDbContext ctx,
        int videoId,
        double start = 1.0,
        double? end = 5.0,
        string? label = null)
    {
        var moment = new VideoMoment
        {
            VideoItemId = videoId,
            StartSeconds = start,
            EndSeconds = end,
            Label = label,
        };
        ctx.VideoMoments.Add(moment);
        ctx.SaveChanges();
        return moment;
    }
}
