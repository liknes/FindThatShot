namespace VideoArchiveManager.Core.Services.Ai;

// A loaded CLIP model (image + text encoders sharing one embedding space).
// Both encode methods return L2-normalized vectors, so similarity between any
// image and any text is just their dot product.
public interface IClipModel : IDisposable
{
    string ModelId { get; }

    int EmbeddingDim { get; }

    // Side length (px) of the square RGB frame the image encoder expects.
    int ImageSize { get; }

    // Encodes a single decoded frame supplied as tightly-packed RGB24 bytes
    // (length == ImageSize * ImageSize * 3, row-major, 3 bytes per pixel).
    float[] EncodeImage(ReadOnlySpan<byte> rgb24);

    // Encodes a caption / search phrase into the shared embedding space.
    float[] EncodeText(string text);
}
