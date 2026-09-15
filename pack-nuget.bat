@echo off
setlocal EnableExtensions DisableDelayedExpansion

set "PROJECT=%~dp0fluid-script\FluidScript.csproj"
set "OUTPUT=%~dp0artifacts\nuget"

if not exist "%PROJECT%" (
    echo Could not find the FluidScript project: "%PROJECT%"
    exit /b 1
)

for /f "usebackq delims=" %%V in (`powershell -NoProfile -Command "$projectPath = $args[0]; $xml = New-Object System.Xml.XmlDocument; $xml.PreserveWhitespace = $true; $xml.Load($projectPath); $version = $xml.SelectSingleNode('/Project/PropertyGroup/Version'); if ($null -eq $version -or $version.InnerText -notmatch '^\d+\.\d+\.\d+$') { throw 'The project Version must use major.minor.patch format.' }; $parts = $version.InnerText -split '\.'; '{0}.{1}.{2}' -f $parts[0], $parts[1], ([int]$parts[2] + 1)" "%PROJECT%"`) do set "NEW_VERSION=%%V"

if not defined NEW_VERSION (
    echo Could not determine the next package version.
    exit /b 1
)

echo Packing FluidScript %NEW_VERSION%...
dotnet pack "%PROJECT%" --configuration Release --output "%OUTPUT%" --p:Version="%NEW_VERSION%"
if errorlevel 1 (
    echo Package creation failed. The project version was not changed.
    exit /b 1
)

powershell -NoProfile -Command "$projectPath = $args[0]; $newVersion = $args[1]; $xml = New-Object System.Xml.XmlDocument; $xml.PreserveWhitespace = $true; $xml.Load($projectPath); $version = $xml.SelectSingleNode('/Project/PropertyGroup/Version'); if ($null -eq $version) { throw 'The project Version element was not found.' }; $version.InnerText = $newVersion; $settings = New-Object System.Xml.XmlWriterSettings; $settings.Encoding = New-Object System.Text.UTF8Encoding($true); $settings.Indent = $false; $writer = [System.Xml.XmlWriter]::Create($projectPath, $settings); try { $xml.Save($writer) } finally { $writer.Dispose() }" "%PROJECT%" "%NEW_VERSION%"
if errorlevel 1 (
    echo Package was created, but the project version could not be updated.
    exit /b 1
)

echo Created "%OUTPUT%\FluidScript.%NEW_VERSION%.nupkg"
exit /b 0
