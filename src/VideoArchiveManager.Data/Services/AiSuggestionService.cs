// Find That Shot - organize and search a large local video archive.
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Ingve Moss Liknes <findthatshot@ingve.no>
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.
using Microsoft.EntityFrameworkCore;
using VideoArchiveManager.Core.Models;
using VideoArchiveManager.Core.Models.Enums;
using VideoArchiveManager.Core.Services.Ai;

namespace VideoArchiveManager.Data.Services;

// Backs the AI suggestion review queue. Accepting a suggestion promotes it to a
// real Tag/VideoTag; rejecting remembers the dismissal so a re-run won't propose
// it again. Neither touches source video files.
public class AiSuggestionService : IAiSuggestionService
{
    private readonly IDbContextFactory<VideoArchiveDbContext> _contextFactory;

    public AiSuggestionService(IDbContextFactory<VideoArchiveDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<int> CountPendingAsync(CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await ctx.AiTagSuggestions
            .CountAsync(s => s.State == AiSuggestionState.Pending, cancellationToken);
    }

    public async Task<IReadOnlyList<AiSuggestionGroup>> GetPendingGroupedAsync(int maxClips, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Rank clips by their strongest pending suggestion, take the top N.
        var rankedClipIds = await ctx.AiTagSuggestions
            .Where(s => s.State == AiSuggestionState.Pending)
            .GroupBy(s => s.VideoItemId)
            .Select(g => new { VideoItemId = g.Key, Best = g.Max(x => x.Confidence) })
            .OrderByDescending(x => x.Best)
            .Take(maxClips > 0 ? maxClips : 200)
            .Select(x => x.VideoItemId)
            .ToListAsync(cancellationToken);

        if (rankedClipIds.Count == 0) return Array.Empty<AiSuggestionGroup>();

        var suggestions = await ctx.AiTagSuggestions
            .AsNoTracking()
            .Where(s => s.State == AiSuggestionState.Pending && rankedClipIds.Contains(s.VideoItemId))
            .ToListAsync(cancellationToken);

        var clips = await ctx.VideoItems
            .AsNoTracking()
            .Where(v => rankedClipIds.Contains(v.Id))
            .Select(v => new { v.Id, v.FileName, v.FolderPath, v.FilePath, v.ThumbnailPath, v.FileExists })
            .ToDictionaryAsync(v => v.Id, cancellationToken);

        var groups = new List<AiSuggestionGroup>(rankedClipIds.Count);
        foreach (var clipId in rankedClipIds)
        {
            if (!clips.TryGetValue(clipId, out var clip)) continue;
            var items = suggestions
                .Where(s => s.VideoItemId == clipId)
                .OrderByDescending(s => s.Confidence)
                .Select(s => new AiSuggestionItem
                {
                    SuggestionId = s.Id,
                    VideoItemId = s.VideoItemId,
                    TagName = s.TagName,
                    Confidence = s.Confidence,
                    BestFrameSeconds = s.BestFrameSeconds,
                    FileName = clip.FileName,
                    FolderPath = clip.FolderPath,
                    FilePath = clip.FilePath,
                    ThumbnailPath = clip.ThumbnailPath,
                    FileExists = clip.FileExists
                })
                .ToList();

            groups.Add(new AiSuggestionGroup
            {
                VideoItemId = clipId,
                FileName = clip.FileName,
                FolderPath = clip.FolderPath,
                FilePath = clip.FilePath,
                ThumbnailPath = clip.ThumbnailPath,
                FileExists = clip.FileExists,
                Suggestions = items
            });
        }
        return groups;
    }

    public async Task<int> AcceptAsync(int suggestionId, TagType tagType, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var suggestion = await ctx.AiTagSuggestions.FirstOrDefaultAsync(s => s.Id == suggestionId, cancellationToken);
        if (suggestion == null) return 0;

        var tagId = await GetOrCreateTagAsync(ctx, suggestion.TagName, tagType, cancellationToken).ConfigureAwait(false);
        await AttachTagAsync(ctx, suggestion.VideoItemId, tagId, cancellationToken).ConfigureAwait(false);

        suggestion.State = AiSuggestionState.Accepted;
        suggestion.Approved = true;
        await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return tagId;
    }

    public async Task RejectAsync(int suggestionId, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var suggestion = await ctx.AiTagSuggestions.FirstOrDefaultAsync(s => s.Id == suggestionId, cancellationToken);
        if (suggestion == null) return;
        suggestion.State = AiSuggestionState.Rejected;
        suggestion.Approved = false;
        await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task AcceptAllForClipAsync(int videoItemId, TagType tagType, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var pending = await ctx.AiTagSuggestions
            .Where(s => s.VideoItemId == videoItemId && s.State == AiSuggestionState.Pending)
            .ToListAsync(cancellationToken);
        if (pending.Count == 0) return;

        foreach (var suggestion in pending)
        {
            var tagId = await GetOrCreateTagAsync(ctx, suggestion.TagName, tagType, cancellationToken).ConfigureAwait(false);
            await AttachTagAsync(ctx, videoItemId, tagId, cancellationToken).ConfigureAwait(false);
            suggestion.State = AiSuggestionState.Accepted;
            suggestion.Approved = true;
        }
        await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RejectAllForClipAsync(int videoItemId, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var pending = await ctx.AiTagSuggestions
            .Where(s => s.VideoItemId == videoItemId && s.State == AiSuggestionState.Pending)
            .ToListAsync(cancellationToken);
        foreach (var s in pending)
        {
            s.State = AiSuggestionState.Rejected;
            s.Approved = false;
        }
        if (pending.Count > 0) await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> GetOrCreateTagAsync(VideoArchiveDbContext ctx, string name, TagType type, CancellationToken cancellationToken)
    {
        var normalized = name.Trim();
        var existing = await ctx.Tags.FirstOrDefaultAsync(t => t.Name == normalized && t.Type == type, cancellationToken);
        if (existing != null) return existing.Id;

        var tag = new Tag { Name = normalized, Type = type };
        ctx.Tags.Add(tag);
        await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return tag.Id;
    }

    private static async Task AttachTagAsync(VideoArchiveDbContext ctx, int videoItemId, int tagId, CancellationToken cancellationToken)
    {
        var exists = await ctx.VideoTags.AnyAsync(vt => vt.VideoItemId == videoItemId && vt.TagId == tagId, cancellationToken);
        if (exists) return;
        ctx.VideoTags.Add(new VideoTag { VideoItemId = videoItemId, TagId = tagId });
    }
}
