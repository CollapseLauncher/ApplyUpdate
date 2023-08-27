using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace Brotli
{
    internal class LibPathBootStrapper
    {
        internal static string  LibPath { get; private set; }

        static LibPathBootStrapper()
        {
                
            
            string fileName = null;
            if (NativeLibraryLoader.IsWindows)
            {
                if (NativeLibraryLoader.Is64Bit)
                {
                    fileName = "brolib_x64.dll";
                }
                else
                {
                    fileName = "brolib_x86.dll";
                }
            } else if (NativeLibraryLoader.IsLinux)
            {
                if (NativeLibraryLoader.Is64Bit)
                {
                    fileName = "brolib_x64.so";
                }
                else
                {
                    fileName = "brolib_x86.so";
                }
            } else if (NativeLibraryLoader.IsMacOSX)
            {
                if (NativeLibraryLoader.Is64Bit)
                {
                    fileName = "brolib_x64.dylib";
                }
            }
            if (string.IsNullOrEmpty(fileName)) throw new NotSupportedException($"OS not supported:{Environment.OSVersion.ToString()}");
            LibPath = "brolib_x64.dll";
            bool libFound = true;

            if (!libFound) throw new NotSupportedException($"Unable to find library {fileName}");
        }
    }
}
