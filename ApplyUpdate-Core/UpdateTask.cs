using Hi3Helper.Http;
using System;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

namespace ApplyUpdate
{
    public class UpdateTask
    {
        internal enum PathAction { create, delete }
        internal enum PathType { file, directory }
        enum StringAlign { left, center, right }

        private static bool _isConsoleWindowHasWidth = IsConsoleWindowHasWidth();
        private static int _windowBufferWidth { get => _isConsoleWindowHasWidth ? Console.WindowWidth : 0; }

        public static int PreferredCDNIndex = 0;
        public static string realExecPath = Process.GetCurrentProcess().MainModule!.FileName;
        public static string realExecDir { get => Path.GetDirectoryName(realExecPath)!; }
        public static string workingDir { get => realExecDir; }
        public static string execPath = Path.Combine(workingDir, applyExec + ".exe");
        public static string tempDir = Path.Combine(workingDir, "_Temp");
        public static string sourcePath = Path.Combine(workingDir, Path.GetFileName(execPath));
        public static string applyExec = Path.GetFileNameWithoutExtension(realExecPath);
        public static string launcherPath = Path.Combine(workingDir, "CollapseLauncher.exe");

        public static string zipPath = Path.Combine(tempDir, "latest");
        public static string zipExtractPath = Path.Combine(tempDir, "_Extract");

        public static readonly string[] excludeDeleteFile = new string[]
        {
            // Generic Files
#if DEBUG
            "ApplyUpdate.",
#else
            "ApplyUpdate.exe",
#endif
            "config.ini",
            "_Temp",
            "unins00",
            "unins000",

            // Game Files (we added this in-case the user uses the same directory as the executable)
            "Hi3SEA", "Hi3Global", "Hi3TW", "Hi3KR", "Hi3CN", "Hi3JP", "BH3",
            "GIGlb", "GICN", "GenshinImpact", "YuanShen", "GIBilibili",
            "SRGlb", "SRCN", "StarRail", "HSRBilibili",
            "ZZZGlb", "ZZZCN", "ZZZ"
#if DEBUG
            // Hi3Helper.Http DLLs
            , "Hi3Helper.Http"
#endif
        };

        internal static event EventHandler<UpdateProgress> EventProgress;
        internal static event EventHandler<UpdateStatus> EventStatus;
        internal static UpdateProgress _propProgress = new UpdateProgress();
        internal static UpdateStatus _propStatus = new UpdateStatus();

        internal static bool IsCollapseRunning() => Process.GetProcessesByName("CollapseLauncher").Length != 0;

        internal static string TryGetStampFilePath()
        {
            string stampFilePath = Path.Combine(tempDir, "release");
            string stampFilePath2 = Path.Combine(workingDir, "release");

            string stampFile = File.Exists(stampFilePath) ? stampFilePath : stampFilePath2;
            return stampFile;
        }

        internal static string DetermineReleaseChannel()
        {
            string stampFile = TryGetStampFilePath();
            if (!File.Exists(stampFile))
            {
                Console.WriteLine($"\"release\" file doesn't exist in \"_Temp\\release\" or current directory");

                return null;
            }

            string[] lines = File.ReadAllLines(stampFile);
            if (lines == null || lines.Length == 0)
            {
                Console.WriteLine($"Content of the \"release\" stamp file is empty. The content of the file must be \"preview\" or \"stable\"");
                return null;
            }

            if (!(lines[0].ToLower() == "stable" || lines[0].ToLower() == "preview"))
            {
                Console.WriteLine($"Content of the \"release\" stamp file is not valid. The content of the file must be \"preview\" or \"stable\"");

                return null;
            }

            return lines[0];
        }

