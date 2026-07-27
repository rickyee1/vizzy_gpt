#nullable enable

using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace VizzyGPT.Tests.EditMode
{
    public sealed class BundledCjkFontProviderTests
    {
        private const string FontAssetGuid = "42a32e345f8e70148801e80491313a76";
        private const string FontAssetPath =
            "Assets/VizzyGPT/Runtime/Resources/Fonts/VizzyGPT/NotoSansCJKsc-Regular.otf";
        private const string LicenseAssetGuid = "8284231862185744b9b188ecf2e9967f";
        private const string LicenseAssetPath =
            "Assets/VizzyGPT/Runtime/Resources/Fonts/VizzyGPT/OFL.txt";

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
    }
}
