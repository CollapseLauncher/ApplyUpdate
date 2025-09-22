using Avalonia;
using Hi3Helper;
#if !DEBUG
using Hi3Helper.Win32.Native.LibraryImport;
#endif
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static Hi3Helper.Logger;

using ZstdCompressionStream = ZstdNet.CompressionStream;
using ZstdCompressionOptions = ZstdNet.CompressionOptions;
using ZstdNet;
using System.Collections.ObjectModel;

namespace ApplyUpdate;

public static partial class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        ZstdNet.DllUtils.IsIgnoreMissingLibrary = true;

        // PInvoke.AllocateConsole();
        CurrentLogger = new LoggerConsole("", Encoding.Unicode);
        Console.OutputEncoding = Encoding.Unicode;
        Console.Title = "Collapse Launcher ApplyUpdate Console";

        if (args.Length == 3 && args[0].ToLower() == "changevelopackinstperms")
        {
            string permission = args[1];
            string executableFile = args[2];

            ChangeVelopackExecPerms(permission, executableFile);
            return;
        }

        if (args.Length != 0 && args[0].ToLower() == "compress")
        {
#if !DEBUG
            PInvoke.AllocConsole();
#endif
            if (CompressMode(args) > 0) Console.ReadLine();
            return;
        }

        if (Directory.GetCurrentDirectory().Trim('\\') != UpdateTask.realExecDir.Trim('\\'))
        {
            LogWriteLine($"Moving to the right working directory ({UpdateTask.realExecDir})...");
            Directory.SetCurrentDirectory(UpdateTask.realExecDir);
        }

#if !DEBUG
        // PInvoke.AllocateConsole();
        PInvoke.ShowWindow(PInvoke.GetConsoleWindow(), 0);
#endif

        BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UseSkia()
            .UseWin32()
#if DEBUG
            .LogToTrace()
