using Hi3Helper;
using Hi3Helper.Http;
using Hi3Helper.Win32.Native.Enums;
using Hi3Helper.Win32.Native.LibraryImport;
using System;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

using static Hi3Helper.Locale;
using static Hi3Helper.Logger;

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
        public static string launcherPath = Path.Combine(workingDir, "current", "CollapseLauncher.exe");
        public static string launcherAppDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"AppData\LocalLow\CollapseLauncher");

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

        internal static bool IsCollapseRunning() => EnumerateInstances() > 0;

        public static Process[] GetCollapseInstanceProcesses()
        {
            var processes = Process.GetProcessesByName("CollapseLauncher");

            return processes;
        }

        public static void TryKillAllCollapseProcesses()
        {
            foreach (var process in GetCollapseInstanceProcesses())
            {
                try
                {
                    process?.Kill();
                }
                catch (Exception ex)
                {
                    LogWriteLine($"Failed while killing Collapse process with PID: {process.Id}\r\n{ex}", Hi3Helper.LogType.Error, true);
                }
                process?.Dispose();
            }
        }

        internal static int EnumerateInstances()
        {
            var instanceProc = GetCollapseInstanceProcesses();
            var finalInstanceCount = 0;

            foreach (Process p in instanceProc)
            {
                if (p == null) continue;
                try
                {
                    if (p.MainWindowHandle == IntPtr.Zero)
                    {
                        LogWriteLine("Process does not have window, skipping...", Hi3Helper.LogType.NoTag, true);
                        continue;
                    }

                    LogWriteLine($"Name: {p.ProcessName}", LogType.NoTag, true);
                    LogWriteLine($"MainModule: {p.MainModule?.FileName}", LogType.NoTag, true);
                    LogWriteLine($"PID: {p.Id}", LogType.NoTag, true);

                    finalInstanceCount++;
                }
                catch (Exception ex)
                {
                    LogWriteLine($"Failed when trying to fetch an instance information! " +
                                 $"InstanceCount is not incremented.\r\n{ex}",
                                 LogType.Error, true);
                }
                p?.Dispose();
            }

            return finalInstanceCount;
        }

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
                LogWriteLine($"\"release\" file doesn't exist in \"_Temp\\release\" or current directory", LogType.Warning);

                return null;
            }

            string[] lines = File.ReadAllLines(stampFile);
            if (lines == null || lines.Length == 0 || !(lines[0].ToLower() == "stable" || lines[0].ToLower() == "preview"))
            {
                LogWriteLine($"Content of the \"release\" stamp file is empty. The content of the file must be \"preview\" or \"stable\"", Hi3Helper.LogType.Warning);
                return null;
            }

            return lines[0];
        }

        internal static bool IsSquirrelJunkExist()
        {
            string stubPath = Path.Combine(workingDir, "CollapseLauncher.exe");
            string createDumpPath = Path.Combine(workingDir, "createdump.exe");
            string restartAgentPath = Path.Combine(workingDir, "RestartAgent.exe");

            if (File.Exists(stubPath) &&
                File.Exists(createDumpPath) &&
                File.Exists(restartAgentPath) &&
                IsSquirrelAppDirExist())
            {
                return true;
            }

            return false;
        }

        internal static bool IsSquirrelAppDirExist()
        {
            return Directory.EnumerateDirectories(workingDir, "app-*", SearchOption.TopDirectoryOnly)
                .Any(x => Directory.EnumerateFiles(x, "*", SearchOption.TopDirectoryOnly)
                    .Any(y => y.EndsWith("CollapseLauncher.exe")));
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

            string title = string.Format(Lang._UpdatePage.ApplyUpdateTaskLegacyVerFoundTitle, version);
            string msg = string.Format(Lang._UpdatePage.ApplyUpdateTaskLegacyVerFoundSubtitle1, version)
                + Lang._UpdatePage.ApplyUpdateTaskLegacyVerFoundSubtitle2
                + Lang._UpdatePage.ApplyUpdateTaskLegacyVerFoundSubtitle3
                + Lang._UpdatePage.ApplyUpdateTaskLegacyVerFoundSubtitle4;

            MessageBoxFlags msgStyle = MessageBoxFlags.MB_YESNO | MessageBoxFlags.MB_ICONWARNING | MessageBoxFlags.MB_SYSTEMMODAL;
            nint currentParentHwnd = App.GetCurrentWindowHwnd();
            MessageBoxResult result = PInvoke.MessageBox(currentParentHwnd, msg, title, msgStyle);
            // Result:
            //    7 = No
            //    6 = Yes
            if (result == MessageBoxResult.IDNO) return false;

            result = PInvoke.MessageBox(currentParentHwnd, Lang._UpdatePage.ApplyUpdateTaskLegacyVerFoundSubtitle5, title, msgStyle);
            if (result == MessageBoxResult.IDNO) return false;

            int count = 5;
            while (count > 0)
            {
                _propStatus.Status = string.Format(Lang._UpdatePage.ApplyUpdateTaskLegacyCleanupCount, count);
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
                        _propStatus.ActivityStatus = string.Format(Lang._UpdatePage.ApplyUpdateTaskLegacyDeleting, path.Substring(offset));
                        InvokeStatus();
                        Console.Write('\r' + GetBothAlignedString($"Deleting folder: {path.Substring(offset)}...", $"[{i++} / ?]"));
                        TryDoPathAction(path, PathAction.delete, PathType.directory);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine();
                        LogWriteLine($"Error!\r\n{ex}", LogType.Error);
                    }
                }
            }

            foreach (string path in Directory.EnumerateFiles(workingDir, "*.*", SearchOption.AllDirectories))
            {
                if (!IsExceptedFiles(path))
                {
                    try
                    {
                        _propStatus.ActivityStatus = string.Format(Lang._UpdatePage.ApplyUpdateTaskLegacyDeleting, path.Substring(offset));
                        InvokeStatus();
                        Console.Write('\r' + GetBothAlignedString($"Deleting file: {path.Substring(offset)}...", $"[{i++} / ?]"));
                        TryDoPathAction(path, PathAction.delete, PathType.file);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine();
                        LogWriteLine($"Error!\r\n{ex}", LogType.Error);
                    }
                }
            }

            string applyUpdateConfig = Path.Combine(workingDir, "ApplyUpdate.exe.config");
            if (File.Exists(applyUpdateConfig))
            {
                try
                {
                    _propStatus.ActivityStatus = string.Format(Lang._UpdatePage.ApplyUpdateTaskLegacyDeleting, applyUpdateConfig.Substring(offset));
                    InvokeStatus();
                    Console.Write('\r' + GetBothAlignedString($"Deleting file: {applyUpdateConfig.Substring(offset)}...", $"[{i++} / ?]"));
                    TryDoPathAction(applyUpdateConfig, PathAction.delete, PathType.file);
                }
                catch (Exception ex)
                {
                    Console.WriteLine();
                    LogWriteLine($"Error!\r\n{ex}", Hi3Helper.LogType.Error);
                }
            }
            Console.WriteLine();

            return true;
        }

        internal static async Task DownloadPackage(string stamp)
        {
            if (!File.Exists(zipPath))
            {
                string packageURL = FallbackCDNUtil.CombineURLFromString("velopack", stamp, "latest");
                DownloadClient downloadClient = DownloadClient.CreateInstance(MainWindow.GlobalHttpClient);

                _propStatus.ActivityStatus = Lang._UpdatePage.ApplyUpdateTaskDownloadingPkgTitle;
                InvokeStatus();
                Stopwatch localStopwatch = Stopwatch.StartNew();
                FallbackCDNUtil.DownloadProgress += (_, progress) =>
                {
                    _propProgress.SizeProcessed = progress.SizeDownloaded;
                    _propProgress.SizeToBeProcessed = progress.SizeToBeDownloaded;
                    _propProgress.Read = progress.Read;

                    long speed = (long)(progress.SizeDownloaded / localStopwatch.Elapsed.TotalSeconds);

                    _propProgress.Speed = speed;

                    _propStatus.IsProgressIndetermined = false;
                    _propStatus.ActivitySubStatus = string.Format("{0} / {1}", SummarizeSizeSimple(progress.SizeDownloaded), SummarizeSizeSimple(progress.SizeToBeDownloaded));
                    InvokeProgress();
                    InvokeStatus();

                    string print = GetBothAlignedString($"Downloading package: {Math.Round(progress.ProgressPercentage, 2)}% [{SummarizeSizeSimple(progress.Speed)}/s]...", $"[{SummarizeSizeSimple(progress.SizeDownloaded)} / {SummarizeSizeSimple(progress.SizeToBeDownloaded)}]");
                    Console.Write($"\r{print}");
                };
                await FallbackCDNUtil.DownloadCDNFallbackContent(downloadClient, zipPath, (byte)(Environment.ProcessorCount >= 4 ? 4 : Environment.ProcessorCount), packageURL, default);
                Console.WriteLine();
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
                    catch (Exception ex1)
                    {
                        LogWriteLine($"still Failed!\r\n{ex1}", Hi3Helper.LogType.Error);
                        throw;
                    }
                    return;
                }
            }
            Console.WriteLine();
        }

        internal static string SummarizeSizeSimple(double value, int decimalPlaces = 2)
        {
            byte mag = (byte)Math.Log(value, 1000);

            return string.Format("{0} {1}", Math.Round(value / (1L << (mag * 10)), decimalPlaces), ConverterTool.SizeSuffixes[mag]);
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
            DirectoryInfo directoryInfo = new DirectoryInfo(path);
            if (directoryInfo.Exists)
            {
                foreach (FileInfo fileInfo in directoryInfo.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    TryDeleteFile(fileInfo);
                }
            }
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }

        private static void TryDeleteFile(string path)
        {
            FileInfo fileInfo = new FileInfo(path);
            TryDeleteFile(fileInfo);
        }

        private static void TryDeleteFile(FileInfo fileInfo)
        {
            if (fileInfo.Exists)
            {
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
