using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Drawing.Imaging;
using System.Net;
using HarmonyLib;
using UnityEngine;

using Color = UnityEngine.Color;
using Font = System.Drawing.Font;
using Graphics = System.Drawing.Graphics;
using SDFontStyle = System.Drawing.FontStyle;

using static CustomGenerator.ExtConfig;
using CustomGenerator.Utility;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace CustomGenerator.Utility {
    static class MapImage
    {
        private static Dictionary<string, string> RequirementResources = new Dictionary<string, string>() {
            {"PermanentMarker.ttf", "https://raw.githubusercontent.com/hammzat/HarmonyCustomGenerator/main/Resources/PermanentMarker.ttf"},
            {"dinpro.otf", "https://raw.githubusercontent.com/hammzat/HarmonyCustomGenerator/main/Resources/dinpro.otf"},
            {"dinprobold.otf", "https://raw.githubusercontent.com/hammzat/HarmonyCustomGenerator/main/Resources/dinprobold.otf"},
        };
        private static void CheckResources() {
            string path = Paths.Get("mapimages", "resources");
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            foreach (var resource in RequirementResources) {
                if (File.Exists(Path.Combine(path, resource.Key))) continue;

                using (var client = new WebClient()) {
                    Logging.Info($"Map image: downloading font {resource.Key}...");
                    try {
                        client.DownloadFile(resource.Value, Path.Combine(path, resource.Key));
                    }
                    catch (Exception ex) {
                        Logging.Error($"Map image: can't download {resource.Key}: {ex.Message}. Copy the fonts from the release archive (mapimages/resources/) to {path}");
                    }
                }
            }
        }
        public static void RenderMap() {
            var settings = Config.MapImage;
            if (!settings.Enabled) { Logging.Info("Map image disabled in the config"); return; }
            CheckResources();

            byte[] array = MapImageRender.Render(out int num, out int num2, out Color color, settings.Scale, false, false, settings.OceanMargin);
            if (array == null) {
                Logging.Error("Map image: the terrain isn't ready, image skipped"); return;
            }

            // Named after the saved map, whatever Override Name/Folder are
            string mapName = Path.GetFileNameWithoutExtension(World.MapFileName);
            string fullPath = Paths.Get("mapimages", mapName + ".png");
            File.WriteAllBytes(fullPath, array);
            Logging.Info($"Map image saved to {fullPath}");
            GenerationReport.Image(fullPath);
        }
    }

    // Original Facepunch Code && MJSU plugin - Rust Map Api 
    public static class MapImageRender {
        private static readonly string PermanentMarkerFont = Paths.Get("mapimages", "resources", "PermanentMarker.ttf");
        private static readonly string DinProFont = Paths.Get("mapimages", "resources", "dinpro.otf");
        private static readonly string DinProFontBold = Paths.Get("mapimages", "resources", "dinprobold.otf");
        private static readonly Vector4 StartColor = new Vector4(0.286274523f, 23f / 85f, 0.247058839f, 1f);
        private static readonly Vector4 WaterColor = new Vector4(0.16941601f, 0.317557573f, 0.362000018f, 1f);
        private static readonly Vector4 GravelColor = new Vector4(0.25f, 37f / 152f, 0.220394745f, 1f);
        private static readonly Vector4 DirtColor = new Vector4(0.6f, 0.479594618f, 0.33f, 1f);
        private static readonly Vector4 SandColor = new Vector4(0.7f, 0.65968585f, 0.5277487f, 1f);
        private static readonly Vector4 GrassColor = new Vector4(0.354863644f, 0.37f, 0.2035f, 1f);
        private static readonly Vector4 ForestColor = new Vector4(0.248437509f, 0.3f, 9f / 128f, 1f);
        private static readonly Vector4 RockColor = new Vector4(0.4f, 0.393798441f, 0.375193775f, 1f);
        private static readonly Vector4 SnowColor = new Vector4(0.862745166f, 0.9294118f, 0.941176534f, 1f);
        private static readonly Vector4 PebbleColor = new Vector4(7f / 51f, 0.2784314f, 0.2761563f, 1f);
        private static readonly Vector4 OffShoreColor = new Vector4(0.04090196f, 0.220600322f, 14f / 51f, 1f);
        private static readonly Vector3 SunDirection = Vector3.Normalize(new Vector3(0.95f, 2.87f, 2.37f));
        private static readonly Vector4 Half = new Vector4(0.5f, 0.5f, 0.5f, 0.5f);

        public readonly struct Array2D<T> {
            private readonly T[] _items;
            private readonly int _width;
            private readonly int _height;

            public ref T this[int x, int y] {
                get {
                    int num = Mathf.Clamp(x, 0, _width - 1);
                    int num2 = Mathf.Clamp(y, 0, _height - 1);
                    return ref _items[num2 * _width + num];
                }
            }

            public Array2D(T[] items, int width, int height) {
                _items = items;
                _width = width;
                _height = height;
            }

            public bool IsEmpty() => _items == null || _width == 0 && _height == 0;
            public Array2D<T> Clone() => new Array2D<T>((T[])_items.Clone(), _width, _height);
        }

        private class MapMonument {
            public string name;
            public int x = 0;
            public int y = 0;
            public Indication indication = Indication.None;
            public string imagePath = "";
        }
        private enum Indication { None = 0, Regular, Smaller, Image }

        private static FieldInfo _monuments = AccessTools.TypeByName("TerrainPath").GetField("Monuments", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        public static byte[] Render(out int imageWidth, out int imageHeight, out Color background, float scale = 0.5f, bool lossy = true, bool transparent = false, int oceanMargin = 500) {
            Logging.Info("Map image: rendering...");
            Stopwatch stopwatch = Stopwatch.StartNew();

            if (lossy && transparent)
                throw new ArgumentException("Rendering a transparent map is not possible when using lossy compression (JPG)");

            imageWidth = 0;
            imageHeight = 0;
            background = OffShoreColor;

            TerrainTexturing instance = TerrainTexturing.Instance;
            if (instance == null) return null;

            Terrain component = instance.GetComponent<Terrain>();
            TerrainMeta component2 = instance.GetComponent<TerrainMeta>();
            TerrainHeightMap terrainHeightMap = instance.GetComponent<TerrainHeightMap>();
            TerrainSplatMap terrainSplatMap = instance.GetComponent<TerrainSplatMap>();
            TerrainTopologyMap terrainTopologyMap = instance.GetComponent<TerrainTopologyMap>();

            if (component == null || component2 == null || terrainHeightMap == null || terrainSplatMap == null || terrainTopologyMap == null)
                return null;

            int mapRes = (int)((float)World.Size * Mathf.Clamp(scale, 0.1f, 4f));
            float invMapRes = 1f / (float)mapRes;

            if (mapRes <= 0) return null;

            imageWidth = mapRes + oceanMargin * 2;
            imageHeight = mapRes + oceanMargin * 2;

            Color[] array = new Color[imageWidth * imageHeight];
            Array2D<Color> output = new Array2D<Color>(array, imageWidth, imageHeight);

            float maxDepth = (transparent ? Mathf.Max(Mathf.Abs(GetHeight(0f, 0f)), 5f) : 50f);
            Vector4 offShoreColor = (transparent ? Vector4.zero : OffShoreColor);
            Vector4 waterColor = (transparent ? new Vector4(WaterColor.x, WaterColor.y, WaterColor.z, 0.5f) : WaterColor);


            Parallel.For(0, imageHeight, delegate (int y) {
                y -= oceanMargin;
                float y2 = (float)y * invMapRes;
                int num = mapRes + oceanMargin;

                for (int i = -oceanMargin; i < num; i++)
                {
                    float x2 = (float)i * invMapRes;
                    Vector4 startColor = StartColor;

                    float height = GetHeight(x2, y2);
                    Vector3 normal = GetNormal(x2, y2);
                    float shoreDist = GetShoreDist(x2, y2);
                    bool flag = (GetTopology(x2, y2) & 0x180) != 0;

                    float light = Math.Max(Vector3.Dot(normal, SunDirection), 0f);

                    startColor = Vector4.Lerp(startColor, GravelColor, GetSplat(x2, y2, 128) * GravelColor.w);
                    startColor = Vector4.Lerp(startColor, PebbleColor, GetSplat(x2, y2, 64) * PebbleColor.w);
                    startColor = Vector4.Lerp(startColor, RockColor, GetSplat(x2, y2, 8) * RockColor.w);
                    startColor = Vector4.Lerp(startColor, DirtColor, GetSplat(x2, y2, 1) * DirtColor.w);
                    startColor = Vector4.Lerp(startColor, GrassColor, GetSplat(x2, y2, 16) * GrassColor.w);
                    startColor = Vector4.Lerp(startColor, ForestColor, GetSplat(x2, y2, 32) * ForestColor.w);
                    startColor = Vector4.Lerp(startColor, SandColor, GetSplat(x2, y2, 4) * SandColor.w);
                    startColor = Vector4.Lerp(startColor, SnowColor, GetSplat(x2, y2, 2) * SnowColor.w);

                    float waterFactor = 0f;
                    if (shoreDist > 0f)
                    {
                        waterFactor = 0f - height;
                        if (waterFactor <= 0f || !flag)
                        {
                            waterFactor = Mathf.Max(waterFactor, 0.1f * shoreDist);
                        }
                    }

                    if (waterFactor > 0f) {
                        startColor = Vector4.Lerp(startColor, waterColor, Mathf.Clamp(0.5f + waterFactor / 5f, 0f, 1f));
                        startColor = Vector4.Lerp(startColor, offShoreColor, Mathf.Clamp(waterFactor / maxDepth, 0f, 1f));
                    }
                    else {
                        startColor += (light - 0.5f) * 0.65f * startColor;
                        startColor = (startColor - Half) * 0.94f + Half;
                    }

                    startColor *= 1.05f;

                    output[i + oceanMargin, y + oceanMargin] = (transparent
                        ? new Color(startColor.x, startColor.y, startColor.z, startColor.w)
                        : new Color(startColor.x, startColor.y, startColor.z));
                }
            });

            background = output[0, 0];

            DrawOverlays(array, imageWidth, imageHeight, mapRes, oceanMargin);

            Logging.Info($"Map image: rendered in {stopwatch.Elapsed.TotalSeconds:0.0}s, encoding...");
            stopwatch.Stop();

            return EncodeToFile(imageWidth, imageHeight, array, lossy);

            float GetHeight(float x, float y) => terrainHeightMap.GetHeight(x, y);
            Vector3 GetNormal(float x, float y) => terrainHeightMap.GetNormal(x, y);
            float GetSplat(float x, float y, int mask) => terrainSplatMap.GetSplat(x, y, mask);
            int GetTopology(float x, float y) => terrainTopologyMap.GetTopology(x, y, 16f);
        }

        static float GetShoreDist(float x, float y) => TerrainTexturing.Instance.GetMainlandCoarseVectorToShore(x, y).shoreDist;

        // Names, the github line and the grid all go on one bitmap: a single
        // conversion there and back instead of one per label
        private static void DrawOverlays(Color[] pixels, int imageWidth, int imageHeight, int mapResolution, int oceanMargin) {
            Stopwatch stopwatch = Stopwatch.StartNew();

            using (Bitmap bitmap = ToBitmap(pixels, imageWidth, imageHeight)) {
                using (Graphics graphics = Graphics.FromImage(bitmap)) {
                    graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    if (Config.MapImage.MonumentNames) RenderMonument(graphics, CollectMonuments(imageWidth, mapResolution, oceanMargin), PermanentMarkerFont);
                    RenderGithub(graphics, DinProFontBold, imageWidth);
                    if (Config.MapImage.Grid) RenderGrid(graphics, mapResolution, imageWidth, oceanMargin);
                }
                FromBitmap(bitmap, pixels);
            }

            stopwatch.Stop();
            Logging.Info($"Map image: labels drawn in {stopwatch.Elapsed.TotalSeconds:0.0}s");
        }

        private static List<MapMonument> CollectMonuments(int imageWidth, int mapResolution, int oceanMargin) {
            List<MonumentInfo> monuments = (List<MonumentInfo>)_monuments.GetValue(tempData.terrainPath);

            var originalMap = mapResolution + oceanMargin;
            var originalMapOffset = imageWidth - originalMap;

            List<MapMonument> mapMonuments = new List<MapMonument>();
            foreach (MonumentInfo monument in monuments)
            {
                string name = GetMonumentName(monument);
                Vector3 position = monument.transform.position;

                int x = (int)(((position.x + (tempData.mapsize / 2.0)) / tempData.mapsize) * mapResolution) + originalMapOffset;
                int z = (int)(((position.z + (tempData.mapsize / 2.0)) / tempData.mapsize) * mapResolution) + originalMapOffset;

                if (name.ToLower().Contains("train")) { mapMonuments.Add(new MapMonument { name = name, x = x, y = z, indication = Indication.Image }); continue; }

                if (monument.shouldDisplayOnMap && monument.mapIcon == null)
                    mapMonuments.Add(new MapMonument { name = name, x = x, y = z, indication = Indication.Regular });
                else
                    mapMonuments.Add(new MapMonument { name = name, x = x, y = z, indication = Indication.None });
            }

            foreach (var custom in tempData.customMonuments) {
                int x = (int)(((custom.Value.x + (tempData.mapsize / 2.0)) / tempData.mapsize) * mapResolution) + originalMapOffset;
                int z = (int)(((custom.Value.z + (tempData.mapsize / 2.0)) / tempData.mapsize) * mapResolution) + originalMapOffset;
                mapMonuments.Add(new MapMonument { name = custom.Key, x = x, y = z, indication = Indication.Regular });
            }

            return mapMonuments;
        }

        // Format32bppArgb is laid out as B, G, R, A bytes
        private static Bitmap ToBitmap(Color[] pixels, int width, int height) {
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try {
                int stride = data.Stride;
                byte[] bytes = new byte[stride * height];
                Parallel.For(0, height, y => {
                    int src = y * width;
                    int dst = y * stride;
                    for (int x = 0; x < width; x++, dst += 4) {
                        Color c = pixels[src + x];
                        bytes[dst] = ToByte(c.b);
                        bytes[dst + 1] = ToByte(c.g);
                        bytes[dst + 2] = ToByte(c.r);
                        bytes[dst + 3] = ToByte(c.a);
                    }
                });
                Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
            }
            finally {
                bitmap.UnlockBits(data);
            }
            return bitmap;
        }

        private static void FromBitmap(Bitmap bitmap, Color[] pixels) {
            int width = bitmap.Width;
            int height = bitmap.Height;
            var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try {
                int stride = data.Stride;
                byte[] bytes = new byte[stride * height];
                Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
                Parallel.For(0, height, y => {
                    int dst = y * width;
                    int src = y * stride;
                    for (int x = 0; x < width; x++, src += 4)
                        pixels[dst + x] = new Color(bytes[src + 2] / 255f, bytes[src + 1] / 255f, bytes[src] / 255f, bytes[src + 3] / 255f);
                });
            }
            finally {
                bitmap.UnlockBits(data);
            }
        }

        private static byte ToByte(float value) => (byte)Mathf.Clamp(Mathf.FloorToInt(value * 255), 0, 255);

        private static void RenderText(Graphics graphics, string text, Font font, Brush brush, int xx, int zz)
        {
            SizeF textSize = graphics.MeasureString(text, font);
            float textX = xx - (textSize.Width / 2);
            float textY = zz - (textSize.Height / 2);

            graphics.TranslateTransform(textX, textY);
            graphics.RotateTransform(180);
            graphics.ScaleTransform(-1, 1);
            graphics.DrawString(text, font, brush, 0, -textSize.Height);
            graphics.ResetTransform();
        }

        private static void RenderGithub(Graphics graphics, string fontPath, int imageResolution) {
            var text = "github.com/hammzat/HarmonyCustomGenerator - DeepSea Update [by aristocratos]";

            float scaleFactor = 0.04f;
            int fontSize = Mathf.Clamp((int)(imageResolution * scaleFactor), 10, 30);

            using (var fontCollection = new PrivateFontCollection()) {
                fontCollection.AddFontFile(fontPath);
                using (Font font = new Font(fontCollection.Families[0], fontSize))
                using (SolidBrush brush = new SolidBrush(System.Drawing.Color.WhiteSmoke)) {
                    int textHeight = (int)graphics.MeasureString(text, font).Height;

                    int x = imageResolution / 2;
                    int y = imageResolution - textHeight;

                    RenderText(graphics, text, font, brush, x, y);
                }
            }
        }

        private static void RenderMonument(Graphics graphics, List<MapMonument> monuments, string fontPath) {
            Logging.Info("Map image: monument names");

            using (var fontCollection = new PrivateFontCollection()) {
                fontCollection.AddFontFile(fontPath);
                using (Font regular = new Font(fontCollection.Families[0], 20))
                using (Font smaller = new Font(fontCollection.Families[0], 11))
                using (SolidBrush brush = new SolidBrush(System.Drawing.Color.Black)) {
                    foreach (MapMonument monument in monuments) {
                        if (monument.indication == Indication.None) continue;
                        if (monument.indication == Indication.Image) continue; // TODO

                        Font font = monument.indication == Indication.Regular ? regular : smaller;
                        RenderText(graphics, monument.name, font, brush, monument.x, monument.y);
                    }
                }
            }
        }

        private static void RenderGrid(Graphics graphics, int mapResolution, int imageWidth, int oceanMargin) {
            Logging.Info("Map image: grid");
            var gridColor = System.Drawing.Color.FromArgb(120, 0, 0, 0);

            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.SystemDefault;

            float gridSize = 146.3f;
            float cellSize = (float)mapResolution / (tempData.mapsize / gridSize);

            using (Pen gridPen = new Pen(gridColor, 1)) {
                int gridCount = (int)(tempData.mapsize / gridSize);
                for (int i = 0; i <= gridCount; i++) {
                    float x = oceanMargin + (i * cellSize);
                    if (x >= oceanMargin && x <= imageWidth - oceanMargin) {
                        graphics.DrawLine(gridPen, x, oceanMargin, x, imageWidth - oceanMargin);
                    }
                }

                for (int i = 0; i <= gridCount; i++) {
                    float y = oceanMargin + (i * cellSize);
                    if (y >= oceanMargin && y <= imageWidth - oceanMargin) {
                        graphics.DrawLine(gridPen, oceanMargin, y, imageWidth - oceanMargin, y);
                    }
                }

                Font gridFont = new Font("Arial", 12, SDFontStyle.Bold);
                float padding = 5f;

                using (SolidBrush brush = new SolidBrush(gridColor)) {
                    for (int x = 0; x < gridCount; x++) {
                        for (int y = 0; y < gridCount; y++) {
                            float posX = oceanMargin + (x * cellSize);
                            float posY = oceanMargin + (y * cellSize);
                            float nextPosX = posX + cellSize;
                            float nextPosY = posY + cellSize;

                            bool isFull = posX >= oceanMargin && nextPosX <= imageWidth - oceanMargin && posY >= oceanMargin && nextPosY <= imageWidth - oceanMargin;
                            bool isPartRight = posX >= oceanMargin && posX <= imageWidth - oceanMargin && posY >= oceanMargin && posY <= imageWidth - oceanMargin && nextPosX > imageWidth - oceanMargin;

                            if (isFull || isPartRight) {
                                string coords = x <= 25 ? $"{(char)('A' + x)}{gridCount - y}" : $"{(char)('A' + (x / 26 - 1))}{(char)('A' + (x % 26))}{gridCount - y}";
                                float textX = posX + padding;
                                float textY = posY + padding + (cellSize - (padding * 6));

                                graphics.TranslateTransform(textX, textY);
                                graphics.RotateTransform(180);
                                graphics.ScaleTransform(-1, 1);
                                graphics.DrawString(coords, gridFont, brush, 0, -gridFont.Height);
                                graphics.ResetTransform();
                            }
                        }
                    }
                }
                gridFont.Dispose();
            }
        }

        private static byte[] EncodeToFile(int width, int height, Color[] pixels, bool lossy) {
            Stopwatch stopwatch = Stopwatch.StartNew();
            Texture2D texture2D = null;
            try {
                texture2D = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false);
                texture2D.SetPixels(pixels);
                texture2D.Apply();
                return lossy ? ImageConversion.EncodeToJPG(texture2D, 85) : ImageConversion.EncodeToPNG(texture2D);
            }
            finally {
                if (texture2D != null) 
                    UnityEngine.Object.Destroy(texture2D);
                stopwatch.Stop();
                Logging.Info($"Map image: encoded in {stopwatch.Elapsed.TotalSeconds:0.0}s");
            }
        }

        public static string GetMonumentName(MonumentInfo monument) {
            string name = monument?.displayPhrase?.english?.Replace("\n", "");
            if (string.IsNullOrEmpty(name)) {
                if (monument.Type == MonumentType.Cave) name = "Cave";
                else if (monument.name.Contains("power_sub")) name = "Power Sub Station";
                else name = monument.name;
            }

            return name;
        }
    }

    public static class ColorExtensions
    {
        public static System.Drawing.Color ToSystemDrawingColor(this Color unityColor)
        {
            return System.Drawing.Color.FromArgb(
                Mathf.Clamp(Mathf.FloorToInt(unityColor.a * 255), 0, 255),
                Mathf.Clamp(Mathf.FloorToInt(unityColor.r * 255), 0, 255),
                Mathf.Clamp(Mathf.FloorToInt(unityColor.g * 255), 0, 255),
                Mathf.Clamp(Mathf.FloorToInt(unityColor.b * 255), 0, 255)
            );
        }
    }
}