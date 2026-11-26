@echo off
cd /d "%~dp0"
set "DOTNET_CLI_HOME=%~dp0.build-home"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=0"
set "DOTNET_GENERATE_ASPNET_CERTIFICATE=0"
set "NUGET_PACKAGES=%~dp0.build-packages"
dotnet restore "src\EarSubtitle.csproj" --configfile NuGet.Config -p:NuGetAudit=false
if errorlevel 1 goto fail
dotnet publish "src\EarSubtitle.csproj" -c Release --no-restore --self-contained false -o app
if errorlevel 1 goto fail
echo Build completed. Open Launch.cmd.
exit /b 0
:fail
echo Build failed.
pause
exit /b 1
