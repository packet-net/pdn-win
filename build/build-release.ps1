<#
  Builds the two release assets for a version into dist/:

    dist/pdn-win-<version>-win-x64.msi            the installer (framework-dependent exe inside)
    dist/pdn-win-<version>-win-x64-portable.exe   one self-contained file, no install, no runtime needed

  The release workflow runs this; run it locally to reproduce a release build exactly.

    ./build/build-release.ps1 -Version 1.2.3
    ./build/build-release.ps1 -Version 1.2.3 -PdnSoundModemRoot C:\src\pdn-soundmodem   # against a local checkout
#>
param(
    [Parameter(Mandatory)] [string] $Version,
    [string] $PdnSoundModemRoot
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must be x.y.z (got '$Version')." }
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    Remove-Item publish, dist -Recurse -Force -ErrorAction SilentlyContinue
    # MSBuild reads the environment as properties; an argument would not survive PowerShell, which
    # splits "-p:Name=C:\path" at the drive's colon on its way to a native command.
    $env:PdnSoundModemRoot = $PdnSoundModemRoot

    # The MSI carries the framework-dependent single file: small, and it uses the installed .NET 10
    # Runtime (Windows offers the download on first start if it is missing). Avalonia draws through
    # native libraries (SkiaSharp, HarfBuzzSharp, ANGLE); they go inside the one file too, because
    # the MSI installs only the exe.
    $msi = @('publish', 'src/PdnWin', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'false',
        '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:DebugType=embedded', "-p:Version=$Version", '-o', 'publish/msi')
    & dotnet $msi
    if ($LASTEXITCODE -ne 0) { throw 'publish (msi) failed' }

    # The portable exe is self-contained: everything, runtime included, in one compressed file.
    $portable = @('publish', 'src/PdnWin', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
        '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:EnableCompressionInSingleFile=true',
        '-p:DebugType=embedded', "-p:Version=$Version", '-o', 'publish/portable')
    & dotnet $portable
    if ($LASTEXITCODE -ne 0) { throw 'publish (portable) failed' }

    dotnet build installer/PdnWin.Installer.wixproj -c Release -p:ProductVersion=$Version -p:PublishDir="$root\publish\msi\"
    if ($LASTEXITCODE -ne 0) { throw 'MSI build failed' }

    New-Item -ItemType Directory dist | Out-Null
    Copy-Item installer/bin/Release/pdn-win.msi "dist/pdn-win-$Version-win-x64.msi"
    Copy-Item publish/portable/pdn-win.exe "dist/pdn-win-$Version-win-x64-portable.exe"
    Get-ChildItem dist | Format-Table Name, Length
}
finally {
    Pop-Location
}
