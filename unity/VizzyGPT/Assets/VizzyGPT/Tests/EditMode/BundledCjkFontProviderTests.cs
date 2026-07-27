#nullable enable

using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using VizzyGPT.Runtime.Ui;

namespace VizzyGPT.Tests.EditMode
{
    public sealed class BundledCjkFontProviderTests
    {
        private readonly List<FakeBundledCjkFontBackend> backends =
            new List<FakeBundledCjkFontBackend>();

        private const string FontAssetGuid = "42a32e345f8e70148801e80491313a76";
        private const string FontAssetPath =
            "Assets/VizzyGPT/Runtime/Resources/Fonts/VizzyGPT/NotoSansCJKsc-Regular.otf";
        private const string LicenseAssetGuid = "8284231862185744b9b188ecf2e9967f";
        private const string LicenseAssetPath =
            "Assets/VizzyGPT/Runtime/Resources/Fonts/VizzyGPT/OFL.txt";

        [TearDown]
        public void TearDown()
        {
            foreach (var backend in backends)
            {
                backend.DestroyAssets();
            }

            backends.Clear();
        }

        [Test]
        public void Resolve_loads_packaged_font_once_and_caches_the_TMP_asset()
        {
            var source = new Font();
            var expected = ScriptableObject.CreateInstance<TMP_FontAsset>();
            var backend = CreateBackend(source, expected);
            var provider = new BundledCjkFontProvider(backend, _ => { });

            Assert.That(provider.Resolve(), Is.SameAs(expected));
            Assert.That(provider.Resolve(), Is.SameAs(expected));
            Assert.That(backend.LoadedPaths, Is.EqualTo(new[] { FontAssetPath }));
            Assert.That(backend.CreateCount, Is.EqualTo(1));
        }

        [Test]
        public void Resolve_without_the_packaged_font_warns_only_once()
        {
            var warnings = new List<string>();
            var backend = CreateBackend(null, null);
            var provider = new BundledCjkFontProvider(backend, warnings.Add);

            Assert.That(provider.Resolve(), Is.Null);
            Assert.That(provider.Resolve(), Is.Null);
            Assert.That(warnings, Has.Count.EqualTo(1));
            StringAssert.Contains("bundled CJK font", warnings[0]);
        }

        [Test]
        public void Dispose_releases_only_the_created_TMP_asset()
        {
            var source = new Font();
            var asset = ScriptableObject.CreateInstance<TMP_FontAsset>();
            var backend = CreateBackend(source, asset);
            var provider = new BundledCjkFontProvider(backend, _ => { });
            provider.Resolve();

            provider.Dispose();
            provider.Dispose();

            Assert.That(backend.ReleasedAssets, Is.EqualTo(new[] { asset }));
        }

        [Test]
        public void Packaged_font_asset_can_load_chinese_glyphs()
        {
            var source = AssetDatabase.LoadAssetAtPath<Font>(FontAssetPath);

            Assert.That(source, Is.Not.Null);
            Assert.That(FontEngine.LoadFontFace(source, 32), Is.EqualTo(FontEngineError.Success));
            Assert.That(FontEngine.TryGetGlyphIndex(0x4E2D, out var chineseGlyph), Is.True);
            Assert.That(chineseGlyph, Is.Not.EqualTo(0));
            Assert.That(FontEngine.TryGetGlyphIndex(0x6587, out var textGlyph), Is.True);
            Assert.That(textGlyph, Is.Not.EqualTo(0));
        }

        [Test]
        public void Packaged_assets_use_the_full_mod_resource_loader_paths()
        {
            Assert.That(AssetDatabase.GUIDToAssetPath(FontAssetGuid), Is.EqualTo(FontAssetPath));
            Assert.That(AssetDatabase.GUIDToAssetPath(LicenseAssetGuid), Is.EqualTo(LicenseAssetPath));
        }

        [Test]
        public void Mod_data_includes_the_font_and_license_in_other_assets()
        {
            var modDataPath = Path.Combine(Application.dataPath, "ModData.asset");
            var modData = File.ReadAllText(modDataPath);
            var section = Regex.Match(
                modData,
                @"(?ms)^  _otherAssets:(?<body>.*?)(?=^  _[A-Za-z])");

            Assert.That(section.Success, Is.True, "_otherAssets section is missing.");
            Assert.That(
                section.Groups["body"].Value,
                Does.Contain(
                    "{fileID: 12800000, guid: " + FontAssetGuid + ", type: 3}"));
            Assert.That(
                section.Groups["body"].Value,
                Does.Contain(
                    "{fileID: 4900000, guid: " + LicenseAssetGuid + ", type: 3}"));
        }

        [Test]
        public void Packaged_font_includes_the_OFL_license()
        {
            var license = AssetDatabase.LoadAssetAtPath<TextAsset>(LicenseAssetPath);

            Assert.That(license, Is.Not.Null);
            Assert.That(license.text, Does.Contain("SIL OPEN FONT LICENSE Version 1.1"));
        }

        private FakeBundledCjkFontBackend CreateBackend(Font? source, TMP_FontAsset? asset)
        {
            var backend = new FakeBundledCjkFontBackend(source, asset);
            backends.Add(backend);
            return backend;
        }

        private sealed class FakeBundledCjkFontBackend : IBundledCjkFontBackend
        {
            private readonly Font? source;
            private readonly TMP_FontAsset? asset;

            public FakeBundledCjkFontBackend(Font? source, TMP_FontAsset? asset)
            {
                this.source = source;
                this.asset = asset;
            }

            public List<string> LoadedPaths { get; } = new List<string>();

            public int CreateCount { get; private set; }

            public List<TMP_FontAsset> ReleasedAssets { get; } = new List<TMP_FontAsset>();

            public Font? LoadSourceFont(string resourcePath)
            {
                LoadedPaths.Add(resourcePath);
                return source;
            }

            public TMP_FontAsset? CreateDynamicFontAsset(Font sourceFont)
            {
                CreateCount++;
                return asset;
            }

            public void Release(TMP_FontAsset releasedAsset)
            {
                ReleasedAssets.Add(releasedAsset);
            }

            public void DestroyAssets()
            {
                if (asset != null)
                {
                    Object.DestroyImmediate(asset);
                }

                if (source != null)
                {
                    Object.DestroyImmediate(source);
                }
            }
        }
    }
}
