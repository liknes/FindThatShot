using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace VideoArchiveManager.Core.Services.Ai;

// CLIP inference over two ONNX sessions (image + text encoders) using the CPU
// ONNX Runtime. Preprocessing (CLIP normalization for images, BPE tokenization
// for text) happens here so callers just hand over raw RGB bytes / strings.
public sealed class ClipOnnxModel : IClipModel
{
    private readonly ClipModelManifest _manifest;
    private readonly ClipTokenizer _tokenizer;
    private readonly InferenceSession _imageSession;
    private readonly InferenceSession _textSession;
    private readonly object _imageLock = new();
    private readonly object _textLock = new();

    public ClipOnnxModel(string modelDirectory)
    {
        var manifestPath = Path.Combine(modelDirectory, "manifest.json");
        _manifest = ClipModelManifest.LoadOrDefault(manifestPath);

        var imagePath = Path.Combine(modelDirectory, _manifest.ImageEncoderFile);
        var textPath = Path.Combine(modelDirectory, _manifest.TextEncoderFile);
        var vocabPath = Path.Combine(modelDirectory, _manifest.VocabFile);

        if (!File.Exists(imagePath))
            throw new FileNotFoundException("CLIP image encoder not found.", imagePath);
        if (!File.Exists(textPath))
            throw new FileNotFoundException("CLIP text encoder not found.", textPath);

        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL
        };

        _imageSession = new InferenceSession(imagePath, options);
        _textSession = new InferenceSession(textPath, options);
        _tokenizer = new ClipTokenizer(vocabPath);
    }

    public string ModelId => _manifest.ModelId;

    public int EmbeddingDim => _manifest.EmbeddingDim;

    public int ImageSize => _manifest.ImageSize;

    public float[] EncodeImage(ReadOnlySpan<byte> rgb24)
    {
        var size = _manifest.ImageSize;
        var expected = size * size * 3;
        if (rgb24.Length < expected)
            throw new ArgumentException($"Expected {expected} RGB bytes, got {rgb24.Length}.", nameof(rgb24));

        // RGB24 (HWC, interleaved) -> normalized CHW float tensor [1,3,H,W].
        var tensor = new DenseTensor<float>(new[] { 1, 3, size, size });
        var mean = _manifest.ImageMean;
        var std = _manifest.ImageStd;
        var plane = size * size;
        var buffer = tensor.Buffer.Span;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var px = (y * size + x) * 3;
                var pos = y * size + x;
                buffer[pos] = (rgb24[px] / 255f - mean[0]) / std[0];
                buffer[plane + pos] = (rgb24[px + 1] / 255f - mean[1]) / std[1];
                buffer[2 * plane + pos] = (rgb24[px + 2] / 255f - mean[2]) / std[2];
            }
        }

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(_manifest.ImageInputName, tensor)
        };

        lock (_imageLock)
        {
            using var results = _imageSession.Run(inputs);
            return ExtractEmbedding(results, _manifest.ImageOutputName);
        }
    }

    public float[] EncodeText(string text)
    {
        var ctx = _manifest.ContextLength;
        var (ids, mask) = _tokenizer.Tokenize(text ?? string.Empty, ctx);

        var idTensor = new DenseTensor<long>(ids, new[] { 1, ctx });
        var maskTensor = new DenseTensor<long>(mask, new[] { 1, ctx });

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(_manifest.TextInputIdsName, idTensor)
        };

        // Some exports drop the attention mask input; only feed it if present.
        if (_textSession.InputMetadata.ContainsKey(_manifest.TextAttentionMaskName))
        {
            inputs.Add(NamedOnnxValue.CreateFromTensor(_manifest.TextAttentionMaskName, maskTensor));
        }

        lock (_textLock)
        {
            using var results = _textSession.Run(inputs);
            return ExtractEmbedding(results, _manifest.TextOutputName);
        }
    }

    // Pulls the embedding tensor out of the session outputs by name (falling
    // back to the first output if the configured name isn't present), copies it
    // to a float[], and L2-normalizes so similarity is a dot product.
    private float[] ExtractEmbedding(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results, string outputName)
    {
        DisposableNamedOnnxValue? match = null;
        foreach (var r in results)
        {
            if (r.Name == outputName) { match = r; break; }
        }
        match ??= results.First();

        var tensor = match.AsTensor<float>();
        var vector = tensor.ToArray();
        VectorMath.L2NormalizeInPlace(vector);
        return vector;
    }

    public void Dispose()
    {
        _imageSession.Dispose();
        _textSession.Dispose();
    }
}
