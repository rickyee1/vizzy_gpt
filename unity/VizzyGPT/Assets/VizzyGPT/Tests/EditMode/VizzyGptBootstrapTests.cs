using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace VizzyGPT.Tests.EditMode
{
    public sealed class VizzyGptBootstrapTests
    {
        [Test]
        public void Runtime_contains_mod_entry_and_lifecycle_owner()
        {
            var types = AppDomain.CurrentDomain.GetAssemblies().SelectMany(assembly =>
            {
                try
                {
                    return assembly.GetTypes();
                }
                catch
                {
                    return Type.EmptyTypes;
                }
            }).ToArray();

            Assert.That(types.Any(type => type.FullName == "VizzyGPT.Runtime.VizzyGptMod"), Is.True);
            Assert.That(types.Any(type => type.FullName == "VizzyGPT.Runtime.VizzyGptBehaviour"), Is.True);
        }

        [Test]
        public void Generated_mod_entry_creates_one_lifecycle_owner()
        {
            var previousIgnoreFailingMessages = UnityEngine.TestTools.LogAssert.ignoreFailingMessages;
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            try
            {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var rootModType = assemblies.Select(assembly => assembly.GetType("Assets.Scripts.Mod"))
                .First(type => type != null);
            var runtimeModType = assemblies.Select(assembly => assembly.GetType("VizzyGPT.Runtime.VizzyGptMod"))
                .First(type => type != null);
            var behaviourType = assemblies.Select(assembly => assembly.GetType("VizzyGPT.Runtime.VizzyGptBehaviour"))
                .First(type => type != null);
            var instance = rootModType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)
                .GetValue(null);
            var initialize = rootModType.GetMethod(
                "OnModInitialized",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

            Assert.That(initialize, Is.Not.Null, "The builder-generated root Mod must forward initialization.");

            var runtimeEnsureInitialized = runtimeModType.GetMethod(
                "EnsureInitialized",
                BindingFlags.Public | BindingFlags.Static);

            Assert.That(runtimeEnsureInitialized, Is.Not.Null);
            Assert.That(runtimeEnsureInitialized.GetParameters(), Has.Length.EqualTo(1));

            var loadFont = new Func<string, Font>(_ => null);
            runtimeEnsureInitialized.Invoke(null, new object[] { loadFont });
            runtimeEnsureInitialized.Invoke(null, new object[] { loadFont });

            var roots = Resources.FindObjectsOfTypeAll<GameObject>()
                .Where(gameObject => gameObject.name == "VizzyGPT" && gameObject.GetComponent(behaviourType) != null)
                .ToArray();
            try
            {
                Assert.That(roots, Has.Length.EqualTo(1));
            }
            finally
            {
                foreach (var root in roots)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
            }
            finally
            {
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = previousIgnoreFailingMessages;
            }
        }

        [Test]
        public void Mod_bootstrap_injects_its_resource_loader_into_runtime()
        {
            var source = File.ReadAllText(Path.Combine(Application.dataPath, "Scripts/Mod.cs"));

            Assert.That(source, Does.Contain("VizzyGptMod.EnsureInitialized("));
            Assert.That(source, Does.Contain("Mod.Instance.ResourceLoader.LoadAsset<Font>"));
        }

        [Test]
        public void Runtime_bootstrap_passes_the_loader_to_the_behaviour_instance()
        {
            var source = File.ReadAllText(
                Path.Combine(Application.dataPath, "VizzyGPT/Runtime/VizzyGptMod.cs"));

            Assert.That(source, Does.Contain("EnsureInitialized(Func<string, Font?> loadFont)"));
            Assert.That(source, Does.Contain("Initialize(loadFont)"));
            Assert.That(source, Does.Not.Contain("Assets.Scripts"));
        }
    }
}
