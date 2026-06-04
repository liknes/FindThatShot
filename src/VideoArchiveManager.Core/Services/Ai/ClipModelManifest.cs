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
    [JsonPropertyName("vocabFile")]
    public string VocabFile { get; set; } = "bpe_simple_vocab_16e6.txt.gz";

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
