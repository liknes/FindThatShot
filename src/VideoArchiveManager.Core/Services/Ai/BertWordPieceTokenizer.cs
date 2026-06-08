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
using Microsoft.ML.Tokenizers;

namespace VideoArchiveManager.Core.Services.Ai;

// BERT WordPiece tokenizer for the multilingual text encoder
// (sentence-transformers/clip-ViT-B-32-multilingual-v1, a multilingual
// DistilBERT distilled to match the CLIP ViT-B/32 image space). Wraps the
// well-tested Microsoft.ML.Tokenizers BertTokenizer so we don't hand-roll
// WordPiece + the multilingual BERT pre-tokenization rules. Produces the
// [CLS] … [SEP] (+[PAD]) id sequence and attention mask the DistilBERT ONNX
// session expects, padded to a fixed context length.
public sealed class BertWordPieceTokenizer : IClipTokenizer
{
    private readonly BertTokenizer _tokenizer;
    private readonly int _contextLength;
    private readonly long _padId;
    private readonly long _sepId;

    // vocabFilePath is the standard BERT vocab.txt (one token per line); the
    // multilingual model uses the 119547-entry bert-base-multilingual-cased
    // vocabulary. lowerCase mirrors the model's do_lower_case (false for the
    // cased multilingual model).
    public BertWordPieceTokenizer(string vocabFilePath, int contextLength, bool lowerCase)
    {
        if (!File.Exists(vocabFilePath))
            throw new FileNotFoundException("BERT WordPiece vocabulary not found.", vocabFilePath);

        _contextLength = contextLength > 0 ? contextLength : 64;

        var options = new BertOptions
        {
            // Multilingual BERT (cased): no lowercasing, keep accents, split CJK
            // into individual characters, and run the basic punctuation/space
            // pre-tokenizer before WordPiece.
            LowerCaseBeforeTokenization = lowerCase,
            ApplyBasicTokenization = true,
            IndividuallyTokenizeCjk = true,
            RemoveNonSpacingMarks = false
        };

        _tokenizer = BertTokenizer.Create(vocabFilePath, options);
        _padId = _tokenizer.PaddingTokenId;
        _sepId = _tokenizer.SeparatorTokenId;
    }

    public (long[] InputIds, long[] AttentionMask) Tokenize(string text)
    {
        // EncodeToIds adds the [CLS] … [SEP] special tokens for us.
        var ids = _tokenizer.EncodeToIds(text ?? string.Empty);

        var inputIds = new long[_contextLength];
        var mask = new long[_contextLength];

        // Default everything to padding; mask stays 0 for pad positions so the
        // model's mean pooling ignores them.
        for (var i = 0; i < _contextLength; i++) inputIds[i] = _padId;

        var count = Math.Min(ids.Count, _contextLength);
        for (var i = 0; i < count; i++)
        {
            inputIds[i] = ids[i];
            mask[i] = 1;
        }

        // If the caption was longer than the context window, keep the sequence
        // terminated with [SEP] so it still looks like a complete BERT input.
        if (ids.Count > _contextLength)
            inputIds[_contextLength - 1] = _sepId;

        return (inputIds, mask);
    }
}
