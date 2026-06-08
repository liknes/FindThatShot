// Find That Shot - organize and search a large local video archive.
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Ingve Moss Liknes <findthatshot@ingve.no>
using FluentAssertions;
using VideoArchiveManager.Core.Services.Ai;
using VideoArchiveManager.Core.Tests.TestSupport;
using Xunit;

namespace VideoArchiveManager.Core.Tests;

public class ClipTokenizerTests
{
    private const int Bos = 49406;
    private const int Eos = 49407;

    [Fact]
    public void Constructor_throws_when_vocab_file_is_missing()
    {
        var act = () => new ClipTokenizer(Path.Combine(Path.GetTempPath(), "no-such-vocab.txt"));
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void Tokenize_emits_bos_eos_and_pads_to_context_length()
    {
        using var dir = new TempDirectory();
        // A minimal but valid merges file: the loader skips the first (header)
        // line, so a header plus no merges still builds the 256+256+2 byte/
        // special vocabulary the tokenizer needs.
        var vocabPath = dir.CreateFile("bpe.txt", System.Text.Encoding.UTF8.GetBytes("#version: 0.2\n"));

        var tokenizer = new ClipTokenizer(vocabPath);

        const int contextLength = 16;
        var (ids, mask) = tokenizer.Tokenize("hello world", contextLength);

        ids.Should().HaveCount(contextLength);
        mask.Should().HaveCount(contextLength);

        ids[0].Should().Be(Bos, "the sequence always starts with the start-of-text token");
        ids.Should().Contain(Eos, "the sequence always contains the end-of-text token");

        var eosIndex = Array.IndexOf(ids, (long)Eos);
        // Mask is 1 up to and including EOS, then 0.
        for (var i = 0; i <= eosIndex; i++) mask[i].Should().Be(1);
        for (var i = eosIndex + 1; i < contextLength; i++)
        {
            mask[i].Should().Be(0);
            ids[i].Should().Be(0, "positions after EOS are zero-padded");
        }
    }
}
