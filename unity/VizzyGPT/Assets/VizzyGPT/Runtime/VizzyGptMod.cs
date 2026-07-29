#nullable enable

using System;
using UnityEngine;

namespace VizzyGPT.Runtime
{
    public static class VizzyGptMod
    {
        private static VizzyGptBehaviour? _behaviour;

        public static void EnsureInitialized(Func<string, Font?> loadFont)
        {
            if (loadFont == null) throw new ArgumentNullException(nameof(loadFont));
            if (_behaviour != null)
            {
                return;
            }

            var root = new GameObject("VizzyGPT");
            if (Application.isPlaying)
            {
                UnityEngine.Object.DontDestroyOnLoad(root);
            }

            _behaviour = root.AddComponent<VizzyGptBehaviour>();
            _behaviour.Initialize(loadFont);
        }
    }
}