#endif
        // .UseReactiveUI()
        ;

    [LibraryImport("nativeLib/rcedit", EntryPoint = "ChangeExecutablePermission", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int ChangePermissionNative(string execPath, string permission);

    private static byte[] VelopackPkgSignature = [
        0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x08
        ];

    private static void ChangeVelopackExecPerms(string permission, string execPath)
    {
        string execTempPath = Path.Combine(execPath + ".temp");
        string execNewPath = Path.Combine(execPath + ".new");
        if (!File.Exists(execPath))
            throw new FileNotFoundException("File not found!", execPath);

        if (!(permission.Equals("requireAdministrator")
           || permission.Equals("asInvoker")
           || permission.Equals("highestAvailable")))
        {
            throw new InvalidOperationException($"Unable to set execution level: {permission}. Valid execution level: asInvoker, highestAvailable or requireAdministrator");
        }

        try
        {
            File.Copy(execPath, execTempPath, true);
            int result;
            switch (result = ChangePermissionNative(execTempPath, permission))
            {
                case 0:
                    break;
                case 1:
                    throw new InvalidOperationException("Failed to apply permission changes on native library (rcedit.dll)");
                case 2:
                    throw new InvalidOperationException($"Unable to set execution level: {permission}. Valid execution level: asInvoker, highestAvailable or requireAdministrator");
                case 3:
                    throw new InvalidOperationException($"Unable to load file: {execPath}. Valid execution level: asInvoker, highestAvailable or requireAdministrator");
                default:
                    throw new InvalidOperationException($"Unknown error: {result}");
            }

            Span<byte> oldFileBytes = File.ReadAllBytes(execPath);
            Span<byte> newFileBytes = File.ReadAllBytes(execTempPath);
            LogWriteLine($"Changed Velopack installer permission to: {permission} for file: {execPath}");

            int pkgOffset = oldFileBytes.IndexOf(VelopackPkgSignature);
            if (pkgOffset == -1)
                throw new IndexOutOfRangeException("Unable to find the Velopack Package data inside of the executable!");

            LogWriteLine($"Got offset of the package data at: {pkgOffset} (0x{pkgOffset:x8})");
            ReadOnlySpan<byte> zipPkgBuffer = oldFileBytes.Slice(pkgOffset);

            using (FileStream fileStream = new FileStream(execNewPath, FileMode.Create, FileAccess.Write))
            {
                LogWriteLine("Writing executable data...");
                fileStream.Write(newFileBytes);
                LogWriteLine("Writing Velopack Package data...");
                fileStream.Write(zipPkgBuffer);
            }

            File.Move(execNewPath, execPath, true);
            LogWriteLine("Done!");
        }
        finally
        {
            File.Delete(execTempPath);
        }
    }

    private static int CompressMode(params string[] args)
    {
        if (args.Length != 3)
        {
            LogWriteLine("Please define Input directory and Output file path!", LogType.Error);
            return 1;
        }

        FileInfo packageTarget = new FileInfo(args[2]);
        DirectoryInfo sourceDirectory = new DirectoryInfo(args[1]);
        DirectoryInfo? targetDirectory = packageTarget.Directory;
        if (!sourceDirectory.Exists)
        {
            LogWriteLine("Input directory doesn't exist!", LogType.Error);
            LogWriteLine("Path: " + args[1], LogType.Error);
            return 2;
        }

        targetDirectory?.Create();

        using FileStream zipArchiveStream = packageTarget.Create();
        using ZipArchive zipArchive = new(zipArchiveStream, ZipArchiveMode.Create, false);

        FileInfo[] fileInfos = sourceDirectory.GetFiles("*", SearchOption.AllDirectories);

        int index = 1;
        int fileCount = fileInfos.Length;

        long fileSizeProcessed = 0;
        long fileTotalSize = fileInfos.Sum(x => x.Length);

        string baseDir = sourceDirectory.FullName;
        Lock thisLock = new();

        LogWriteLine("Input path: " + args[1]);
        LogWriteLine("Output path: " + args[2]);
        LogWriteLine("Input file size: " + fileTotalSize + " bytes");

        Parallel.ForEach(fileInfos, ImplWriteTo);

        Console.WriteLine(" Completed!");
        LogWriteLine("Output file size: " + zipArchiveStream.Length + " bytes");
        LogWriteLine($"Compression ratio: {Math.Round((double)zipArchiveStream.Length / fileTotalSize * 100, 4)}%");

        return 0;

        void ImplWriteTo(FileInfo fileInfo)
        {
            const int bufferSize = 512 << 10;

            byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
            Span<byte> bufferSpan = buffer;

            string fullName = fileInfo.FullName;
            string relativeName = fullName.AsSpan(baseDir.Length).TrimStart("/\\").ToString() + ".zstd";

            try
            {
                DateTime lastWriteTime = fileInfo.LastWriteTime;

                if (lastWriteTime.Year is < 1980 or > 2107)
                    lastWriteTime = new DateTime(1980, 1, 1, 0, 0, 0);

                using MemoryStream memoryStream = new();
                ZstdCompressionStream compressStream = new(memoryStream, new ZstdCompressionOptions(22));
                using FileStream fileStream = fileInfo.OpenRead();

                int read;
                while ((read = fileStream.Read(bufferSpan)) > 0)
                {
                    compressStream.Write(bufferSpan[..read]);
                    Interlocked.Add(ref fileSizeProcessed, read);

                    Console.Write($"\rCompressing: [{index} / {fileCount}] {Math.Round((double)fileSizeProcessed / fileTotalSize * 100, 4)}%...");
                }

                compressStream.Dispose();
                memoryStream.Flush();
                memoryStream.Position = 0;

                using (thisLock.EnterScope())
                {
                    ZipArchiveEntry entry = zipArchive.CreateEntry(relativeName, CompressionLevel.NoCompression);
                    entry.LastWriteTime = lastWriteTime;

                    using Stream entryStream = entry.Open();
                    memoryStream.CopyTo(entryStream);
                    index++;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }
}
