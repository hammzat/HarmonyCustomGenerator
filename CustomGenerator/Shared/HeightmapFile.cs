using System;
using System.IO;
using System.IO.Compression;

namespace CustomGenerator.Shared
{
    // A heightmap image as values 0..1: grayscale PNG (8 or 16 bit; color PNGs are averaged) or RAW (16-bit little
    // endian like Unity, World Machine and Gaea export, or 8-bit), square. Black = lowest, white = highest.
    // PNG rows go from north (top of the picture) to south; RAW rows go from south to north, as Unity reads them.
    // No Unity in here: the mod reads it for generation, the launcher for uploads and the preview.
    // This file is compiled into CustomGenerator and CustomGenerator.Launcher.
    public sealed class HeightmapFile
    {
        public int Width, Height;
        public int Bits;
        // Row-major, row 0 = south
        public float[] Values;

        public static readonly string[] Extensions = { ".png", ".raw", ".r16" };

        public static HeightmapFile Read(byte[] data, string name) {
            string ext = Path.GetExtension(name ?? "").ToLowerInvariant();
            if (ext == ".png") return Png(data);
            if (ext == ".raw" || ext == ".r16") return Raw(data);
            throw new FormatException($"{name}: only .png, .raw and .r16 heightmaps are supported");
        }

        // Bilinear value at map coordinates 0..1 (u east, v north)
        public float Sample(float u, float v) {
            float x = Clamp01(u) * (Width - 1), y = Clamp01(v) * (Height - 1);
            int x0 = (int)x, y0 = (int)y, x1 = Math.Min(x0 + 1, Width - 1), y1 = Math.Min(y0 + 1, Height - 1);
            float fx = x - x0, fy = y - y0;
            float a = Values[y0 * Width + x0], b = Values[y0 * Width + x1], c = Values[y1 * Width + x0], d = Values[y1 * Width + x1];
            return (a + (b - a) * fx) * (1f - fy) + (c + (d - c) * fx) * fy;
        }

        // Fills a res x res heightmap in meters: black = min, white = max
        public void Fill(float[] heights, int res, float min, float max) {
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    heights[z * res + x] = min + (max - min) * Sample(x / (float)(res - 1), z / (float)(res - 1));
        }

        private static HeightmapFile Raw(byte[] data) {
            int side16 = (int)Math.Round(Math.Sqrt(data.Length / 2.0)), side8 = (int)Math.Round(Math.Sqrt(data.Length));
            var map = new HeightmapFile();
            if (data.Length % 2 == 0 && side16 * side16 * 2 == data.Length && side16 >= 2) {
                map.Width = map.Height = side16; map.Bits = 16;
                map.Values = new float[side16 * side16];
                for (int i = 0; i < map.Values.Length; i++) map.Values[i] = (data[i * 2] | data[i * 2 + 1] << 8) / 65535f;
            } else if (side8 * side8 == data.Length && side8 >= 2) {
                map.Width = map.Height = side8; map.Bits = 8;
                map.Values = new float[data.Length];
                for (int i = 0; i < data.Length; i++) map.Values[i] = data[i] / 255f;
            } else {
                throw new FormatException($"A RAW heightmap must be square, 8 or 16 bit: {data.Length} bytes isn't");
            }
            return map;
        }

