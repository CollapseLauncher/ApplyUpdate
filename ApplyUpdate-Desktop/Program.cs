using Avalonia;
using Hi3Helper;
using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

using static Hi3Helper.Logger;

namespace ApplyUpdate;

public static partial class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // PInvoke.AllocateConsole();
        Logger._log = new LoggerConsole("", Encoding.Unicode);
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
            PInvoke.AllocateConsole();
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
        PInvoke.ShowWindow(PInvoke.m_consoleWindow, 0);
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
                LogWriteLine($"Writing executable data...");
                fileStream.Write(newFileBytes);
                LogWriteLine($"Writing Velopack Package data...");
                fileStream.Write(zipPkgBuffer);
            }

            File.Move(execNewPath, execPath, true);
            LogWriteLine($"Done!");
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
            LogWriteLine("Please define Input and Output file path!", LogType.Error);
            return 1;
        }

        if (!File.Exists(args[1]))
        {
            LogWriteLine("Input file doesn't exist!", LogType.Error);
            LogWriteLine("Path: " + args[1], LogType.Error);
            return 2;
        }

        if (!Directory.Exists(Path.GetDirectoryName(args[2])))
        {
            LogWriteLine("Output directory from given path doesn't exist!", LogType.Error);
            LogWriteLine("Path: " + args[2], LogType.Error);
            return 2;
        }

        using (FileStream fsi = new FileStream(args[1], FileMode.Open, FileAccess.Read))
        using (FileStream fso = new FileStream(args[2], FileMode.Create, FileAccess.Write))
        using (BrotliStream bso = new BrotliStream(fso, CompressionMode.Compress, true))
        {
            LogWriteLine("Input path: " + args[1]);
            LogWriteLine("Output path: " + args[2]);
            byte[] buffer = new byte[4 << 14];
            LogWriteLine("Input filesize: " + fsi.Length + " bytes");

            int read = 0;
            long curRead = 0;
            long length = fsi.Length;
            while ((read = fsi.Read(buffer, 0, buffer.Length)) > 0)
            {
                curRead += read;
                Console.Write($"\rCompressing: {Math.Round(((double)curRead / length) * 100, 4)}%...");
                bso.Write(buffer, 0, read);
            }
            Console.WriteLine(" Completed!");
            LogWriteLine("Output filesize: " + fso.Length + " bytes", LogType.Error);
            LogWriteLine($"Compression ratio: {Math.Round((double)fso.Length / fsi.Length * 100, 4)}%", LogType.Error);
        }

        return 0;
    }
}
