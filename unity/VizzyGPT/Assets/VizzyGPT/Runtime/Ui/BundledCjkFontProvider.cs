#nullable enable

using System;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace VizzyGPT.Runtime.Ui
{
    public interface IBundledCjkFontBackend
    {
        Font? LoadSourceFont(string resourcePath);

        TMP_FontAsset? CreateDynamicFontAsset(Font source);

        void Release(TMP_FontAsset asset);
    }

    public sealed class BundledCjkFontProvider : IDisposable
    {
        public const string FontAssetPath =
            "Assets/VizzyGPT/Runtime/Resources/Fonts/VizzyGPT/NotoSansCJKsc-Regular.otf";

        private readonly IBundledCjkFontBackend backend;
        private readonly Action<string> logWarning;
        private TMP_FontAsset? resolvedAsset;
        private bool attempted;
        private bool disposed;

        public BundledCjkFontProvider(
            IBundledCjkFontBackend backend,
            Action<string> logWarning)
        {
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
            this.logWarning = logWarning ?? throw new ArgumentNullException(nameof(logWarning));
        }

        public static BundledCjkFontProvider CreateDefault(Func<string, Font?> loadFont)
        {
            return new BundledCjkFontProvider(
                new UnityBundledCjkFontBackend(loadFont),
                message => Debug.LogWarning(message));
        }

        public TMP_FontAsset? Resolve()
        {
            if (disposed || attempted)
            {
                return resolvedAsset;
            }

            attempted = true;
            try
            {
                var source = backend.LoadSourceFont(FontAssetPath);
                if (source != null)
                {
                    resolvedAsset = backend.CreateDynamicFontAsset(source);
                }
            }
            catch
            {
                resolvedAsset = null;
            }

            if (resolvedAsset == null)
            {
                try
                {
                    logWarning(
                        "VizzyGPT could not load its bundled CJK font. Chinese text may appear as missing glyphs.");
                }
                catch
                {
                }
            }

            return resolvedAsset;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (resolvedAsset != null)
            {
                backend.Release(resolvedAsset);
                resolvedAsset = null;
            }
        }
    }

    public sealed class UnityBundledCjkFontBackend : IBundledCjkFontBackend
    {
        private readonly Func<string, Font?> loadFont;

        public UnityBundledCjkFontBackend(Func<string, Font?> loadFont)
        {
            this.loadFont = loadFont ?? throw new ArgumentNullException(nameof(loadFont));
        }

        public Font? LoadSourceFont(string assetPath)
        {
            return loadFont(assetPath);
        }

        public TMP_FontAsset? CreateDynamicFontAsset(Font source)
        {
            var asset = TMP_FontAsset.CreateFontAsset(
                source,
                32,
                4,
                GlyphRenderMode.SDFAA,
                1024,
                1024,
                AtlasPopulationMode.Dynamic,
                true);
            if (asset != null)
            {
                asset.name = "VizzyGPT Bundled CJK";
                asset.isMultiAtlasTexturesEnabled = true;
            }

            return asset;
        }

        public void Release(TMP_FontAsset asset)
        {
            UnityEngine.Object.Destroy(asset);
        }
    }
}
