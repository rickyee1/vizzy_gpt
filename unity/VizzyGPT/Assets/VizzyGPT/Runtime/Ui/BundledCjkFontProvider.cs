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

        bool TryAddCharacters(TMP_FontAsset asset, string characters, out string missing);

        void Release(TMP_FontAsset asset);
    }

    public sealed class BundledCjkFontProvider : IDisposable
    {
        public const string FontAssetPath =
            "Assets/VizzyGPT/Runtime/Resources/Fonts/VizzyGPT/NotoSansCJKsc-Regular.otf";
        public const string GlyphProbe = "你好，请解释当前程序。中文回复预览";

        private readonly IBundledCjkFontBackend backend;
        private readonly Action<string> logWarning;
        private readonly Action<string> logInfo;
        private TMP_FontAsset? resolvedAsset;
        private bool attempted;
        private bool disposed;

        public BundledCjkFontProvider(
            IBundledCjkFontBackend backend,
            Action<string> logWarning,
            Action<string>? logInfo = null)
        {
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
            this.logWarning = logWarning ?? throw new ArgumentNullException(nameof(logWarning));
            this.logInfo = logInfo ?? (message => Debug.Log(message));
        }

        public static BundledCjkFontProvider CreateDefault(Func<string, Font?> loadFont)
        {
            return new BundledCjkFontProvider(
                new UnityBundledCjkFontBackend(loadFont),
                message => Debug.LogWarning(message),
                message => Debug.Log(message));
        }

        public TMP_FontAsset? Resolve()
        {
            if (disposed || attempted)
            {
                return resolvedAsset;
            }

            attempted = true;
            TMP_FontAsset? createdAsset = null;
            try
            {
                var source = backend.LoadSourceFont(FontAssetPath);
                if (source != null)
                {
                    createdAsset = backend.CreateDynamicFontAsset(source);
                    if (createdAsset != null &&
                        backend.TryAddCharacters(createdAsset, GlyphProbe, out var missing) &&
                        string.IsNullOrEmpty(missing))
                    {
                        resolvedAsset = createdAsset;
                        TryLogInfo("VizzyGPT bundled CJK font glyph validation succeeded.");
                        return resolvedAsset;
                    }
                }
            }
            catch
            {
            }

            if (createdAsset != null)
            {
                try
                {
                    backend.Release(createdAsset);
                }
                catch
                {
                }
            }

            TryLogWarning(
                "VizzyGPT bundled CJK font glyph validation failed; Chinese text may appear as missing glyphs.");

            return null;
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

        private void TryLogInfo(string message)
        {
            try
            {
                logInfo(message);
            }
            catch
            {
            }
        }

        private void TryLogWarning(string message)
        {
            try
            {
                logWarning(message);
            }
            catch
            {
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

        public bool TryAddCharacters(TMP_FontAsset asset, string characters, out string missing)
        {
            return asset.TryAddCharacters(characters, out missing);
        }

        public void Release(TMP_FontAsset asset)
        {
            UnityEngine.Object.Destroy(asset);
        }
    }
}
