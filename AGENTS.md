## 0. Big Picture: What This MVP Will Do

**User flow (your vision, simplified for MVP):**

1. Open app (portable EXE).
2. Drop in:

   * one **audio** file (MP3/WAV narration),
   * one **SRT** file (sentence-level transcript with timestamps),
   * a **folder of video clips**.
3. App reads SRT → creates **segments**.
4. For each segment, user assigns a clip (can auto-suggest `1.mp4 → segment 1`, etc.).
5. For each segment + clip, app:

   * trims or slows/speeds to fit the slot,
   * auto-crops/centers to chosen aspect ratio (e.g. vertical 9:16),
   * mutes clip audio by default.
6. App concatenates all processed clips in order.
7. App muxes final video + your audio narration → **final video**.
8. No Shotcut. Just drag, click, render.

We’ll build a **minimal-but-real** version of this first, and later you (or a dev) can add:

* looping, more advanced speed options
* background music
* crossfades, LUTs, template saving
* CC search (Pexels/Pixabay/Unsplash)
* licensing & auto-updates

---

## 1. Install Your Tools

### 1.1 Install Visual Studio Community

1. Go to the official Visual Studio site (search “Visual Studio Community 2022” in your browser).
2. Download **Visual Studio Community** (free).
3. Run the installer.
4. In the workloads screen, **check**:

   * ✅ **“.NET desktop development”**
5. Click **Install** and let it finish.

> This gives you the environment to build Windows apps in C#.

---

### 1.2 Download FFmpeg (portable binaries)

1. Search for: **“FFmpeg Windows static builds”** (look for a well-known provider like Gyan.dev or BtbN).

2. Download a **static build** for Windows (usually a `.zip` file).

3. When the download finishes:

   * Right-click the zip → **Extract All…**
   * You’ll get a folder like `ffmpeg-xxxx-win64-static`.

4. Inside that folder, find `bin\ffmpeg.exe` and `bin\ffprobe.exe`.

We’ll use those in your app’s folder later.

---

## 2. Create Your Project

### 2.1 Start a New WPF App

