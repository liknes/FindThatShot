namespace VideoArchiveManager.Core.Services.Ai;

// One decoded frame: its position in the clip plus tightly-packed RGB24 pixels
// at the model's square input size (size*size*3 bytes).
public readonly record struct SampledFrame(double TimeSeconds, byte[] Rgb24);

// Decodes N evenly-spaced frames across a clip via ffmpeg, scaled+center-cropped
// to the requested square size and handed back as raw RGB. The source file is
// only ever read, never modified.
public interface IFrameSampler
{
    Task<IReadOnlyList<SampledFrame>> SampleAsync(
        string videoFilePath,
        double? durationSeconds,
        int frameCount,
        int size,
        CancellationToken cancellationToken = default);
}