        internal static async Task<bool> CleanupOldFiles()
        {
            // This is a switch to determine that this file only deletes everything if
            // the current or old installation exist
            string switchPath1 = Path.Combine(workingDir, "CollapseLauncher.exe");
            string switchPath2 = Path.Combine(workingDir, "CollapseLauncher.dll");
            string switchPath3 = Path.Combine(workingDir, "Hi3Helper.Core.dll");
            string switchPath4 = Path.Combine(workingDir, "Hi3Helper.Http.dll");
            string switchPath5 = Path.Combine(workingDir, "Hi3Helper.EncTool.dll");
            if (!(File.Exists(switchPath1) && File.Exists(switchPath2) && File.Exists(switchPath3)
               && File.Exists(switchPath4) && File.Exists(switchPath5)))
            {
                return true;
            }

            FileVersionInfo execInfo = FileVersionInfo.GetVersionInfo(switchPath2);
            string version = execInfo.FileVersion!;

            string title = $"A previous legacy installation of Collapse v{version} detected!";
            string msg = $"We detected that you have a legacy Collapse v{version} installed on your PC. "
                + "The updater needs to clean-up all the old files inside its directory.\r\n"
                + "Please make sure you don't have any important files inside of the Collapse directory or it will be COMPLETELY WIPED OUT!"
                + "\r\n\r\nClick \"Yes\" to proceed or \"No\" to cancel.";

            uint msgStyle = 0x00000004u | 0x00000030u | 0x00001000u;
            int result = PInvoke.MessageBoxExA(IntPtr.Zero, msg, title, msgStyle);
            // Result:
            //    7 = No
            //    6 = Yes
            if (result == 7) return false;

            result = PInvoke.MessageBoxExA(IntPtr.Zero, "Click \"Yes\" once again to confirm.", title, msgStyle);
            if (result == 7) return false;

            int count = 5;
            while (count > 0)
            {
                _propStatus.Status = $"Clean-up process will be started in {count}...";
                InvokeStatus();
                Console.Write('\r' + _propStatus.Status);
                await Task.Delay(1000);
                count--;
            }
            Console.WriteLine();

            int offset = workingDir.Length + 1;
            int i = 1;
            foreach (string path in Directory.EnumerateDirectories(workingDir, "*", SearchOption.TopDirectoryOnly))
            {
                if (!IsExceptedFiles(path))
                {
                    try
                    {
                        _propStatus.ActivityStatus = $"Deleting: {path.Substring(offset)}...";
                        InvokeStatus();
                        Console.Write('\r' + GetBothAlignedString($"Deleting folder: {path.Substring(offset)}...", $"[{i++} / ?]"));
                        TryDoPathAction(path, PathAction.delete, PathType.directory);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine();
                        Console.WriteLine($"Error!\r\n{ex}");
                    }
                }
            }

            foreach (string path in Directory.EnumerateFiles(workingDir, "*.*", SearchOption.AllDirectories))
            {
                if (!IsExceptedFiles(path))
                {
                    try
                    {
                        _propStatus.ActivityStatus = $"Deleting: {path.Substring(offset)}...";
                        InvokeStatus();
                        Console.Write('\r' + GetBothAlignedString($"Deleting file: {path.Substring(offset)}...", $"[{i++} / ?]"));
                        TryDoPathAction(path, PathAction.delete, PathType.file);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine();
                        Console.WriteLine($"Error!\r\n{ex}");
                    }
                }
            }

            string applyUpdateConfig = Path.Combine(workingDir, "ApplyUpdate.exe.config");
            if (File.Exists(applyUpdateConfig))
            {
                try
                {
                    _propStatus.ActivityStatus = $"Deleting: {applyUpdateConfig.Substring(offset)}...";
                    InvokeStatus();
                    Console.Write('\r' + GetBothAlignedString($"Deleting file: {applyUpdateConfig.Substring(offset)}...", $"[{i++} / ?]"));
                    TryDoPathAction(applyUpdateConfig, PathAction.delete, PathType.file);
                }
                catch (Exception ex)
                {
                    Console.WriteLine();
                    Console.WriteLine($"Error!\r\n{ex}");
                }
            }
            Console.WriteLine();

            return true;
        }

