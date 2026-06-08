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
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VideoArchiveManager.Core.Services.Ai;

// Data-driven description of a CLIP ONNX model bundle. Lives as manifest.json
// next to the .onnx files so the exact tensor names, image size, and CLIP
// normalization constants can vary per export without code changes. Every
// field has a sensible default for an OpenAI CLIP ViT-B/32 export (as produced
// by HuggingFace optimum), so a bundle can ship without a manifest at all.
public class ClipModelManifest
{
    [JsonPropertyName("modelId")]
    public string ModelId { get; set; } = "clip-vit-b32";

    [JsonPropertyName("imageEncoderFile")]
    public string ImageEncoderFile { get; set; } = "image_encoder.onnx";

    [JsonPropertyName("textEncoderFile")]
    public string TextEncoderFile { get; set; } = "text_encoder.onnx";

    // Standard OpenAI CLIP BPE vocabulary (gzip-compressed text of merges).
    // For a bert-wordpiece bundle this is the plain BERT vocab.txt instead.
    [JsonPropertyName("vocabFile")]
    public string VocabFile { get; set; } = "bpe_simple_vocab_16e6.txt.gz";

    // Selects how the query/caption text is turned into token ids:
    //   "clip-bpe"        -> OpenAI CLIP byte-level BPE (the English model)
    //   "bert-wordpiece"  -> multilingual BERT WordPiece (the multilingual model)
    // Defaults to clip-bpe so older English bundles (which predate this field)
    // keep working unchanged.
    [JsonPropertyName("tokenizerType")]
    public string TokenizerType { get; set; } = "clip-bpe";

    // Whether the WordPiece tokenizer lowercases before tokenizing. The
    // multilingual model is cased (do_lower_case = false). Ignored for clip-bpe.
    [JsonPropertyName("tokenizerLowerCase")]
    public bool TokenizerLowerCase { get; set; } = false;

    [JsonPropertyName("imageSize")]
    public int ImageSize { get; set; } = 224;

    [JsonPropertyName("embeddingDim")]
    public int EmbeddingDim { get; set; } = 512;

    [JsonPropertyName("contextLength")]
    public int ContextLength { get; set; } = 77;

    [JsonPropertyName("imageInputName")]
    public string ImageInputName { get; set; } = "pixel_values";

    [JsonPropertyName("imageOutputName")]
    public string ImageOutputName { get; set; } = "image_embeds";

    [JsonPropertyName("textInputIdsName")]
    public string TextInputIdsName { get; set; } = "input_ids";

    [JsonPropertyName("textAttentionMaskName")]
    public string TextAttentionMaskName { get; set; } = "attention_mask";

    [JsonPropertyName("textOutputName")]
    public string TextOutputName { get; set; } = "text_embeds";

    // CLIP image normalization (after scaling pixels to [0,1]).
    [JsonPropertyName("imageMean")]
    public float[] ImageMean { get; set; } = { 0.48145466f, 0.4578275f, 0.40821073f };

    [JsonPropertyName("imageStd")]
    public float[] ImageStd { get; set; } = { 0.26862954f, 0.26130258f, 0.27577711f };

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static ClipModelManifest LoadOrDefault(string? manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
            return new ClipModelManifest();
        try
        {
            var json = File.ReadAllText(manifestPath);
            return JsonSerializer.Deserialize<ClipModelManifest>(json, Options) ?? new ClipModelManifest();
        }
        catch
        {
            return new ClipModelManifest();
        }
    }
}
