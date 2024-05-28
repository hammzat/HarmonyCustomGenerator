using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Drawing;
using UnityEngine;

using Color = UnityEngine.Color;
using static CustomGenerator.ExtConfig;
using Facepunch;
using System.Reflection;
using HarmonyLib;
using System.Drawing.Text;
using Font = System.Drawing.Font;
using Graphics = System.Drawing.Graphics;
namespace CustomGenerator.Utility
{
    static class MapImage
    {
        public static void RenderMap(TerrainTexturing _instance, float scale = 0.5f, int oceanMargin = 500) {
            byte[] array = MapImageRender.Render(_instance, out int num, out int num2, out Color color, scale, false, false, 200);
            if (array == null) {
                Debug.Log("MapImageGenerator returned null!");
                return;
            }

            
            if (!Directory.Exists("mapimages")) Directory.CreateDirectory("mapimages");
            string fullPath = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, $"mapimages/MAP{tempData.mapsize}_{tempData.mapseed}.png"));
            File.WriteAllBytes(fullPath, array);
            Debug.Log($"Generated Map image: <root>/mapimages/ \nMap saved to <root>/maps/!\n\n\n\n");
        }

        private static void GetSizes(int width, int height) {
            Debug.Log($"{width}x{height} | map: {tempData.mapsize}");
            Debug.Log($"monuments count: {tempData.terrainMeta.GetComponent<TerrainPath>().Monuments.Count}");
        }
    }

    // Original Facepunch Code && MJSU plugin - Rust Map Api 
    public static class MapImageRender {
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
        private const float SunPower = 0.65f;
        private const float Brightness = 1.05f;
        private const float Contrast = 0.94f;
        private const float OceanWaterLevel = 0f;
        private static readonly Vector4 Half = new Vector4(0.5f, 0.5f, 0.5f, 0.5f);
        private static Array2D<Color> generatedMap;
        private static Array2D<Color> generatedMapIcons;
        private static int width;
        private static int height;
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
            public Bitmap ToBitmap()
            {
                Bitmap bitmap = new Bitmap(_width, _height);
                for (int y = 0; y < _height; y++)
                {
                    for (int x = 0; x < _width; x++)
                    {
                        System.Drawing.Color color = (System.Drawing.Color)(object)this[x, y];
                        bitmap.SetPixel(x, y, color);
                    }
                }
                return bitmap;
            }
            public static Array2D<Color> FromBitmap(Bitmap bitmap)
            {
                int width = bitmap.Width;
                int height = bitmap.Height;

                Color[] colors = new Color[width * height];

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        var px = bitmap.GetPixel(x, y);
                        colors[y * width + x] = new Color(px.R, px.G, px.B);
                    }
                }

                return new Array2D<Color>(colors, width, height);
            }
            public bool IsEmpty()
            {
                return _items == null || _width == 0 && _height == 0;
            }

            public Array2D<T> Clone()
            {
                return new Array2D<T>((T[])_items.Clone(), _width, _height);
            }

        }

        private static FieldInfo _monuments = AccessTools.TypeByName("TerrainPath").GetField("Monuments", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static void LoadIcons(ref Array2D<Color> output, TerrainTexturing _instance, int imageWidth, int imageHeight, int mapResolution, int oceanMargin)
        {

            List<MonumentInfo> monuments = (List<MonumentInfo>)_monuments.GetValue(tempData.terrainPath);
            Debug.Log(monuments.Count);
            Debug.Log("Generating names...");
            var originalMap = mapResolution + oceanMargin;
            var originalMapOffset = imageWidth - originalMap;
            Debug.Log(mapResolution);
            Debug.Log(originalMapOffset);
            for (int i = 0; i < 20; i++)
            {
                for (int j = 0; j < 20; j++)
                {
                    output[i, j] = Color.black;
                    output[imageWidth - i, imageHeight - j] = Color.cyan;

                    output[imageWidth - i, j] = Color.magenta;
                    output[i, imageHeight - j] = Color.grey;


                    output[originalMapOffset + i, originalMapOffset + j] = Color.black;
                    output[originalMap + i, originalMap + j] = Color.cyan;
                }
            }
            foreach (MonumentInfo monument in monuments)
            {
                string name = GetMonumentName(monument);
                Vector3 position = monument.transform.position;
                Debug.Log(name);

                int x = (int)(((position.x + (tempData.mapsize / 2.0)) / tempData.mapsize) * mapResolution) + originalMapOffset;
                int z = (int)(((position.z + (tempData.mapsize / 2.0)) / tempData.mapsize) * mapResolution) + originalMapOffset;

                for (int i = 0; i < 10; i++)
                {
                    for (int j = 0; j < 10; j++)
                    {
                        if ((x + i < imageWidth) && (z + j < imageHeight))
                        {
                            output[x + i, z + j] = Color.magenta;
                        }
                    }
                }

                RenderText(name, "PermanentMarker.ttf", 14, System.Drawing.Color.Black, output.ToBitmap(), x, z, out output);
            }
        }

        public static void RenderText(string text, string fontPath, int fontSize, System.Drawing.Color color, System.Drawing.Bitmap output, int xx, int zz, out Array2D<Color> array)
        {
            PrivateFontCollection fontCollection = new PrivateFontCollection();
            fontCollection.AddFontFile(fontPath);
            Font font = new Font(fontCollection.Families[0], fontSize);
            Graphics graphics = Graphics.FromImage(output);
            SolidBrush brush = new SolidBrush(color);
            graphics.DrawString(text, font, brush, xx, zz);


            int width = output.Width;
            int height = output.Height;

            Color[] colors = new Color[width * height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var px = output.GetPixel(x, y);
                    colors[y * width + x] = new Color(px.R, px.G, px.B);
                }
            }

            array = new Array2D<Color>(colors, width, height);
        }

        public static byte[] Render(TerrainTexturing _instance, out int imageWidth, out int imageHeight, out Color background, float scale = 0.5f, bool lossy = true, bool transparent = false, int oceanMargin = 500)
        {
            if (lossy && transparent) {
                throw new ArgumentException("Rendering a transparent map is not possible when using lossy compression (JPG)");
            }

            imageWidth = 0;
            imageHeight = 0;
            background = OffShoreColor;
            TerrainTexturing instance = _instance;
            if (instance == null) return null;

            Terrain component = instance.GetComponent<Terrain>();
            TerrainMeta component2 = instance.GetComponent<TerrainMeta>();
            TerrainHeightMap terrainHeightMap = instance.GetComponent<TerrainHeightMap>();
            TerrainSplatMap terrainSplatMap = instance.GetComponent<TerrainSplatMap>();
            if ((UnityEngine.Object)(object)component == null || component2 == null || terrainHeightMap == null || terrainSplatMap == null) return null;

            int mapRes = (int)(tempData.mapsize * Mathf.Clamp(scale, 0.1f, 4f)); ;
            float invMapRes = 1f / mapRes;
            if (mapRes <= 0) return null;

            imageWidth = mapRes + oceanMargin * 2;
            imageHeight = mapRes + oceanMargin * 2;
            Color[] array = new Color[imageWidth * imageHeight];
            Array2D<Color> output = new Array2D<Color>(array, imageWidth, imageHeight);
            float maxDepth = (transparent ? Mathf.Max(Mathf.Abs(GetHeight(0f, 0f)), 5f) : 50f);
            Vector4 offShoreColor = (transparent ? Vector4.zero : OffShoreColor);
            Vector4 waterColor = (transparent ? new Vector4(WaterColor.x, WaterColor.y, WaterColor.z, 0.5f) : WaterColor);
            System.Threading.Tasks.Parallel.For(0, imageHeight, delegate (int y) {
                y -= oceanMargin;
                float y2 = y * invMapRes;
                int num = mapRes + oceanMargin;
                for (int i = -oceanMargin; i < num; i++) {
                    float x2 = i * invMapRes;
                    Vector4 startColor = StartColor;
                    float height = GetHeight(x2, y2);
                    float num2 = Math.Max(Vector3.Dot(GetNormal(x2, y2), SunDirection), 0f);
                    startColor = Vector4.Lerp(startColor, GravelColor, GetSplat(x2, y2, 128) * GravelColor.w);
                    startColor = Vector4.Lerp(startColor, PebbleColor, GetSplat(x2, y2, 64) * PebbleColor.w);
                    startColor = Vector4.Lerp(startColor, RockColor, GetSplat(x2, y2, 8) * RockColor.w);
                    startColor = Vector4.Lerp(startColor, DirtColor, GetSplat(x2, y2, 1) * DirtColor.w);
                    startColor = Vector4.Lerp(startColor, GrassColor, GetSplat(x2, y2, 16) * GrassColor.w);
                    startColor = Vector4.Lerp(startColor, ForestColor, GetSplat(x2, y2, 32) * ForestColor.w);
                    startColor = Vector4.Lerp(startColor, SandColor, GetSplat(x2, y2, 4) * SandColor.w);
                    startColor = Vector4.Lerp(startColor, SnowColor, GetSplat(x2, y2, 2) * SnowColor.w);
                    float num3 = 0f - height;
                    if (num3 > 0f) {
                        startColor = Vector4.Lerp(startColor, waterColor, Mathf.Clamp(0.5f + num3 / 5f, 0f, 1f));
                        startColor = Vector4.Lerp(startColor, offShoreColor, Mathf.Clamp(num3 / maxDepth, 0f, 1f));
                    }
                    else {
                        startColor += (num2 - 0.5f) * 0.65f * startColor;
                        startColor = (startColor - Half) * 0.94f + Half;
                    }

                    startColor *= 1.05f;
                    output[i + oceanMargin, y + oceanMargin] = (transparent ? new Color(startColor.x, startColor.y, startColor.z, startColor.w) : new Color(startColor.x, startColor.y, startColor.z));
                }
            });
            background = output[0, 0];

            LoadIcons(ref output, _instance, imageWidth, imageHeight, mapRes, oceanMargin);
            return EncodeToFile(imageWidth, imageHeight, array, lossy);

            Vector3 GetNormal(float x, float y) => terrainHeightMap.GetNormal(x, y);
            float GetHeight(float x, float y) => terrainHeightMap.GetHeight(x, y);
            float GetSplat(float x, float y, int mask) => terrainSplatMap.GetSplat(x, y, mask);
        }

        private static byte[] EncodeToFile(int width, int height, Color[] pixels, bool lossy)
        {
            Texture2D texture2D = null;
            try {
                texture2D = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false);
                texture2D.SetPixels(pixels);
                texture2D.Apply();
                return lossy ? ImageConversion.EncodeToJPG(texture2D, 85) : ImageConversion.EncodeToPNG(texture2D);
            }
            finally {
                if (texture2D != null) {
                    UnityEngine.Object.Destroy(texture2D);
                }
            }
        }

        private static string GetMonumentName(MonumentInfo monument)
        {
            string name = monument.displayPhrase.english.Replace("\n", "");
            if (string.IsNullOrEmpty(name)) {
                if (monument.Type == MonumentType.Cave) {
                    name = "Cave";
                }
                else if (monument.name.Contains("power_sub")) {
                    name = "Power Sub Station";
                }
                else {
                    name = monument.name;
                }
            }

            return name;
        }
    }
}
