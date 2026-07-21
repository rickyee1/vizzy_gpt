using System.IO;
using UnityEngine;

namespace VizzyGPT.Runtime.Storage
{
    public static class JunoDataPaths
    {
        public static string Root => Path.Combine(Application.persistentDataPath, "UserData", "VizzyGPT");
    }
}
