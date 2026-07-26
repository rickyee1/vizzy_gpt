#nullable enable

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace VizzyGPT.Runtime.Ui
{
    public interface ISystemCjkFontBackend
    {
        IReadOnlyList<string> GetInstalledFontNames();

        TMP_FontAsset? CreateDynamicFontAsset(string familyName);

        bool SupportsCharacters(TMP_FontAsset asset, string characters);

        void Release(TMP_FontAsset asset);
    }

    public sealed class SystemCjkFontProvider : IDisposable
    {
        public const string ProbeCharacters = "中文";

        public static readonly IReadOnlyList<string> PreferredFamilies = new[]
        {
            "Microsoft YaHei UI",
            "Microsoft YaHei",
            "DengXian",
            "SimSun",
            "NSimSun",
            "SimHei",
            "KaiTi",
            "FangSong",
            "Noto Sans CJK SC",
            "Noto Sans SC",
            "PingFang SC",
            "Heiti SC",
            "WenQuanYi Micro Hei"
        };

        private readonly ISystemCjkFontBackend backend;
        private readonly Action<string> logWarning;
        private readonly List<TMP_FontAsset> createdAssets = new List<TMP_FontAsset>();
        private TMP_FontAsset? resolvedAsset;
        private bool resolutionAttempted;
        private bool disposed;

        public SystemCjkFontProvider(
            ISystemCjkFontBackend backend,
            Action<string> logWarning)
        {
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
            this.logWarning = logWarning ?? throw new ArgumentNullException(nameof(logWarning));
        }

        public static SystemCjkFontProvider CreateDefault()
        {
            return new SystemCjkFontProvider(
                new UnitySystemCjkFontBackend(),
                message => Debug.LogWarning(message));
        }

        public TMP_FontAsset? Resolve()
        {
            if (disposed || resolutionAttempted)
            {
                return resolvedAsset;
            }

            resolutionAttempted = true;
            try
            {
                var installed = new HashSet<string>(
                    backend.GetInstalledFontNames() ?? Array.Empty<string>(),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var family in PreferredFamilies)
                {
                    if (!installed.Contains(family))
                    {
                        continue;
                    }

                    TMP_FontAsset? candidate;
                    try
                    {
                        candidate = backend.CreateDynamicFontAsset(family);
                    }
                    catch
                    {
                        continue;
                    }

                    if (candidate == null)
                    {
                        continue;
                    }

                    createdAssets.Add(candidate);
                    try
                    {
                        if (backend.SupportsCharacters(candidate, ProbeCharacters))
                        {
                            resolvedAsset = candidate;
                            return resolvedAsset;
                        }
                    }
                    catch
                    {
                        // A broken candidate is handled like an unsupported candidate.
                    }
                }
            }
            catch
            {
                // Font discovery is optional and must not block panel startup.
            }

            WarnNoFont();
            return null;
        }

        private void WarnNoFont()
        {
            try
            {
                logWarning(
                    "VizzyGPT could not find a CJK system font. Chinese text may appear as missing glyphs.");
            }
            catch
            {
                // Logging must not turn optional font discovery into a startup failure.
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            foreach (var asset in createdAssets)
            {
                backend.Release(asset);
            }

            createdAssets.Clear();
            resolvedAsset = null;
        }
    }

    public sealed class UnitySystemCjkFontBackend : ISystemCjkFontBackend
    {
        public IReadOnlyList<string> GetInstalledFontNames()
        {
            return Font.GetOSInstalledFontNames();
        }

        public TMP_FontAsset? CreateDynamicFontAsset(string familyName)
        {
            Font? source = null;
            TMP_FontAsset? asset = null;
            var succeeded = false;
            try
            {
                source = Font.CreateDynamicFontFromOSFont(familyName, 32);
                if (source == null)
                {
                    return null;
                }

                asset = TMP_FontAsset.CreateFontAsset(
                    source,
                    32,
                    4,
                    GlyphRenderMode.SDFAA,
                    1024,
                    1024,
                    AtlasPopulationMode.Dynamic,
                    true);
                if (asset == null)
                {
                    return null;
                }

                asset.name = "VizzyGPT System CJK - " + familyName;
                succeeded = true;
                return asset;
            }
            catch
            {
                return null;
            }
            finally
            {
                if (!succeeded)
                {
                    if (asset != null)
                    {
                        UnityEngine.Object.Destroy(asset);
                    }

                    if (source != null)
                    {
                        UnityEngine.Object.Destroy(source);
                    }
                }
            }
        }

        public bool SupportsCharacters(TMP_FontAsset asset, string characters)
        {
            return asset.TryAddCharacters(characters, out var missingCharacters) &&
                string.IsNullOrEmpty(missingCharacters);
        }

        public void Release(TMP_FontAsset asset)
        {
            var source = asset.sourceFontFile;
            UnityEngine.Object.Destroy(asset);
            if (source != null)
            {
                UnityEngine.Object.Destroy(source);
            }
        }
    }
}