        private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        private static HeightmapFile Png(byte[] data) {
            for (int i = 0; i < 8; i++) if (data.Length < 8 || data[i] != Signature[i]) throw new FormatException("Not a PNG file");
            int width = 0, height = 0, depth = 0, color = 0, interlace = 0;
            byte[] palette = null;
            var idat = new MemoryStream();
            for (int pos = 8; pos + 8 <= data.Length;) {
                int length = data[pos] << 24 | data[pos + 1] << 16 | data[pos + 2] << 8 | data[pos + 3];
                string type = System.Text.Encoding.ASCII.GetString(data, pos + 4, 4);
                int body = pos + 8;
                if (length < 0 || body + length > data.Length) throw new FormatException("The PNG file is cut off");
                if (type == "IHDR") {
                    width = BigEndian(data, body); height = BigEndian(data, body + 4);
                    depth = data[body + 8]; color = data[body + 9]; interlace = data[body + 12];
                } else if (type == "PLTE") {
                    palette = new byte[length];
                    Buffer.BlockCopy(data, body, palette, 0, length);
                } else if (type == "IDAT") {
                    idat.Write(data, body, length);
                } else if (type == "IEND") break;
                pos = body + length + 4;
            }
            if (width <= 1 || height <= 1) throw new FormatException("The PNG has no size");
            if (interlace != 0) throw new FormatException("Interlaced PNGs aren't supported, save it without interlacing");
            int channels = color switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => throw new FormatException($"Unknown PNG color type {color}") };
            if (color == 3 ? depth != 8 : depth != 8 && depth != 16) throw new FormatException($"Only 8 and 16 bit PNGs are supported (this one is {depth} bit)");
            if ((long)width * height > 8192L * 8192L) throw new FormatException($"{width}x{height} is too big, 8192x8192 at most");

            int bpp = channels * depth / 8, stride = width * bpp;
            var raw = new byte[(stride + 1) * height];
            idat.Position = 2;  // zlib header
            using (var inflate = new DeflateStream(idat, CompressionMode.Decompress)) {
                int read = 0;
                while (read < raw.Length) {
                    int n = inflate.Read(raw, read, raw.Length - read);
                    if (n <= 0) throw new FormatException("The PNG image data is cut off");
                    read += n;
                }
            }

            var map = new HeightmapFile { Width = width, Height = height, Bits = depth, Values = new float[width * height] };
            var line = new byte[stride];
            var previous = new byte[stride];
            for (int y = 0; y < height; y++) {
                int filter = raw[y * (stride + 1)];
                Buffer.BlockCopy(raw, y * (stride + 1) + 1, line, 0, stride);
                Unfilter(filter, line, previous, bpp);
                // Picture top = north = last row
                int row = (height - 1 - y) * width;
                for (int x = 0; x < width; x++) {
                    float sum = 0f;
                    int gray = color == 0 || color == 4 || color == 3 ? 1 : 3;
                    for (int c = 0; c < gray; c++) {
                        int at = x * bpp + c * depth / 8;
                        if (color == 3) {
                            int index = line[x] * 3;
                            sum = palette != null && index + 2 < palette.Length ? (palette[index] + palette[index + 1] + palette[index + 2]) / 765f : 0f;
                            gray = 1;
                            break;
                        }
                        sum += depth == 16 ? (line[at] << 8 | line[at + 1]) / 65535f : line[at] / 255f;
                    }
                    map.Values[row + x] = sum / gray;
                }
                var swap = previous; previous = line; line = swap;
            }
            return map;
        }

        private static void Unfilter(int filter, byte[] line, byte[] up, int bpp) {
            for (int i = 0; i < line.Length; i++) {
                int a = i >= bpp ? line[i - bpp] : 0, b = up[i], c = i >= bpp ? up[i - bpp] : 0;
                switch (filter) {
                    case 0: break;
                    case 1: line[i] = (byte)(line[i] + a); break;
                    case 2: line[i] = (byte)(line[i] + b); break;
                    case 3: line[i] = (byte)(line[i] + ((a + b) >> 1)); break;
                    case 4: {
                        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                        line[i] = (byte)(line[i] + (pa <= pb && pa <= pc ? a : pb <= pc ? b : c));
                        break;
                    }
                    default: throw new FormatException($"Bad PNG filter {filter}");
                }
            }
        }

        private static int BigEndian(byte[] d, int i) => d[i] << 24 | d[i + 1] << 16 | d[i + 2] << 8 | d[i + 3];
        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
