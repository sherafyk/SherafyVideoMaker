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

## Publishing a Portable Build
1. In Visual Studio: **Build → Publish**.
2. Target **Folder**, runtime `win-x64`, **self-contained**.
3. After publish, copy `ffmpeg/`, `temp/`, `output/`, and `logs/` into the publish folder alongside `SherafyVideoMaker.exe`.
4. Zip the publish folder and attach it to a GitHub release.

## Roadmap Ideas
- Looping and advanced speed/trim options.
- Burned-in subtitles from SRT.
- Background music with volume control.
- Template/profile saving.
- CC stock search and licensing hooks.
