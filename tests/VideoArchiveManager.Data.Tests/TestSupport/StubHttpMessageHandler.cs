// Find That Shot - organize and search a large local video archive.
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Ingve Moss Liknes <findthatshot@ingve.no>
using System.Net;

namespace VideoArchiveManager.Data.Tests.TestSupport;

// Minimal HttpMessageHandler that returns a canned JSON body and counts how
// many requests reached the network — so a test can prove caching short-circuits
// the second lookup.
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly string _json;
    private readonly HttpStatusCode _status;

    public int RequestCount { get; private set; }
    public Uri? LastRequestUri { get; private set; }

    public StubHttpMessageHandler(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        _json = json;
        _status = status;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestCount++;
        LastRequestUri = request.RequestUri;
        return Task.FromResult(new HttpResponseMessage(_status)
        {
            Content = new StringContent(_json, System.Text.Encoding.UTF8, "application/json"),
        });
    }
}
