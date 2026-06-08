// Find That Shot - organize and search a large local video archive.
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Ingve Moss Liknes <findthatshot@ingve.no>
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using VideoArchiveManager.Core.Models;

namespace VideoArchiveManager.Data.Tests.TestSupport;

// Forces SaveChanges to fail at exactly the moment a RootFolder row is being
// deleted. Used to prove RemoveRootFolderAsync's transaction rolls back the
// (already-executed) video deletions when the second step fails.
public sealed class ThrowOnRootFolderDeleteInterceptor : SaveChangesInterceptor
{
    public sealed class SimulatedFailureException : Exception
    {
        public SimulatedFailureException() : base("Simulated failure while deleting a root folder.") { }
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        ThrowIfDeletingRootFolder(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ThrowIfDeletingRootFolder(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void ThrowIfDeletingRootFolder(DbContext? context)
    {
        if (context is null) return;
        var deletingRoot = context.ChangeTracker.Entries<RootFolder>()
            .Any(e => e.State == EntityState.Deleted);
        if (deletingRoot) throw new SimulatedFailureException();
    }
}
