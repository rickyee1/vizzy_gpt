using System;
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
            var runtimeInitialize = runtimeModType.GetMethod(
                "OnModInitialized",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

            Assert.That(initialize, Is.Not.Null, "The builder-generated root Mod must forward initialization.");
            Assert.That(runtimeInitialize, Is.Not.Null);

            initialize.Invoke(instance, null);
            runtimeInitialize.Invoke(Activator.CreateInstance(runtimeModType), null);
            initialize.Invoke(instance, null);

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
    }
}