        internal static async Task DownloadPackage(string stamp)
        {
            if (!File.Exists(zipPath))
            {
                string packageURL = FallbackCDNUtil.CombineURLFromString("squirrel", stamp, "latest");
                using (Http httpClient = new Http(true))
                {
                    _propStatus.ActivityStatus = "Downloading package";
                    InvokeStatus();
                    FallbackCDNUtil.DownloadProgress += (_, progress) =>
                    {
                        _propProgress.SizeProcessed = progress.SizeDownloaded;
                        _propProgress.SizeToBeProcessed = progress.SizeToBeDownloaded;
                        _propProgress.Read = progress.Read;
                        _propProgress.Speed = progress.Speed;

                        _propStatus.IsProgressIndetermined = false;
                        _propStatus.ActivitySubStatus = string.Format("{0} / {1}", SummarizeSizeSimple(progress.SizeDownloaded), SummarizeSizeSimple(progress.SizeToBeDownloaded));
                        InvokeProgress();
                        InvokeStatus();

                        string print = GetBothAlignedString($"Downloading package: {Math.Round(progress.ProgressPercentage, 2)}% [{SummarizeSizeSimple(progress.Speed)}/s]...", $"[{SummarizeSizeSimple(progress.SizeDownloaded)} / {SummarizeSizeSimple(progress.SizeToBeDownloaded)}]");
                        Console.Write($"\r{print}");
                    };
                    await FallbackCDNUtil.DownloadCDNFallbackContent(httpClient, zipPath, (byte)(Environment.ProcessorCount >= 4 ? 4 : Environment.ProcessorCount), packageURL, default);
                    Console.WriteLine();
                }
            }
        }

        internal static async Task ExtractPackage(string packageFile, string outputFolder)
        {
            using (FileStream fs = new FileStream(packageFile, FileMode.Open, FileAccess.Read, FileShare.None, 0, FileOptions.DeleteOnClose))
            using (BrotliStream bs = new BrotliStream(fs, CompressionMode.Decompress))
            {
                int i = 1;
                using (TarReader reader = new TarReader(bs, true))
                {
                    while (reader.GetNextEntry() is TarEntry entry)
                    {
                        _propStatus.ActivityStatus = $"{entry.Name}";
                        _propStatus.ActivitySubStatus = $"{i} / ?";
                        _propStatus.IsProgressIndetermined = true;
                        InvokeStatus();
                        Console.Write('\r' + GetBothAlignedString($"Extracting: {entry.Name}...", $"[{i++} / ?]"));
                        await CreateFileFromStream(outputFolder, entry);
                    }
                }
            }
            Console.WriteLine();
        }

        internal static void MoveExtractedPackage(string source, string destination)
        {
            int offset = source.Length + 1;
            string[] files = Directory.GetFiles(source, "*", SearchOption.AllDirectories);

            int i = 1;
            foreach (string sourceFile in files)
            {
                string baseName = sourceFile.AsSpan().Slice(offset).ToString();
                string destPath = Path.Combine(destination, baseName);
                string destDir = Path.GetDirectoryName(destPath)!;

                _propStatus.ActivityStatus = baseName;

                TryDoPathAction(destDir, PathAction.create, PathType.directory);

                try
                {

                    Console.Write('\r' + GetBothAlignedString($"Moving: {baseName}...", $"[{i++} / {files.Length}]"));
                    if (File.Exists(destPath)) File.Delete(destPath);
                    File.Move(sourceFile, destPath);
                }
                catch (Exception ex)
                {
                    Console.WriteLine();
                    Console.WriteLine($"Failed! Copying it instead!\r\n{ex}");
                    try
                    {
                        Console.Write('\r' + GetBothAlignedString($"Copying: {baseName}...", $"[{i++} / {files.Length}]"));
                        File.Copy(sourceFile, destPath, true);
                        Console.WriteLine();
                    }
                    catch
                    {
                        Console.WriteLine($"still Failed!\r\n{ex}");
                        throw;
                    }
                    return;
                }
            }
            Console.WriteLine();
        }

