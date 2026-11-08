$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\src\ShadowClient\ShadowClient.csproj'
$project = [System.IO.Path]::GetFullPath($project)

dotnet add $project package CmlLib.Core --version 4.1.0
if ($LASTEXITCODE -ne 0) { throw 'Failed to add CmlLib.Core.' }
dotnet add $project package CmlLib.Core.Auth.Microsoft --version 3.4.0
if ($LASTEXITCODE -ne 0) { throw 'Failed to add CmlLib.Core.Auth.Microsoft.' }

dotnet publish $project -c Release -r win-x64 --self-contained true -o (Join-Path $PSScriptRoot '..\publish') `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:DebugType=None `
  -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
