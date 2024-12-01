@echo off

:START

if not exist "nativeLib" mkdir nativeLib
if not exist "nativeLib\libSkiaSharp.lib" goto :RequireLibMsg
if not exist "nativeLib\libHarfBuzzSharp.lib" goto :RequireLibMsg
if not exist "nativeLib\rcedit.lib" goto :RequireLibMsg
if not exist "nativeLib\rcedit.dll" goto :RequireLibMsg
if not exist "nativeLib\upx.exe" goto :RequireUpxMsg

:: Clean-up the solution and restore all NuGet packages
dotnet clean ..\ --configuration Release
if not %errorlevel% == 0 goto :ErrorMsg
dotnet restore ..\
if not %errorlevel% == 0 goto :ErrorMsg

:: Start compiling using publish
dotnet publish /p:PublishProfile=NativeAOT_win-x64
if not %errorlevel% == 0 goto :ErrorMsg

:: Remove all unused .dll libraries since it's already linked to the executable
del build\*.dll
if not %errorlevel% == 0 goto :ErrorMsg
:: Remove all debug (.pdb) files
del build\*.pdb
if not %errorlevel% == 0 goto :ErrorMsg

:: Compress executable with UPX (this is used to achieve the smallest size possible)
nativeLib\upx -f -obuild\ApplyUpdate.exe --lzma --best build\ApplyUpdate-Desktop.exe
if not %errorlevel% == 0 goto :ErrorMsg

del build\ApplyUpdate-Desktop.exe
if not %errorlevel% == 0 goto :ErrorMsg

goto :SuccessMsg

:RequireUpxMsg
	echo UPX executable ^(upx.exe^) doesn't exist in the "nativeLib" folder!
	echo Please download the UPX executable here:
	echo https://github.com/upx/upx/releases
	echo.
	echo Find the download link for "upx-x.x.x-win64.zip" file, then extract
	echo the .exe file into "nativeLib" folder and re-run this script
	goto :RESTART

:RequireLibMsg
	echo libSkiaSharp.lib or libHarfBuzzSharp.lib library doesn't exist in the "nativeLib" folder!
	echo Please download the necessary libraries here:
	echo https://github.com/CollapseLauncher/ApplyUpdate/releases/tag/aotlib-20241201
	echo.
	echo Find the download link for "Avalonia-AOT_minimalLib_########.zip"
	echo then extract the .lib file into "nativeLib" folder and re-run this script
	goto :RESTART

:SuccessMsg
	echo ApplyUpdate has been successfully compiled using NativeAOT! Go to the "build" folder to find the compiled executable.
	goto :END

:ErrorMsg
	echo Error has occured while compiling ApplyUpdate using NativeAOT.
	goto :END

:RESTART
	pause > nul | echo Press any key to restart the compiling process...
	echo.
	goto :START

:END
	pause > nul | echo Press any key to quit...