        private static readonly string[] SizeSuffixes = { "B", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB" };
        internal static string SummarizeSizeSimple(double value, int decimalPlaces = 2)
        {
            byte mag = (byte)Math.Log(value, 1000);

            return string.Format("{0} {1}", Math.Round(value / (1L << (mag * 10)), decimalPlaces), SizeSuffixes[mag]);
        }

        private static async Task CreateFileFromStream(string outputDir, TarEntry entry)
        {
            string pathTo = Path.Combine(outputDir, entry.Name);
            string pathDir = Path.GetDirectoryName(pathTo)!;
            TryDoPathAction(pathDir, PathAction.create, PathType.directory);

            if (entry.EntryType == TarEntryType.Directory) return;

            await using FileStream fileStream = File.Create(pathTo);
            await entry.DataStream!.CopyToAsync(fileStream);
        }

        private static bool IsExceptedFiles(string path) => excludeDeleteFile.Any(x => path.Contains(x));

        #region TryDoPatchAction
        internal static void TryDoPathAction(string path, PathAction action, PathType type)
        {
            bool isFile = type == PathType.file;

            if (isFile && action == PathAction.create) throw new ArgumentException("File type doesn't support create action!", "action");

            try
            {
                if (isFile)
                {
                    TryDeleteFile(path);
                    return;
                }

                switch (action)
                {
                    case PathAction.create:
                        TryCreateDirectory(path);
                        break;
                    case PathAction.delete:
                        TryDeleteDirectory(path);
                        break;
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed while trying to {action} a {type}: {path} -> {ex.Message}", ex);
            }
        }

        private static void TryCreateDirectory(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                FileInfo fileInfo = new FileInfo(path);
                fileInfo.IsReadOnly = false;
                fileInfo.Delete();
            }
        }
        #endregion

        #region ConsoleUtil

        private static bool IsConsoleWindowHasWidth()
        {
            try
            {
                _ = Console.WindowWidth;
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static string GetBothAlignedString(string leftString, string rightString)
        {
            if (_windowBufferWidth == 0)
            {
                return leftString + ' ' + rightString;
            }

            int spaceWidth = _windowBufferWidth - 1 - (leftString.Length + rightString.Length);
            if (spaceWidth < 0)
            {
                leftString = leftString.Remove(leftString.Length + (spaceWidth - 4)) + "...";
                spaceWidth = _windowBufferWidth - 1 - (leftString.Length + rightString.Length);
            }
            return leftString + new string(' ', Math.Max(spaceWidth, 0)) + rightString;
        }

        private static void PrintAlignedString(string inputString, StringAlign align, bool isNewLine = false, bool isFlushCurrentLine = false)
        {
            int spaceLength = 0;
            switch (align)
            {
                case StringAlign.right:
                    spaceLength = _windowBufferWidth - inputString.Length;
                    break;
                case StringAlign.center:
                    spaceLength = (int)(((double)_windowBufferWidth / 2) - ((double)inputString.Length / 2));
                    break;
            }

            if (isFlushCurrentLine)
            {
                Console.Write('\r' + new string(' ', spaceLength) + '\r');
            }
            else
            {
                if (_isConsoleWindowHasWidth) Console.CursorLeft = spaceLength;
            }

            if (isNewLine)
            {
                Console.WriteLine(inputString);
            }
            else
            {
                Console.Write(inputString);
            }
        }
        #endregion

        #region Event
        internal static void InvokeStatus() => EventStatus?.Invoke(null, _propStatus);
        internal static void InvokeProgress() => EventProgress?.Invoke(null, _propProgress);
        #endregion
    }

    internal class UpdateStatus
    {
        public string Status { get; set; }
        public string ActivityStatus { get; set; }
        public string ActivitySubStatus { get; set; }
        public bool IsProgressIndetermined { get; set; }
    }

    internal class UpdateProgress
    {
        public long SizeProcessed { get; set; }
        public long SizeToBeProcessed { get; set; }
        public double ProgressPercentage => Math.Round((SizeProcessed / (double)SizeToBeProcessed) * 100, 2);
        public long Read { get; set; }
        public long Speed { get; set; }
        public TimeSpan TimeLeft => checked(TimeSpan.FromSeconds((SizeToBeProcessed - SizeProcessed) / UnZeroed(Speed)));
        private long UnZeroed(long Input) => Math.Max(Input, 1);
    }
}