1. Open **Visual Studio**.
2. Click **Create a new project**.
3. In the search box type: `WPF App`.
4. Select:

   * **WPF App (.NET)** (C#)
5. Click **Next**.
6. Project name: `SherafyVideoMaker`
7. Location: pick something simple, e.g. `C:\Users\YourName\Source\SherafyVideoMaker`
8. Click **Create**.
9. When asked for .NET version:

   * Choose `.NET 8.0` if available (otherwise .NET 6.0).

You’ll see a basic window app with a `MainWindow.xaml`.

---

## 3. Set Up Portable Folder Structure

Ultimately, your portable app will look like:

```
SherafyVideoMaker/
│
├── SherafyVideoMaker.exe        (your built app)
├── ffmpeg/
│     ├── ffmpeg.exe
│     └── ffprobe.exe
├── temp/
├── output/
└── (optionally) settings.json, logs, etc.
```

For development, Visual Studio builds into a folder like:

`...SherafyVideoMaker\bin\Debug\net8.0-windows\`

We’ll mimic the final structure there:

1. Build once (Ctrl+Shift+B) so the `bin` folder exists.

2. In **File Explorer**, go to:

   `...\SherafyVideoMaker\bin\Debug\net8.0-windows\`

3. Create folders:

   * `ffmpeg`
   * `temp`
   * `output`
   * `logs` (optional)

4. Copy `ffmpeg.exe` and `ffprobe.exe` from the static build’s `bin` into that `ffmpeg` folder.

> Later, when we publish the app, we’ll copy this structure as “the portable app folder.”

---

## 4. Design a Simple GUI (First Version)

We’ll create:

* File selectors for: Audio, SRT, Clips folder
* Dropdowns for aspect ratio, FPS
* A table to show segments
* Buttons: **Load**, **Validate**, **Render**

Open `MainWindow.xaml` in Visual Studio. Replace the default grid with something like this:

```xml
<Window x:Class="SherafyVideoMaker.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="AutoBroll Builder - MVP" Height="600" Width="900">
    <Grid Margin="10">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>   <!-- Inputs -->
            <RowDefinition Height="Auto"/>   <!-- Settings -->
            <RowDefinition Height="*"/>      <!-- Segments table -->
            <RowDefinition Height="Auto"/>   <!-- Buttons & log -->
        </Grid.RowDefinitions>

        <!-- INPUTS -->
        <GroupBox Header="Inputs" Grid.Row="0" Margin="0,0,0,10">
            <StackPanel Margin="10">
                <StackPanel Orientation="Horizontal" Margin="0,0,0,5">
                    <TextBlock Text="Audio file:" Width="100" VerticalAlignment="Center"/>
                    <TextBox x:Name="AudioPathTextBox" Width="600" Margin="5,0" IsReadOnly="True"/>
                    <Button Content="Browse..." Width="80" Click="BrowseAudio_Click"/>
                </StackPanel>

                <StackPanel Orientation="Horizontal" Margin="0,0,0,5">
                    <TextBlock Text="SRT file:" Width="100" VerticalAlignment="Center"/>
                    <TextBox x:Name="SrtPathTextBox" Width="600" Margin="5,0" IsReadOnly="True"/>
                    <Button Content="Browse..." Width="80" Click="BrowseSrt_Click"/>
                </StackPanel>

                <StackPanel Orientation="Horizontal">
                    <TextBlock Text="Clips folder:" Width="100" VerticalAlignment="Center"/>
                    <TextBox x:Name="ClipsFolderTextBox" Width="600" Margin="5,0" IsReadOnly="True"/>
                    <Button Content="Browse..." Width="80" Click="BrowseClipsFolder_Click"/>
                </StackPanel>
            </StackPanel>
        </GroupBox>

        <!-- SETTINGS -->
        <GroupBox Header="Output Settings" Grid.Row="1" Margin="0,0,0,10">
            <StackPanel Orientation="Horizontal" Margin="10">
                <StackPanel Margin="0,0,20,0">
                    <TextBlock Text="Aspect Ratio / Resolution"/>
                    <ComboBox x:Name="AspectComboBox" Width="200" SelectedIndex="0">
                        <ComboBoxItem Content="1080 x 1920 (Vertical 9:16)" Tag="1080x1920"/>
                        <ComboBoxItem Content="1920 x 1080 (Horizontal 16:9)" Tag="1920x1080"/>
                    </ComboBox>
                </StackPanel>

                <StackPanel Margin="0,0,20,0">
                    <TextBlock Text="FPS"/>
                    <ComboBox x:Name="FpsComboBox" Width="80" SelectedIndex="1">
                        <ComboBoxItem Content="24"/>
                        <ComboBoxItem Content="30"/>
                        <ComboBoxItem Content="60"/>
                    </ComboBox>
                </StackPanel>
            </StackPanel>
        </GroupBox>

        <!-- SEGMENTS TABLE -->
        <GroupBox Header="Segments" Grid.Row="2" Margin="0,0,0,10">
            <DataGrid x:Name="SegmentsDataGrid" AutoGenerateColumns="False" Margin="5">
                <DataGrid.Columns>
                    <DataGridTextColumn Header="#" Binding="{Binding Index}" Width="40"/>
                    <DataGridTextColumn Header="Start (s)" Binding="{Binding Start}" Width="80"/>
                    <DataGridTextColumn Header="End (s)" Binding="{Binding End}" Width="80"/>
                    <DataGridTextColumn Header="Duration (s)" Binding="{Binding Duration}" Width="80"/>
                    <DataGridTextColumn Header="Assigned Clip" Binding="{Binding AssignedClipPath}" Width="*"/>
                </DataGrid.Columns>
            </DataGrid>
        </GroupBox>

        <!-- BUTTONS + LOG -->
        <StackPanel Grid.Row="3">
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,0,0,5">
                <Button Content="Load SRT" Width="100" Margin="5" Click="LoadSrt_Click"/>
                <Button Content="Validate" Width="100" Margin="5" Click="Validate_Click"/>
                <Button Content="Render" Width="100" Margin="5" Click="Render_Click"/>
            </StackPanel>

            <TextBox x:Name="LogTextBox" Height="100" Margin="0,0,0,0"
                     TextWrapping="Wrap" VerticalScrollBarVisibility="Auto"
                     IsReadOnly="True"/>
        </StackPanel>
    </Grid>
</Window>
```

This GUI is simple but functional for MVP.

---

## 5. Add Core Classes: Settings & Segment Model

Open `MainWindow.xaml.cs` and add at the **top of the file**:

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using System.Collections.ObjectModel;
```

Above the `MainWindow` class, add:

```csharp
public class ProjectSettings
{
    public string AudioPath { get; set; }
    public string SrtPath { get; set; }
    public string ClipsFolder { get; set; }

    public int OutputWidth { get; set; }
    public int OutputHeight { get; set; }
    public int Fps { get; set; }
}

public class Segment
{
    public int Index { get; set; }
    public double Start { get; set; }
    public double End { get; set; }
    public double Duration => End - Start;
    public string AssignedClipPath { get; set; }
}
```

Inside `MainWindow` class, add:

```csharp
public partial class MainWindow : Window
{
    private ProjectSettings Settings = new ProjectSettings();
    private ObservableCollection<Segment> Segments = new ObservableCollection<Segment>();

    public MainWindow()
    {
        InitializeComponent();
        SegmentsDataGrid.ItemsSource = Segments;
    }

    // (we will add handlers below)
}
```

---

## 6. Implement File Browsing

Still in `MainWindow.xaml.cs`, inside `MainWindow` class, add:

```csharp
private void BrowseAudio_Click(object sender, RoutedEventArgs e)
{
    var dlg = new OpenFileDialog();
    dlg.Filter = "Audio Files|*.wav;*.mp3|All Files|*.*";
    if (dlg.ShowDialog() == true)
    {
        Settings.AudioPath = dlg.FileName;
        AudioPathTextBox.Text = Settings.AudioPath;
        Log("Selected audio: " + Settings.AudioPath);
    }
}

private void BrowseSrt_Click(object sender, RoutedEventArgs e)
{
    var dlg = new OpenFileDialog();
    dlg.Filter = "Subtitle Files|*.srt|All Files|*.*";
    if (dlg.ShowDialog() == true)
    {
        Settings.SrtPath = dlg.FileName;
        SrtPathTextBox.Text = Settings.SrtPath;
        Log("Selected SRT: " + Settings.SrtPath);
    }
}

private void BrowseClipsFolder_Click(object sender, RoutedEventArgs e)
{
    using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
    {
        var result = dlg.ShowDialog();
        if (result == System.Windows.Forms.DialogResult.OK)
        {
            Settings.ClipsFolder = dlg.SelectedPath;
            ClipsFolderTextBox.Text = Settings.ClipsFolder;
            Log("Selected clips folder: " + Settings.ClipsFolder);
        }
    }
}
```

> You might need to add reference to `System.Windows.Forms`:
>
> * Right-click **References** → **Add Reference…** → search **System.Windows.Forms** → check it → OK.

Also add a helper `Log`:

```csharp
private void Log(string message)
{
    LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\r\n");
    LogTextBox.ScrollToEnd();
}
```

---

## 7. Parse the SRT into Segments

Add two methods inside `MainWindow`:

```csharp
private void LoadSrt_Click(object sender, RoutedEventArgs e)
{
    if (string.IsNullOrEmpty(Settings.SrtPath) || !File.Exists(Settings.SrtPath))
    {
        MessageBox.Show("Please select an SRT file first.");
        return;
    }

    Segments.Clear();
    var segments = ParseSrt(Settings.SrtPath);
    int i = 1;
    foreach (var seg in segments)
    {
        seg.Index = i++;

        // Auto-assign default clip: ClipsFolder\{Index}.mp4 (if exists)
        if (!string.IsNullOrEmpty(Settings.ClipsFolder))
        {
            var candidate = Path.Combine(Settings.ClipsFolder, seg.Index + ".mp4");
            if (File.Exists(candidate))
                seg.AssignedClipPath = candidate;
        }

        Segments.Add(seg);
    }

    Log($"Loaded {Segments.Count} segments from SRT.");
}

private List<Segment> ParseSrt(string path)
{
    var result = new List<Segment>();
    var lines = File.ReadAllLines(path);
    int i = 0;

    while (i < lines.Length)
    {
        // skip blank
        while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i]))
            i++;

        if (i >= lines.Length) break;

        // index line
        if (!int.TryParse(lines[i].Trim(), out var dummyIndex))
        {
            i++;
            continue;
        }
        i++;

        if (i >= lines.Length) break;

        // time line
        var timeLine = lines[i].Trim();
        i++;
        var parts = timeLine.Split(new string[] { " --> " }, StringSplitOptions.None);
        if (parts.Length != 2)
            continue;

        double start = ParseSrtTime(parts[0]);
        double end = ParseSrtTime(parts[1]);

        // text lines (ignored for now)
        while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]))
        {
            i++;
        }

        result.Add(new Segment
        {
            Start = start,
            End = end
        });
    }

    return result;
}

private double ParseSrtTime(string s)
{
    // Format: 00:00:11,520
    var parts = s.Split(new[] { ':', ',' });
    int hh = int.Parse(parts[0]);
    int mm = int.Parse(parts[1]);
    int ss = int.Parse(parts[2]);
    int ms = int.Parse(parts[3]);
    return hh * 3600 + mm * 60 + ss + ms / 1000.0;
}
```

Now click **Load SRT** (in the app) after selecting files → the grid should fill with segment timings.

---

## 8. Hook Up Output Settings

Add a helper to read the selected aspect and fps:

```csharp
private bool ReadSettingsFromUi()
{
    if (string.IsNullOrEmpty(Settings.AudioPath) || !File.Exists(Settings.AudioPath))
    {
        MessageBox.Show("Audio file is missing.");
        return false;
    }
    if (string.IsNullOrEmpty(Settings.SrtPath) || !File.Exists(Settings.SrtPath))
    {
        MessageBox.Show("SRT file is missing.");
        return false;
    }
    if (string.IsNullOrEmpty(Settings.ClipsFolder) || !Directory.Exists(Settings.ClipsFolder))
    {
        MessageBox.Show("Clips folder is missing.");
        return false;
    }
    if (Segments.Count == 0)
    {
        MessageBox.Show("No segments loaded. Click 'Load SRT' first.");
        return false;
    }

    // Aspect
    var selectedAspect = AspectComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem;
    var tag = (string)selectedAspect.Tag; // e.g. "1080x1920"
    var wh = tag.Split('x');
    Settings.OutputWidth = int.Parse(wh[0]);
    Settings.OutputHeight = int.Parse(wh[1]);

    // FPS
    var fpsItem = FpsComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem;
    Settings.Fps = int.Parse((string)fpsItem.Content);

    return true;
}
```

---

## 9. FFmpeg Integration Helpers

Add two methods to run `ffprobe` and `ffmpeg`.

First, get the path to the ffmpeg folder (relative to the EXE):

```csharp
private string GetAppDirectory()
{
    return AppDomain.CurrentDomain.BaseDirectory;
}

private string GetFfmpegPath()
{
    return Path.Combine(GetAppDirectory(), "ffmpeg", "ffmpeg.exe");
}

private string GetFfprobePath()
{
    return Path.Combine(GetAppDirectory(), "ffmpeg", "ffprobe.exe");
}
```

Now add:

```csharp
private double GetClipDuration(string clipPath)
{
    var psi = new ProcessStartInfo
    {
        FileName = GetFfprobePath(),
        Arguments = $"-v error -show_entries format=duration -of csv=p=0 \"{clipPath}\"",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    using var proc = Process.Start(psi);
    string output = proc.StandardOutput.ReadToEnd();
    string error = proc.StandardError.ReadToEnd();
    proc.WaitForExit();

    if (!string.IsNullOrWhiteSpace(error))
        Log("ffprobe error: " + error);

    if (double.TryParse(output, NumberStyles.Any, CultureInfo.InvariantCulture, out var dur))
        return dur;

    throw new Exception("Could not get duration for " + clipPath);
}

private int RunFfmpeg(string arguments)
{
    var psi = new ProcessStartInfo
    {
        FileName = GetFfmpegPath(),
        Arguments = arguments,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    using var proc = Process.Start(psi);
    string stdOut = proc.StandardOutput.ReadToEnd();
    string stdErr = proc.StandardError.ReadToEnd();
    proc.WaitForExit();

    if (!string.IsNullOrEmpty(stdOut))
        Log("ffmpeg: " + stdOut);
    if (!string.IsNullOrEmpty(stdErr))
        Log("ffmpeg: " + stdErr);

    return proc.ExitCode;
}
```

---

## 10. Validate Button (Check for Missing Clips)

Add:

```csharp
private void Validate_Click(object sender, RoutedEventArgs e)
{
    if (!ReadSettingsFromUi()) return;

    bool ok = true;

    foreach (var seg in Segments)
    {
        if (string.IsNullOrEmpty(seg.AssignedClipPath) || !File.Exists(seg.AssignedClipPath))
        {
            Log($"Segment {seg.Index}: Missing clip.");
            ok = false;
        }
    }

    if (ok)
        MessageBox.Show("Validation OK. All segments have clips.");
    else
        MessageBox.Show("Validation failed. See log for details.");
}
```

This matches your requirement: don’t render if clips are missing.

---

## 11. Build Video Filter String (Scale & Crop to Aspect)

We’ll implement a simple scale+crop to match your chosen aspect.

Add:

```csharp
private string BuildVideoFilter(ProjectSettings s)
{
    // target aspect ratio = width / height
    double targetAspect = (double)s.OutputWidth / s.OutputHeight;
    string targetAspectStr = targetAspect.ToString(CultureInfo.InvariantCulture);

    // Explanation: if input aspect a > target, we fit height and crop width; else fit width and crop height.
    string filter =
        $"scale='if(gt(a,{targetAspectStr}),-2,{s.OutputWidth})':'if(gt(a,{targetAspectStr}),{s.OutputHeight},-2)'," +
        $"crop={s.OutputWidth}:{s.OutputHeight}";

    return filter;
}
```

For MVP, we **always** center-crop.

---

## 12. Render Button: Full Pipeline

We’ll:

1. Read settings.
2. Ensure `temp` and `output` folders exist.
3. For each segment:

   * Get slot duration.
   * Get clip duration (via ffprobe).
   * Decide whether to trim or slow/speed.
   * Call FFmpeg to create `temp\clip_{index}.mp4`.
4. Create `temp\concat_list.txt`.
5. Run FFmpeg concat + audio.

Add:

```csharp
private void Render_Click(object sender, RoutedEventArgs e)
{
    try
    {
        if (!ReadSettingsFromUi()) return;

        // Ensure no missing clips
        foreach (var seg in Segments)
        {
            if (string.IsNullOrEmpty(seg.AssignedClipPath) || !File.Exists(seg.AssignedClipPath))
            {
                MessageBox.Show($"Segment {seg.Index} has no valid clip. Fix before rendering.");
                return;
            }
        }

        string appDir = GetAppDirectory();
        string tempDir = Path.Combine(appDir, "temp");
        string outputDir = Path.Combine(appDir, "output");
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(outputDir);

        Log("Starting render...");

        string videoFilterBase = BuildVideoFilter(Settings);

        // Process each segment
        foreach (var seg in Segments)
        {
            double slot = seg.Duration;
            string clipPath = seg.AssignedClipPath;
            Log($"Segment {seg.Index}: slot {slot:F2}s, clip {clipPath}");

            double clipDur = GetClipDuration(clipPath);
            Log($"Segment {seg.Index}: clip duration {clipDur:F2}s");

            bool stretch = false;
            double speedFactor = 1.0;

            // Basic logic: if clip longer -> trim; if shorter -> slow down
            if (clipDur > slot)
            {
                stretch = false; // trim
            }
            else
            {
                stretch = true;
                speedFactor = clipDur / slot;
            }

            string filter;
            if (stretch)
            {
                // slow (or speed) and then crop
                filter = $"setpts={speedFactor.ToString(CultureInfo.InvariantCulture)}*PTS,{videoFilterBase}";
            }
            else
            {
                filter = videoFilterBase;
            }

            string outClip = Path.Combine(tempDir, $"clip_{seg.Index}.mp4");
            // -an = no audio from clip (we'll use narration only)
            string args;

            if (clipDur > slot)
            {
                args = $"-y -i \"{clipPath}\" -t {slot.ToString(CultureInfo.InvariantCulture)} -an -vf \"{filter}\" \"{outClip}\"";
            }
            else
            {
                args = $"-y -i \"{clipPath}\" -an -vf \"{filter}\" \"{outClip}\"";
            }

            Log($"ffmpeg args (segment {seg.Index}): {args}");
            int code = RunFfmpeg(args);
            if (code != 0)
            {
                MessageBox.Show($"FFmpeg failed on segment {seg.Index}. See log.");
                return;
            }
        }

        // Build concat list
        string concatPath = Path.Combine(tempDir, "concat_list.txt");
        using (var sw = new StreamWriter(concatPath))
        {
            foreach (var seg in Segments.OrderBy(sg => sg.Index))
            {
                string outClip = Path.Combine(tempDir, $"clip_{seg.Index}.mp4");
                sw.WriteLine($"file '{outClip.Replace("\\", "/")}'");
            }
        }

        string outputFile = Path.Combine(outputDir, "final_output.mp4");

        string concatArgs =
            $"-y -f concat -safe 0 -i \"{concatPath}\" -i \"{Settings.AudioPath}\" " +
            "-map 0:v -map 1:a -c:v libx264 -c:a aac -shortest " +
            $"\"{outputFile}\"";

        Log("Running final concat + audio...");
        int finalCode = RunFfmpeg(concatArgs);
        if (finalCode == 0)
        {
            Log("Render complete: " + outputFile);
            MessageBox.Show("Render complete!\n" + outputFile);
        }
        else
        {
            MessageBox.Show("Final ffmpeg concat failed. See log.");
        }
    }
    catch (Exception ex)
    {
        MessageBox.Show("Error during render: " + ex.Message);
        Log("Exception: " + ex);
    }
}
```

> This is a *basic* force-fit: trim if too long, slow/speed if shorter.
> Later, you can add UI controls to choose **loop**, **trim-in-middle**, etc.

---

## 13. Build and Test

1. In Visual Studio, click **Build → Build Solution**.

   * Fix any typos if errors appear (you can also paste code to me in another message if something confuses you).
2. Press **F5** (run).
3. In the running app:

   * Click **Browse…** for audio → pick your WAV/MP3.
   * Click **Browse…** for SRT → pick your transcript.
   * Click **Browse…** for clips folder → pick folder with `1.mp4`, `2.mp4`, etc.
   * Click **Load SRT** → table fills with segments.

     * You should see `Assigned Clip` auto-filled if the numbered clips exist.
   * Click **Validate** → it will confirm all segments have valid clips.
   * Click **Render** → FFmpeg runs; log shows progress.
4. After success:

   * Go to `...bin\Debug\net8.0-windows\output\final_output.mp4`
   * Play it.
     You should see video segments synced to narration.

---

## 14. Turn This Into a True Portable App

Once the MVP works in Debug:

1. In Visual Studio: **Build → Publish** (or **Right-click project → Publish**)
2. Choose **Folder** as the publish target.
3. Configure:

   * Target: **win-x64**
   * Deployment: **self-contained** (so users don’t need .NET installed)
4. Publish.

You’ll get a folder like:

`...\SherafyVideoMaker\bin\Release\net8.0-windows\publish\`

Copy into a new location your final portable bundle:

```
SherafyVideoMaker/
│
├── SherafyVideoMaker.exe
├── ffmpeg/
│     ├── ffmpeg.exe
│     └── ffprobe.exe
├── temp/
├── output/
└── logs/
```

Now you can **zip that folder** and move it anywhere (even `D:\My Apps\SherafyVideoMaker Builder`) and double-click `SherafyVideoMaker.exe` to run it.

---

## 15. Where to Go Next (After MVP Works)

Once this basic version is working, the next iterations (which we can design step-by-step later) are:

* **Per-segment options**: loop / speed / trim / sliding window.
* **Per-segment cropping controls** (center, top, bottom).
* **Burned-in subtitles** from SRT (font, color, position).
* **Background music track** with volume control.
* **Template/profile saving** for common presets.
* **Creative Commons video search integration** (Pexels/Pixabay/Unsplash).
* **Auto-update** (simple check against a version file on your site/GitHub).
* **Licensing**: trial vs pro, limits by duration or clip count.
