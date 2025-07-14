using System;
using System.IO;
using System.Text.Json.Serialization;
using static Hi3Helper.Locale;

namespace ApplyUpdate
{
    [JsonSerializable(typeof(LocalizationParams))]
    [JsonSerializable(typeof(LocalizationParamsBase))]
    internal partial class CoreLibraryFieldsJsonContext : JsonSerializerContext { }

    internal static class Statics
    {
        public static string AppDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "CollapseLauncher");
        public static string AppConfigFile = Path.Combine(AppDataFolder, "config.ini");
        internal static string AppLangFolder { get => UpdateTask.realExecDir; }
    }
}
