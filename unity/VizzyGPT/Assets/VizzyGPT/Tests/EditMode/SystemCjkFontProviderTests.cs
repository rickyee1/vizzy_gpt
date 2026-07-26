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
        private readonly List<FakeSystemCjkFontBackend> backends = new List<FakeSystemCjkFontBackend>();

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
        public void Resolve_skips_missing_and_non_cjk_fonts_then_caches_first_supported_candidate()
        {
            var backend = CreateBackend(
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
            var backend = CreateBackend(
                new[] { "sImSuN" },
                supportedFamilies: new[] { "SimSun" });
            var provider = new SystemCjkFontProvider(backend, _ => { });

            provider.Resolve();

            Assert.That(backend.CreatedFamilies, Is.EqualTo(new[] { "SimSun" }));
        }

        [Test]
        public void Resolve_without_supported_font_returns_null_and_warns_only_once()
        {
            var backend = CreateBackend(new[] { "Arial" }, Array.Empty<string>());
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
            var backend = CreateBackend(
                new[] { "DengXian", "SimSun" },
                supportedFamilies: new[] { "SimSun" });
            var provider = new SystemCjkFontProvider(backend, _ => { });

            provider.Resolve();
            provider.Dispose();
            provider.Dispose();

            Assert.That(backend.ReleasedAssets, Is.EquivalentTo(backend.Assets.Values));
            Assert.That(backend.ReleasedAssets, Has.Count.EqualTo(2));
        }

        [Test]
        public void Resolve_continues_after_candidate_creation_returns_null()
        {
            var backend = CreateBackend(
                new[] { "DengXian", "SimSun" },
                supportedFamilies: new[] { "SimSun" },
                nullCreationFamilies: new[] { "DengXian" });
            var provider = new SystemCjkFontProvider(backend, _ => { });

            var resolved = provider.Resolve();

            Assert.That(resolved, Is.SameAs(backend.Assets["SimSun"]));
            Assert.That(backend.CreatedFamilies, Is.EqualTo(new[] { "DengXian", "SimSun" }));
        }

        [Test]
        public void Resolve_continues_after_candidate_creation_throws_and_caches_success()
        {
            var backend = CreateBackend(
                new[] { "DengXian", "SimSun" },
                supportedFamilies: new[] { "SimSun" },
                throwingCreationFamilies: new[] { "DengXian" });
            var warnings = new List<string>();
            var provider = new SystemCjkFontProvider(backend, warnings.Add);

            var first = provider.Resolve();
            var second = provider.Resolve();

            Assert.That(first, Is.SameAs(backend.Assets["SimSun"]));
            Assert.That(second, Is.SameAs(first));
            Assert.That(backend.CreatedFamilies, Is.EqualTo(new[] { "DengXian", "SimSun" }));
            Assert.That(warnings, Is.Empty);
        }

        private FakeSystemCjkFontBackend CreateBackend(
            IReadOnlyList<string> installedFontNames,
            IReadOnlyCollection<string> supportedFamilies,
            IReadOnlyCollection<string>? nullCreationFamilies = null,
            IReadOnlyCollection<string>? throwingCreationFamilies = null)
        {
            var backend = new FakeSystemCjkFontBackend(
                installedFontNames,
                supportedFamilies,
                nullCreationFamilies,
                throwingCreationFamilies);
            backends.Add(backend);
            return backend;
        }

        private sealed class FakeSystemCjkFontBackend : ISystemCjkFontBackend
        {
            private readonly IReadOnlyList<string> installedFontNames;
            private readonly HashSet<string> supportedFamilies;
            private readonly HashSet<string> nullCreationFamilies;
            private readonly HashSet<string> throwingCreationFamilies;
            private readonly Dictionary<TMP_FontAsset, string> assetFamilies = new Dictionary<TMP_FontAsset, string>();

            public FakeSystemCjkFontBackend(
                IReadOnlyList<string> installedFontNames,
                IReadOnlyCollection<string> supportedFamilies,
                IReadOnlyCollection<string>? nullCreationFamilies,
                IReadOnlyCollection<string>? throwingCreationFamilies)
            {
                this.installedFontNames = installedFontNames;
                this.supportedFamilies = new HashSet<string>(supportedFamilies, StringComparer.OrdinalIgnoreCase);
                this.nullCreationFamilies = new HashSet<string>(
                    nullCreationFamilies ?? Array.Empty<string>(),
                    StringComparer.OrdinalIgnoreCase);
                this.throwingCreationFamilies = new HashSet<string>(
                    throwingCreationFamilies ?? Array.Empty<string>(),
                    StringComparer.OrdinalIgnoreCase);
            }

            public Dictionary<string, TMP_FontAsset> Assets { get; } = new Dictionary<string, TMP_FontAsset>(StringComparer.OrdinalIgnoreCase);

            public List<string> CreatedFamilies { get; } = new List<string>();

            public List<TMP_FontAsset> ReleasedAssets { get; } = new List<TMP_FontAsset>();

            private List<TMP_FontAsset> CreatedAssets { get; } = new List<TMP_FontAsset>();

            public IReadOnlyList<string> GetInstalledFontNames()
            {
                return installedFontNames;
            }

            public TMP_FontAsset? CreateDynamicFontAsset(string familyName)
            {
                CreatedFamilies.Add(familyName);
                if (throwingCreationFamilies.Contains(familyName))
                {
                    throw new InvalidOperationException("candidate creation failed");
                }

                if (nullCreationFamilies.Contains(familyName))
                {
                    return null;
                }

                var asset = ScriptableObject.CreateInstance<TMP_FontAsset>();
                Assets[familyName] = asset;
                assetFamilies[asset] = familyName;
                CreatedAssets.Add(asset);
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

            public void DestroyAssets()
            {
                foreach (var asset in CreatedAssets)
                {
                    if (asset != null)
                    {
                        UnityEngine.Object.DestroyImmediate(asset);
                    }
                }

                CreatedAssets.Clear();
                assetFamilies.Clear();
                Assets.Clear();
            }
        }
    }
}
