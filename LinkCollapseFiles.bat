@echo off
%~d0
cd %~dp0
if /i not exist ..\Collapse goto :RequireCollapseRepo

:Linking
if /i not exist ApplyUpdate-Core\Assets\Locale mkdir ApplyUpdate-Core\Assets\Locale || goto :ErrorOccured
call :MakeDirLink ApplyUpdate-Core\Logger ..\Collapse\Hi3Helper.Core\Classes\Logger
call :MakeFileLink ApplyUpdate-Core\Localization.cs ..\Collapse\Hi3Helper.Core\Lang\Localization.cs
call :MakeFileLink ApplyUpdate-Core\InvokeProp.cs ..\Collapse\Hi3Helper.Core\Classes\Data\InvokeProp.cs
call :MakeFileLink ApplyUpdate-Core\IniFile.cs ..\Collapse\Hi3Helper.Core\Classes\Data\Tools\IniFile.cs
call :MakeFileLink ApplyUpdate-Core\LangUpdatePage.cs ..\Collapse\Hi3Helper.Core\Lang\Locale\LangUpdatePage.cs
for /r ..\Collapse\Hi3Helper.Core\Lang\ %%a in (*.json) do (
	call :MakeFileLink ApplyUpdate-Core\Assets\Locale\%%~nxa %%~fa
)
goto :Success

:RequireCollapseRepo
echo Collapse repository is required at ..\Collapse path to load linked files.
echo Please clone the repo first and try to compile the program once again!
goto :End

:ErrorOccured
echo Error has occured with error code: %errorlevel%
goto :End

:Success
echo Linking localization file is done!
goto :End

:MakeDirLink
(
	echo Making directory link from %~f2 to %~1
	if /i not exist %~1 mklink /D %~1 %~f2 > nul || goto :ErrorOccured
	goto :EOF
)

:MakeFileLink
(
	echo Making file link from %~f2 to %~1
	if /i not exist %~1 mklink /H %~1 %~f2 > nul || goto :ErrorOccured
	goto :EOF
)

:End
pause > nul | echo Press any key to quit...
goto :EOF