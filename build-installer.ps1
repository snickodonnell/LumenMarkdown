$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

dotnet run --project (Join-Path $root "tests\LumenMarkdown.Tests\LumenMarkdown.Tests.csproj") -c Release
dotnet publish (Join-Path $root "src\LumenMarkdown\LumenMarkdown.csproj") -c Release -r win-x64 --self-contained false -p:DebugType=None -o (Join-Path $root "dist")
dotnet build (Join-Path $root "installer\LumenMarkdown.Installer.wixproj") -c Release

Write-Host "Installer built in $root\installer-output"
