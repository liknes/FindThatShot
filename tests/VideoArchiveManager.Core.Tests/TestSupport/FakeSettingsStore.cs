// Find That Shot - organize and search a large local video archive.
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Ingve Moss Liknes <findthatshot@ingve.no>
using VideoArchiveManager.Core.Configuration;

namespace VideoArchiveManager.Core.Tests.TestSupport;

// In-memory ISettingsStore for tests: hands back a mutable AppSettings instance
// the test can configure (thumbnail dir, sidecar toggle, db/backup paths) with
// no file I/O.
public sealed class FakeSettingsStore : ISettingsStore
{
    public AppSettings Current { get; }

    public FakeSettingsStore(AppSettings? settings = null) => Current = settings ?? new AppSettings();

    public AppSettings Load() => Current;

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
