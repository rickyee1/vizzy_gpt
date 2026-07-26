#nullable enable

using System;
using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using VizzyGPT.Runtime.Ui;

namespace VizzyGPT.Tests.EditMode
{
    public sealed class SystemCjkFontProviderTests
    {
        [Test]
        public void Resolve_skips_missing_and_non_cjk_fonts_then_caches_first_supported_candidate()
        {
            var backend = new FakeSystemCjkFontBackend(
                new[] { "DengXian", "SimSun" },
                supportedFamilies: new[] { "SimSun" });
            var warnings = new List<string>();
            var provider = new SystemCjkFontProvider(backend, warnings.Add);

            var first = provider.Resolve();
            var second = provider.Resolve();

            Assert.That(first, Is.SameAs(backend.Assets["SimSun"]));
            Assert.That(second, Is.SameAs(first));
            Assert.That(backend.CreatedFamilies, Is.EqualTo(new[] { "DengXian", "SimSun" }));
            Assert.That(warnings, Is.Empty);
        }

        [Test]
        public void Resolve_matches_installed_family_names_case_insensitively()
        {
            var backend = new FakeSystemCjkFontBackend(
                new[] { "sImSuN" },
                supportedFamilies: new[] { "SimSun" });
            var provider = new SystemCjkFontProvider(backend, _ => { });

            provider.Resolve();

            Assert.That(backend.CreatedFamilies, Is.EqualTo(new[] { "SimSun" }));
        }

        [Test]
        public void Resolve_without_supported_font_returns_null_and_warns_only_once()
        {
            var backend = new FakeSystemCjkFontBackend(new[] { "Arial" }, Array.Empty<string>());
            var warnings = new List<string>();
            var provider = new SystemCjkFontProvider(backend, warnings.Add);

            Assert.That(provider.Resolve(), Is.Null);
            Assert.That(provider.Resolve(), Is.Null);
            Assert.That(warnings, Has.Count.EqualTo(1));
            StringAssert.Contains("CJK system font", warnings[0]);
        }

        [Test]
        public void Dispose_releases_every_created_candidate_once()
        {
            var backend = new FakeSystemCjkFontBackend(
                new[] { "DengXian", "SimSun" },
                supportedFamilies: new[] { "SimSun" });
            var provider = new SystemCjkFontProvider(backend, _ => { });

            provider.Resolve();
            provider.Dispose();
            provider.Dispose();

            Assert.That(backend.ReleasedAssets, Is.EquivalentTo(backend.Assets.Values));
            Assert.That(backend.ReleasedAssets, Has.Count.EqualTo(2));
        }

        private sealed class FakeSystemCjkFontBackend : ISystemCjkFontBackend
        {
            private readonly IReadOnlyList<string> installedFontNames;
            private readonly HashSet<string> supportedFamilies;
            private readonly Dictionary<TMP_FontAsset, string> assetFamilies = new Dictionary<TMP_FontAsset, string>();

            public FakeSystemCjkFontBackend(
                IReadOnlyList<string> installedFontNames,
                IReadOnlyCollection<string> supportedFamilies)
            {
                this.installedFontNames = installedFontNames;
                this.supportedFamilies = new HashSet<string>(supportedFamilies, StringComparer.OrdinalIgnoreCase);
            }

            public Dictionary<string, TMP_FontAsset> Assets { get; } = new Dictionary<string, TMP_FontAsset>(StringComparer.OrdinalIgnoreCase);

            public List<string> CreatedFamilies { get; } = new List<string>();

            public List<TMP_FontAsset> ReleasedAssets { get; } = new List<TMP_FontAsset>();

            public IReadOnlyList<string> GetInstalledFontNames()
            {
                return installedFontNames;
            }

            public TMP_FontAsset? CreateDynamicFontAsset(string familyName)
            {
                var asset = ScriptableObject.CreateInstance<TMP_FontAsset>();
                Assets[familyName] = asset;
                assetFamilies[asset] = familyName;
                CreatedFamilies.Add(familyName);
                return asset;
            }

            public bool SupportsCharacters(TMP_FontAsset asset, string characters)
            {
                return supportedFamilies.Contains(assetFamilies[asset]);
            }

            public void Release(TMP_FontAsset asset)
            {
                ReleasedAssets.Add(asset);
            }
        }
    }
}
