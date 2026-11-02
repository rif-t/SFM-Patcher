using ModernSfmPatcher.FramePipeline;

var input = new Frame(2, 1, PixelComponentType.UInt8, ChannelLayout.RGBA, 8,
    ColorSpace.SRgb, AlphaMode.Straight, RenderPass.Color,
    [10, 20, 30, 255, 100, 110, 120, 128]);
var output = new FramePipeline([new InvertRgb8Stage()]).Process(input);
Console.WriteLine($"{output.Width}x{output.Height} {output.ChannelLayout}: {string.Join(' ', output.Pixels.ToArray())}");
