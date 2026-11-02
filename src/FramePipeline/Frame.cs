namespace SfmPatcher.FramePipeline;

public enum PixelComponentType { UInt8, UInt16, Float16, Float32 }
public enum ChannelLayout { Gray, GrayAlpha, RGB, BGR, RGBA, BGRA }
public enum ColorSpace { Unknown, Linear, SRgb, Rec709 }
public enum AlphaMode { None, Straight, Premultiplied }
public enum RenderPass { Color, Depth, Normal, Auxiliary }

public sealed class Frame
{
    private readonly byte[] _pixels;

    // The caller transfers ownership of pixels; it must not mutate or reuse the array.
    public Frame(int width, int height, PixelComponentType componentType,
        ChannelLayout channelLayout, int rowStride, ColorSpace colorSpace,
        AlphaMode alphaMode, RenderPass renderPass, byte[] pixels)
    {
        Width = width;
        Height = height;
        ComponentType = componentType;
        ChannelLayout = channelLayout;
        RowStride = rowStride;
        ColorSpace = colorSpace;
        AlphaMode = alphaMode;
        RenderPass = renderPass;
        _pixels = pixels ?? throw new ArgumentNullException(nameof(pixels));
        Validate();
    }

    public int Width { get; }
    public int Height { get; }
    public PixelComponentType ComponentType { get; }
    public ChannelLayout ChannelLayout { get; }
    public int RowStride { get; }
    public ColorSpace ColorSpace { get; }
    public AlphaMode AlphaMode { get; }
    public RenderPass RenderPass { get; }
    public ReadOnlyMemory<byte> Pixels => _pixels;
    public int ChannelCount => ChannelLayout switch
    {
        ChannelLayout.Gray => 1,
        ChannelLayout.GrayAlpha => 2,
        ChannelLayout.RGB or ChannelLayout.BGR => 3,
        ChannelLayout.RGBA or ChannelLayout.BGRA => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(ChannelLayout))
    };
    public int BytesPerComponent => ComponentType switch
    {
        PixelComponentType.UInt8 => 1,
        PixelComponentType.UInt16 or PixelComponentType.Float16 => 2,
        PixelComponentType.Float32 => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(ComponentType))
    };

    public void Validate()
    {
        if (Width <= 0 || Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(Width), "Frame dimensions must be positive.");

        int rowBytes;
        int requiredBytes;
        try
        {
            rowBytes = checked(Width * ChannelCount * BytesPerComponent);
            requiredBytes = checked(RowStride * Height);
        }
        catch (OverflowException ex)
        {
            throw new ArgumentException("Frame dimensions or layout exceed supported memory sizes.", ex);
        }

        if (RowStride < rowBytes)
            throw new ArgumentException("Row stride is smaller than the packed pixel row.", nameof(RowStride));
        if (_pixels.Length < requiredBytes)
            throw new ArgumentException("Pixel buffer is shorter than row stride × height.", nameof(_pixels));
        if ((ChannelLayout is ChannelLayout.Gray or ChannelLayout.RGB or ChannelLayout.BGR) != (AlphaMode == AlphaMode.None))
            throw new ArgumentException("Alpha mode must match the channel layout.", nameof(AlphaMode));
    }

    internal byte[] CopyPixels() => (byte[])_pixels.Clone();
}

public interface IFrameStage
{
    Frame Process(Frame input);
}

public sealed class FramePipeline(IEnumerable<IFrameStage> stages)
{
    private readonly IFrameStage[] _stages = stages?.ToArray() ?? throw new ArgumentNullException(nameof(stages));

    public Frame Process(Frame input)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.Validate();
        var current = input;
        foreach (var stage in _stages)
        {
            current = stage.Process(current) ?? throw new InvalidOperationException("A processing stage returned no frame.");
            current.Validate();
        }
        return current;
    }
}

public sealed class InvertRgb8Stage : IFrameStage
{
    public Frame Process(Frame input)
    {
        input.Validate();
        if (input.RenderPass != RenderPass.Color || input.ComponentType != PixelComponentType.UInt8 ||
            input.ChannelLayout is not (ChannelLayout.RGB or ChannelLayout.BGR or ChannelLayout.RGBA or ChannelLayout.BGRA))
            throw new NotSupportedException("RGB inversion supports UInt8 color frames with three or four color channels.");

        var pixels = input.CopyPixels();
        var channels = input.ChannelCount;
        for (var y = 0; y < input.Height; y++)
        {
            var row = y * input.RowStride;
            for (var x = 0; x < input.Width; x++)
            {
                var pixel = row + x * channels;
                for (var c = 0; c < 3; c++)
                    pixels[pixel + c] = (byte)(255 - pixels[pixel + c]);
            }
        }

        return new Frame(input.Width, input.Height, input.ComponentType, input.ChannelLayout,
            input.RowStride, input.ColorSpace, input.AlphaMode, input.RenderPass, pixels);
    }
}
