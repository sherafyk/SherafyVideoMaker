# SherafyVideoMaker

A Windows-first WPF tool that turns your audio narration, SRT transcript, and a folder of clips into a rendered video with trimmed/cropped segments and muxed audio.

## Features (MVP)
- Load audio (WAV/MP3), SRT transcript, and a folder of video clips.
- Parse SRT into segments and auto-assign numbered clips (e.g., `1.mp4` → segment 1).
- Validate inputs and assignments before rendering.
- FFmpeg-powered per-segment trimming/speed/crop with aspect presets (16:9, 9:16, 1:1).
- Concatenate processed clips and mux narration into the final video.

## Repository Layout
```
SherafyVideoMaker.sln
src/
  SherafyVideoMaker/
    App.xaml
    MainWindow.xaml
    Models/
    Services/
    SherafyVideoMaker.csproj
```

## Prerequisites (Windows)
- Visual Studio 2022 Community with the **“.NET desktop development”** workload.
- .NET 8 SDK (installs with the workload above).
- Static FFmpeg build for Windows (e.g., from Gyan.dev or BtbN).

## Setup
1. Clone the repo and open `SherafyVideoMaker.sln` in Visual Studio.
2. Build once (Ctrl+Shift+B) to create `bin/Debug/net8.0-windows/`.
3. Create the runtime folders inside that output directory:
   - `ffmpeg/` (place `ffmpeg.exe` and `ffprobe.exe` here)
   - `temp/`
   - `output/`
   - `logs/` (optional)
4. Press **F5** to run.

## Usage
1. In the app, browse for **Audio**, **SRT**, and **Clips Folder**.
2. Choose **Aspect** and **FPS**, then click **Load SRT** to populate segments.
3. Optionally edit clip assignments or speed per row, then click **Validate**.
4. Click **Render** to process segments and mux narration. The final video saves to `output/final_output.mp4`.

## Download portable build
- Download the latest portable zip from the GitHub Releases page (e.g., `v0.1.0-mvp`) – the asset is named `SherafyVideoMaker_Por
table-win-x64.zip`.
- Unzip anywhere, then launch `SherafyVideoMaker.exe`.
- The bundle already contains `ffmpeg/ffmpeg.exe`, `ffmpeg/ffprobe.exe`, plus `temp/`, `output/`, and `logs/` folders ready for
 use.

## Publishing a Portable Build
Use the included PowerShell helper to create a turnkey Windows x64 package (self-contained EXE + folders):

```powershell
pwsh -File scripts/publish-portable.ps1
```

What the script does:
- Runs `dotnet publish` with the required flags:
  - `-c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:PublishTrimmed=false`
- Stages the published files in `artifacts/portable/SherafyVideoMaker_Portable`.
- Downloads a static FFmpeg build (from [gyan.dev](https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip) by default) and copies `ffmpeg.exe` and `ffprobe.exe` into `ffmpeg/`.
- Ensures `temp/`, `output/`, and `logs/` exist beside the EXE.
- Produces `artifacts/SherafyVideoMaker_Portable-win-x64.zip` ready for a GitHub release.

If you already have FFmpeg binaries or want to control the output location, pass parameters such as:

```powershell
pwsh -File scripts/publish-portable.ps1 -SkipFfmpegDownload -PublishDir C:\Builds\SherafyVideoMaker_Portable
```

## Roadmap Ideas
- Looping and advanced speed/trim options.
- Burned-in subtitles from SRT.
- Background music with volume control.
- Template/profile saving.
- CC stock search and licensing hooks.
