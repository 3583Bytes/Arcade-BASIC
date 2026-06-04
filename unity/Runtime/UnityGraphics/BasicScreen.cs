using System.IO;
using ArcadeBasic;            // BasicEngine (from the shipped plugin DLLs)
using ArcadeBasic.Runtime;    // RasterGraphicsDevice
using UnityEngine;

namespace ArcadeBasic.Unity
{
    /// <summary>
    /// Renders an Arcade BASIC §13 graphics program onto a <see cref="Texture2D"/>
    /// — the Unity "screen". The actual rasterization lives in the engine-agnostic
    /// <see cref="RasterGraphicsDevice"/> (unit-tested, no UnityEngine dependency);
    /// this component just runs a program and copies the pixel buffer into a
    /// texture, then assigns it to a Renderer's material (e.g. a Quad).
    ///
    /// v1: STATIC programs — the program runs to completion on Start and the final
    /// frame is shown (e.g. examples/graphics.bas). Real-time programs that loop on
    /// INKEY$/SLEEP (kanban, invaders) need the threaded driver — a planned
    /// follow-up — so they don't block Unity's main thread.
    /// </summary>
    [AddComponentMenu("Arcade BASIC/Basic Screen")]
    public sealed class BasicScreen : MonoBehaviour
    {
        [Tooltip("The BASIC program to run (§13 graphics).")]
        [TextArea(6, 24)]
        public string source =
            "SET WINDOW 0, 100, 0, 100\n" +
            "SET LINE COLOR 6\n" +
            "GRAPH LINES: 10, 10; 90, 10; 50, 90; 10, 10\n" +
            "SET AREA COLOR 4\n" +
            "GRAPH AREA: 40, 30; 60, 30; 50, 55\n" +
            "SET TEXT COLOR 7\n" +
            "GRAPH TEXT, AT 12, 95: \"ARCADE BASIC\"\n";

        [Tooltip("Screen resolution in pixels. Higher = crisper vector lines but more CPU per frame.")]
        public Vector2Int resolution = new Vector2Int(256, 192);

        [Tooltip("Point = crisp pixels; Bilinear = smoothed when scaled up.")]
        public FilterMode filterMode = FilterMode.Point;

        [Tooltip("Where to show the screen. The texture is set as material.mainTexture. " +
                 "A Quad works well; for a UI RawImage, read the Screen property and assign it yourself.")]
        public Renderer targetRenderer;

        private Texture2D _texture;

        /// <summary>The rendered screen texture (also assigned to targetRenderer).</summary>
        public Texture2D Screen => _texture;

        private void Start() => Run();

        /// <summary>Run the program once and present its final frame.</summary>
        public void Run()
        {
            int w = Mathf.Max(1, resolution.x);
            int h = Mathf.Max(1, resolution.y);

            var device = new RasterGraphicsDevice(w, h);
            BasicEngine.Run(source, new StringWriter(), null, "<unity>", default, device);
            Present(device);
        }

        private void Present(RasterGraphicsDevice device)
        {
            int w = device.Width, h = device.Height;
            if (_texture == null || _texture.width != w || _texture.height != h)
            {
                _texture = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: false)
                {
                    filterMode = filterMode,
                    wrapMode = TextureWrapMode.Clamp,
                };
            }

            var src = device.Pixels;              // ARGB (0xAARRGGBB), row 0 = top
            var dst = new Color32[src.Length];
            for (int y = 0; y < h; y++)
            {
                // Unity textures are bottom-up, so flip the row order.
                int srcRow = y * w;
                int dstRow = (h - 1 - y) * w;
                for (int x = 0; x < w; x++)
                {
                    int argb = src[srcRow + x];
                    dst[dstRow + x] = new Color32(
                        (byte)((argb >> 16) & 0xFF),   // R
                        (byte)((argb >> 8) & 0xFF),    // G
                        (byte)(argb & 0xFF),           // B
                        (byte)((argb >> 24) & 0xFF));  // A
                }
            }

            _texture.SetPixels32(dst);
            _texture.Apply(updateMipmaps: false);

            if (targetRenderer != null)
                targetRenderer.material.mainTexture = _texture;
        }
    }
}
