@echo off

:START

if not exist "nativeLib" mkdir nativeLib
if not exist "nativeLib\libSkiaSharp.lib" goto :RequireLibMsg
if not exist "nativeLib\libHarfBuzzSharp.lib" goto :RequireLibMsg
if not exist "nativeLib\rcedit.lib" goto :RequireLibMsg
if not exist "nativeLib\rcedit.dll" goto :RequireLibMsg

:: Clean-up the solution and restore all NuGet packages
dotnet clean ..\ --configuration Release
if not %errorlevel% == 0 goto :ErrorMsg
dotnet restore ..\
if not %errorlevel% == 0 goto :ErrorMsg

:: Start compiling using publish
dotnet publish /p:PublishProfile=SinglePublish_win-x64
if not %errorlevel% == 0 goto :ErrorMsg

:: Remove all unused .dll libraries since it's already linked to the executable
del build\*.dll
if not %errorlevel% == 0 goto :ErrorMsg
:: Remove all debug (.pdb) files
del build\*.pdb
if not %errorlevel% == 0 goto :ErrorMsg

:: Rename the executable to ApplyUpdate
move build\ApplyUpdate-Desktop.exe build\ApplyUpdate.exe
if not %errorlevel% == 0 goto :ErrorMsg

goto :SuccessMsg

:RequireLibMsg
	echo libSkiaSharp.lib or libHarfBuzzSharp.lib library doesn't exist in the "nativeLib" folder!
	echo Please download the necessary libraries here:
	echo https://github.com/CollapseLauncher/ApplyUpdate/releases/tag/aotlib-20241201
	echo.
	echo Find the download link for "Avalonia-AOT_minimalLib_########.zip"
	echo then extract the .lib file into "nativeLib" folder and re-run this script
	goto :RESTART

:SuccessMsg
	echo ApplyUpdate has been successfully compiled using Single Publish! Go to the "build" folder to find the compiled executable.
	goto :END

:ErrorMsg
	echo Error has occured while compiling ApplyUpdate using Single Publish.
	goto :END

:RESTART
	pause > nul | echo Press any key to restart the compiling process...
	echo.
	goto :START

:END
	pause > nul | echo Press any key to quit...