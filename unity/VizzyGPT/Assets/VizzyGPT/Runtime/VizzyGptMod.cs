using Jundroo.ModTools;
using UnityEngine;

namespace VizzyGPT.Runtime
{
    public sealed class VizzyGptMod : GameModBase
    {
        private static VizzyGptBehaviour _behaviour;

        protected override void OnModInitialized()
        {
            EnsureInitialized();
        }

        public static void EnsureInitialized()
        {
            if (_behaviour != null)
            {
                return;
            }

            var root = new GameObject("VizzyGPT");
            if (Application.isPlaying)
            {
                Object.DontDestroyOnLoad(root);
            }

            _behaviour = root.AddComponent<VizzyGptBehaviour>();
        }
    }
}
