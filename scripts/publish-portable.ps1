<#
.SYNOPSIS
Builds a self-contained Windows x64 portable release of SherafyVideoMaker.

.DESCRIPTION
Runs dotnet publish with the required flags, downloads a static FFmpeg build,
and assembles the portable folder layout. Produces a zip archive ready for a
GitHub release.

.EXAMPLE
pwsh -File scripts/publish-portable.ps1

.PARAMETER Configuration
Build configuration to publish (default: Release).

.PARAMETER Runtime
RID to publish for (default: win-x64).

.PARAMETER PublishDir
Optional explicit output directory for the portable layout. Defaults to
artifacts/portable/SherafyVideoMaker_Portable.

.PARAMETER FfmpegUri
Download URL for the FFmpeg static zip (default: gyan.dev essentials build).

.PARAMETER SkipFfmpegDownload
Skip downloading and reusing cached/existing FFmpeg executables.

.PARAMETER SkipZip
Skip creating the final portable zip archive.
#>
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$PublishDir,
    [string]$FfmpegUri = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip",
    [switch]$SkipFfmpegDownload,
    [switch]$SkipZip
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Path $PSScriptRoot -Parent
$portableRoot = if ($PublishDir) { $PublishDir } else { Join-Path $repoRoot "artifacts/portable/SherafyVideoMaker_Portable" }
$cacheRoot = Join-Path $repoRoot "artifacts/portable/ffmpeg-cache"
$ffmpegZipPath = Join-Path $cacheRoot "ffmpeg.zip"
$ffmpegExtractPath = Join-Path $cacheRoot "ffmpeg-extracted"

function Ensure-Directory {
    param([string]$Path)
    if (-not (Test-Path $Path)) {
        New-Item -ItemType Directory -Path $Path | Out-Null
    }
}

Write-Host "Publishing portable build to" $portableRoot
Ensure-Directory (Split-Path -Path $portableRoot -Parent)

Push-Location $repoRoot
try {
    dotnet publish "src/SherafyVideoMaker/SherafyVideoMaker.csproj" `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        /p:PublishSingleFile=true `
        /p:IncludeNativeLibrariesForSelfExtract=true `
        /p:PublishTrimmed=false `
        -o $portableRoot
}
finally {
    Pop-Location
}

$ffmpegTarget = Join-Path $portableRoot "ffmpeg"
$tempDir = Join-Path $portableRoot "temp"
$outputDir = Join-Path $portableRoot "output"
$logsDir = Join-Path $portableRoot "logs"

foreach ($dir in @($ffmpegTarget, $tempDir, $outputDir, $logsDir)) {
    Ensure-Directory $dir
}

if (-not $SkipFfmpegDownload) {
    Ensure-Directory $cacheRoot
    if (-not (Test-Path $ffmpegZipPath)) {
        Write-Host "Downloading FFmpeg from" $FfmpegUri
        Invoke-WebRequest -Uri $FfmpegUri -OutFile $ffmpegZipPath
    }

    if (Test-Path $ffmpegExtractPath) {
        Remove-Item -Recurse -Force $ffmpegExtractPath
    }

    Write-Host "Extracting FFmpeg archive"
    Expand-Archive -Path $ffmpegZipPath -DestinationPath $ffmpegExtractPath -Force

    $ffmpegExe = Get-ChildItem -Path $ffmpegExtractPath -Recurse -Filter "ffmpeg.exe" | Select-Object -First 1
    $ffprobeExe = Get-ChildItem -Path $ffmpegExtractPath -Recurse -Filter "ffprobe.exe" | Select-Object -First 1

    if (-not $ffmpegExe -or -not $ffprobeExe) {
        throw "Could not locate ffmpeg.exe or ffprobe.exe in the downloaded archive."
    }

    Write-Host "Copying FFmpeg binaries into portable layout"
    Copy-Item -Path $ffmpegExe.FullName -Destination (Join-Path $ffmpegTarget "ffmpeg.exe") -Force
    Copy-Item -Path $ffprobeExe.FullName -Destination (Join-Path $ffmpegTarget "ffprobe.exe") -Force
} else {
    Write-Host "Skipping FFmpeg download as requested. Ensure ffmpeg.exe and ffprobe.exe exist in $ffmpegTarget"
}

if (-not $SkipZip) {
    $zipOutput = Join-Path $repoRoot "artifacts/SherafyVideoMaker_Portable-$Runtime.zip"
    if (Test-Path $zipOutput) {
        Remove-Item $zipOutput -Force
    }

    Write-Host "Creating portable zip at" $zipOutput
    Compress-Archive -Path (Join-Path $portableRoot '*') -DestinationPath $zipOutput -Force
}

Write-Host "Portable build ready at" $portableRoot
