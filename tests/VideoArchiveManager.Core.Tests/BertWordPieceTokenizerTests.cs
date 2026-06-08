// Find That Shot - organize and search a large local video archive.
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Ingve Moss Liknes <findthatshot@ingve.no>
using System.Text;
using FluentAssertions;
using VideoArchiveManager.Core.Services.Ai;
using VideoArchiveManager.Core.Tests.TestSupport;
using Xunit;

namespace VideoArchiveManager.Core.Tests;

public class BertWordPieceTokenizerTests
{
    // A tiny BERT vocab.txt: one token per line, line index = token id. The
    // first five lines are the special tokens BertTokenizer expects.
    private const int Pad = 0;  // [PAD]
    private const int Cls = 2;  // [CLS]
    private const int Sep = 3;  // [SEP]
    private const int Hello = 5;
    private const int World = 6;

    private static string WriteVocab(TempDirectory dir)
    {
        var vocab = string.Join("\n", new[]
        {
            "[PAD]", "[UNK]", "[CLS]", "[SEP]", "[MASK]", "hello", "world"
        });
        return dir.CreateFile("vocab.txt", Encoding.UTF8.GetBytes(vocab));
    }

    [Fact]
    public void Constructor_throws_when_vocab_file_is_missing()
    {
        var act = () => new BertWordPieceTokenizer(
            Path.Combine(Path.GetTempPath(), "no-such-vocab.txt"), contextLength: 8, lowerCase: false);
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void Tokenize_wraps_with_cls_sep_and_pads_to_context_length()
    {
        using var dir = new TempDirectory();
        var vocabPath = WriteVocab(dir);

        var tokenizer = new BertWordPieceTokenizer(vocabPath, contextLength: 8, lowerCase: false);

        var (ids, mask) = tokenizer.Tokenize("hello world");

        ids.Should().HaveCount(8);
        mask.Should().HaveCount(8);

        ids[0].Should().Be(Cls, "BERT input starts with [CLS]");
        ids[1].Should().Be(Hello);
        ids[2].Should().Be(World);
        ids[3].Should().Be(Sep, "the real tokens are terminated with [SEP]");

        // Mask is 1 across [CLS] hello world [SEP], then 0.
        for (var i = 0; i < 4; i++) mask[i].Should().Be(1);
        for (var i = 4; i < 8; i++)
        {
            mask[i].Should().Be(0);
            ids[i].Should().Be(Pad, "positions after [SEP] are padded with [PAD]");
        }
    }

    [Fact]
    public void Tokenize_truncates_overlong_input_and_keeps_sep_last()
    {
        using var dir = new TempDirectory();
        var vocabPath = WriteVocab(dir);

        // Context length 4 can only hold [CLS] + 2 tokens + [SEP]; a longer input
        // must be truncated but still end with [SEP].
        var tokenizer = new BertWordPieceTokenizer(vocabPath, contextLength: 4, lowerCase: false);

        var (ids, mask) = tokenizer.Tokenize("hello world hello world");

        ids.Should().HaveCount(4);
        ids[0].Should().Be(Cls);
        ids[^1].Should().Be(Sep, "a truncated sequence is still terminated with [SEP]");
        mask.Should().OnlyContain(m => m == 1, "every position is a real (non-pad) token when full");
    }
}
