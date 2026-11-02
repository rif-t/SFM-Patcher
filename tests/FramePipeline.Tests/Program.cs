using SfmPatcher.FramePipeline;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}

var sourceBytes = new byte[] { 10, 20, 30, 255, 100, 110, 120, 128, 77, 88, 99, 111 };
var source = new Frame(1, 2, PixelComponentType.UInt8, ChannelLayout.RGBA, 8,
    ColorSpace.SRgb, AlphaMode.Straight, RenderPass.Color, sourceBytes);
var result = new FramePipeline([new InvertRgb8Stage()]).Process(source);
Check(result.Pixels.Span.SequenceEqual(new byte[] { 245, 235, 225, 255, 77, 88, 99, 111, 77, 88, 99, 111 }), "Pixel values, alpha, or row padding changed unexpectedly.");
Check(source.Pixels.Span.SequenceEqual(sourceBytes), "Processing mutated its input.");
Check(result.RowStride == 8 && result.Width == 1 && result.Height == 2, "Frame metadata was not preserved.");
Throws<ArgumentOutOfRangeException>(() => new Frame(0, 1, PixelComponentType.UInt8, ChannelLayout.RGB, 3,
    ColorSpace.SRgb, AlphaMode.None, RenderPass.Color, new byte[3]));
Throws<ArgumentException>(() => new Frame(1, 2, PixelComponentType.UInt8, ChannelLayout.RGB, 2,
    ColorSpace.SRgb, AlphaMode.None, RenderPass.Color, new byte[4]));
Throws<ArgumentException>(() => new Frame(1, 2, PixelComponentType.Float32, ChannelLayout.RGBA, 16,
    ColorSpace.Linear, AlphaMode.Straight, RenderPass.Color, new byte[16]));
Throws<NotSupportedException>(() => new FramePipeline([new InvertRgb8Stage()]).Process(
    new Frame(1, 1, PixelComponentType.Float32, ChannelLayout.RGBA, 16,
        ColorSpace.Linear, AlphaMode.Straight, RenderPass.Color, new byte[16])));
Console.WriteLine("FramePipeline tests passed.");
