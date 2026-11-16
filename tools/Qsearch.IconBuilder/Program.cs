namespace Qsearch.IconBuilder;

public static class Program
{
    private static readonly int[] Sizes = [16, 20, 24, 32, 48, 64, 128, 256];

    public static int Main(string[] args)
    {
        if (args.Length is < 1 or > 2)
        {
            Console.Error.WriteLine("Usage: Qsearch.IconBuilder <output.ico> [brand|dark|light]");
            return 2;
        }

        string outputPath = Path.GetFullPath(args[0]);
        IconTheme theme = args.Length == 1
            ? IconTheme.Brand
            : Enum.Parse<IconTheme>(args[1], ignoreCase: true);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        WriteIcon(outputPath, theme);
        Console.WriteLine(outputPath);
        return 0;
    }

    private static void WriteIcon(string outputPath, IconTheme theme)
    {
        byte[][] frames = Sizes.Select(size => RenderFrame(size, theme)).ToArray();
        using FileStream stream = File.Create(outputPath);
        using BinaryWriter writer = new(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)frames.Length);

        int offset = 6 + (16 * frames.Length);
        for (int index = 0; index < frames.Length; index++)
        {
            int size = Sizes[index];
            writer.Write(size == 256 ? (byte)0 : (byte)size);
            writer.Write(size == 256 ? (byte)0 : (byte)size);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write((uint)frames[index].Length);
            writer.Write((uint)offset);
            offset += frames[index].Length;
        }

        foreach (byte[] frame in frames)
        {
            writer.Write(frame);
        }
    }

    private static byte[] RenderFrame(int size, IconTheme theme)
    {
        byte[] pixels = RenderPixels(size, theme);
        int maskStride = ((size + 31) / 32) * 4;
        byte[] mask = new byte[maskStride * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int pixelOffset = (((size - 1 - y) * size) + x) * 4;
                if (pixels[pixelOffset + 3] == 0)
                {
                    int maskOffset = ((size - 1 - y) * maskStride) + (x / 8);
                    mask[maskOffset] |= (byte)(0x80 >> (x % 8));
                }
            }
        }

        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        writer.Write((uint)40);
        writer.Write(size);
        writer.Write(size * 2);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write((uint)0);
        writer.Write((uint)pixels.Length);
        writer.Write(0);
        writer.Write(0);
        writer.Write((uint)0);
        writer.Write((uint)0);
        writer.Write(pixels);
        writer.Write(mask);
        return stream.ToArray();
    }

    private static byte[] RenderPixels(int size, IconTheme theme)
    {
        const int samples = 4;
        byte[] pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double alpha = 0;
                double red = 0;
                double green = 0;
                double blue = 0;
                for (int sampleY = 0; sampleY < samples; sampleY++)
                {
                    for (int sampleX = 0; sampleX < samples; sampleX++)
                    {
                        double unitX = (x + ((sampleX + 0.5) / samples)) * 48 / size;
                        double unitY = (y + ((sampleY + 0.5) / samples)) * 48 / size;
                        Rgba color = Sample(unitX, unitY, theme);
                        alpha += color.A;
                        red += color.R * color.A;
                        green += color.G * color.A;
                        blue += color.B * color.A;
                    }
                }

                double divisor = samples * samples;
                byte outputAlpha = (byte)Math.Round(alpha / divisor);
                int offset = (((size - 1 - y) * size) + x) * 4;
                if (outputAlpha > 0 && alpha > 0)
                {
                    pixels[offset] = (byte)Math.Round(blue / alpha);
                    pixels[offset + 1] = (byte)Math.Round(green / alpha);
                    pixels[offset + 2] = (byte)Math.Round(red / alpha);
                    pixels[offset + 3] = outputAlpha;
                }
            }
        }

        return pixels;
    }

    private static Rgba Sample(double x, double y, IconTheme theme)
    {
        Rgba foreground = theme switch
        {
            IconTheme.Light => new Rgba(8, 119, 50, 255),
            _ => new Rgba(29, 185, 84, 255),
        };

        const double center = 24;
        double radius = Distance(x, y, center, center);
        if (radius <= 151d * 48 / 700)
        {
            return foreground;
        }

        if (radius < 196d * 48 / 700 || radius > 305d * 48 / 700)
        {
            return default;
        }

        double angle = (Math.Atan2(y - center, x - center) * 180 / Math.PI + 360) % 360;
        bool insideSegment =
            angle is >= 233 and <= 307 or
            >= 323 and <= 360 or
            >= 0 and <= 26 or
            >= 54 and <= 127 or
            >= 143 and <= 217;
        return insideSegment ? foreground : default;
    }

    private static double Distance(double x, double y, double centerX, double centerY)
    {
        double deltaX = x - centerX;
        double deltaY = y - centerY;
        return Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
    }

    private readonly record struct Rgba(byte R, byte G, byte B, byte A);

    private enum IconTheme
    {
        Brand,
        Dark,
        Light,
    }
}
