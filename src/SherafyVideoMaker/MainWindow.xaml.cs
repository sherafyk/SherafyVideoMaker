using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SherafyVideoMaker.Models;
using SherafyVideoMaker.Services;

namespace SherafyVideoMaker
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private readonly SrtParser _srtParser = new();
        private readonly FfmpegService _ffmpegService = new();
        private readonly DownloadService _downloadService = new();
        private readonly LoggingService _logging;

        public ObservableCollection<Segment> Segments { get; } = new();
        public ProjectSettings Settings { get; } = new();
        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<string> AspectOptions { get; } = new(new[] { "16:9", "9:16", "1:1" });
        public ObservableCollection<int> FpsOptions { get; } = new(new[] { 24, 25, 30, 60 });
        public Array FitModeOptions { get; } = Enum.GetValues(typeof(FitMode));

        private const double DurationToleranceSeconds = 2.5;

        private string _logText = string.Empty;
        public string LogText
        {
            get => _logText;
            set
            {
                _logText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LogText)));
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            Settings.AspectRatio = AspectOptions.First();
            Settings.Fps = FpsOptions.First(x => x == 30);
            _logging = new LoggingService(Settings);
        }

        private void SegmentsGrid_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private void SegmentsGrid_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                return;
            }

            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files is null || files.Length == 0)
            {
                return;
            }

            var dataGrid = (DataGrid)sender;
            var position = e.GetPosition(dataGrid);
            var hitElement = dataGrid.InputHitTest(position) as DependencyObject;
            var row = FindParent<DataGridRow>(hitElement);

            if (row?.Item is Segment segment)
            {
                var fileName = Path.GetFileName(files[0]);
                segment.AssignedClip = fileName;
                dataGrid.Items.Refresh();
                Log($"Assigned clip '{fileName}' to segment {segment.Index} via drag & drop.");
            }
        }

        private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child is not null)
            {
                if (child is T parent)
                {
                    return parent;
                }

                child = VisualTreeHelper.GetParent(child);
            }

            return null;
        }

        private void BrowseAudio(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Audio files|*.wav;*.mp3;*.m4a|All files|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                Settings.AudioPath = dialog.FileName;
                OnPropertyChanged(nameof(Settings));
            }
        }

        private void BrowseSrt(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "SubRip files|*.srt|All files|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                Settings.SrtPath = dialog.FileName;
                OnPropertyChanged(nameof(Settings));
            }
        }

        private void BrowseClipsFolder(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog();

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                Settings.ClipsFolder = dialog.SelectedPath;
                OnPropertyChanged(nameof(Settings));
            }
        }

        private void LoadSrt(object sender, RoutedEventArgs e)
        {
            try
            {
                Segments.Clear();
                foreach (var segment in _srtParser.Parse(Settings.SrtPath))
                {
                    Segments.Add(segment);
                }
                AutoAssignClips();
                CheckSrtAlignmentAgainstAudio();
                Log($"Loaded {Segments.Count} segments from SRT.");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Failed to load SRT: " + ex.Message);
                Log("Error while loading SRT: " + ex);
            }
        }

        private async void ValidateSegments(object sender, RoutedEventArgs e)
        {
            await ValidateSegmentsAsync();
        }

        private async Task ValidateSegmentsAsync()
        {
            if (!await EnsureDownloadIfNeeded())
            {
                return;
            }

            var validation = Settings.Validate(Segments.Any(s => !string.IsNullOrWhiteSpace(s.ClipUrl)));
            if (!validation.IsValid)
            {
                System.Windows.MessageBox.Show(validation.Message);
                Log(validation.Message);
                return;
            }

            if (!await EnsureSegmentDownloadsAsync())
            {
                return;
            }

            foreach (var segment in Segments)
            {
                if (string.IsNullOrWhiteSpace(segment.AssignedClip) && string.IsNullOrWhiteSpace(segment.ClipUrl))
                {
                    System.Windows.MessageBox.Show($"Segment {segment.Index} has no clip assigned.");
                    return;
                }

                var clipPath = ResolveClipPath(segment);
                if (clipPath is null)
                {
                    System.Windows.MessageBox.Show($"Clip not found for segment {segment.Index}. Assign a local file or a valid URL.");
                    return;
                }
            }

            System.Windows.MessageBox.Show("Validation passed.");
            Log("Validation passed for audio, SRT, clips, and segment assignments.");
        }

        private async void RenderVideo(object sender, RoutedEventArgs e)
        {
            await RenderVideoAsync();
        }

        private async Task RenderVideoAsync()
        {
            if (!await EnsureDownloadIfNeeded())
            {
                return;
            }

            var validation = Settings.Validate(Segments.Any(s => !string.IsNullOrWhiteSpace(s.ClipUrl)));
            if (!validation.IsValid)
            {
                System.Windows.MessageBox.Show(validation.Message);
                return;
            }

            if (Segments.Count == 0)
            {
                System.Windows.MessageBox.Show("Load SRT before rendering.");
                return;
            }

            try
            {
                if (!await EnsureSegmentDownloadsAsync())
                {
                    return;
                }

                Directory.CreateDirectory(Settings.TempFolder);
                Directory.CreateDirectory(Settings.OutputFolder);
                Directory.CreateDirectory(Settings.FfmpegFolder);
                _logging.EnsureLogFolder();

                CheckSrtAlignmentAgainstAudio();

                foreach (var segment in Segments.OrderBy(s => s.Index))
                {
                    var clipPath = ResolveClipPath(segment);
                    if (clipPath is null)
                    {
                        throw new FileNotFoundException($"Missing clip for segment {segment.Index}.");
                    }

                    _ffmpegService.ProcessSegment(Settings, segment, clipPath, Log);
                }

                var outputFile = _ffmpegService.ConcatAndMux(Settings, Segments.OrderBy(s => s.Index).ToList(), Log);
                if (outputFile is not null)
                {
                    System.Windows.MessageBox.Show("Render complete!\n" + outputFile);
                }
                else
                {
                    System.Windows.MessageBox.Show("Render failed. See log for details.");
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Error during render: " + ex.Message);
                Log("Exception: " + ex);
            }
        }

        private void AutoAssignClips()
        {
            foreach (var segment in Segments)
            {
                var numberedFile = Path.Combine(Settings.ClipsFolder ?? string.Empty, $"{segment.Index}.mp4");
                if (File.Exists(numberedFile))
                {
                    segment.AssignedClip = Path.GetFileName(numberedFile);
                }
            }
        }

        private void Log(string message)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            _logging.Write(line);

            var builder = new StringBuilder(LogText.Length + line.Length + 2);
            builder.Append(LogText);
            builder.AppendLine(line);
            LogText = builder.ToString();
        }

        private void CheckSrtAlignmentAgainstAudio()
        {
            if (Segments.Count == 0)
            {
                return;
            }

            var totalSegmentsSeconds = Segments.Sum(s => s.Duration.TotalSeconds);
            try
            {
                var audioDuration = _ffmpegService.GetAudioDuration(Settings, Log);
                var durationDelta = Math.Abs(audioDuration - totalSegmentsSeconds);
                if (durationDelta > DurationToleranceSeconds)
                {
                    var warning =
                        $"Warning: SRT total duration ({totalSegmentsSeconds:0.###}s) differs from the audio master duration ({audioDuration:0.###}s) by {durationDelta:0.###}s. The audio stays as the master timeline; please verify your transcript matches the narration.";
                    System.Windows.MessageBox.Show(
                        warning,
                        "Duration mismatch",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    Log(warning);
                }
                else
                {
                    Log($"SRT total duration is within {DurationToleranceSeconds:0.#}s of the audio duration ({audioDuration:0.###}s).");
                }
            }
            catch (Exception ex)
            {
                Log("Skipping duration alignment check: " + ex.Message);
            }
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private async void DownloadClip(object sender, RoutedEventArgs e)
        {
            await EnsureDownloadIfNeeded(showSuccessToast: true);
        }

        private async Task<bool> EnsureDownloadIfNeeded(bool showSuccessToast = false)
        {
            if (string.IsNullOrWhiteSpace(Settings.ClipUrl))
            {
                return true;
            }

            try
            {
                Log($"Starting download: {Settings.ClipUrl}");

                var lastProgress = 0d;
                var downloadedPath = await _downloadService.DownloadAsync(
                    Settings,
                    Settings.ClipUrl,
                    p =>
                    {
                        if (p - lastProgress >= 5 || p >= 100)
                        {
                            lastProgress = p;
                            Log($"Download progress: {p:0.#}%");
                        }
                    },
                    Log);

                Settings.ClipsFolder = Settings.DownloadsFolder;
                var fileName = Path.GetFileName(downloadedPath);
                var targetSegment = Segments.OrderBy(s => s.Index)
                    .FirstOrDefault(s => string.IsNullOrWhiteSpace(s.AssignedClip));
                if (targetSegment is not null)
                {
                    targetSegment.AssignedClip = fileName;
                    Log($"Assigned downloaded clip '{fileName}' to segment {targetSegment.Index}.");
                }
                else
                {
                    Log($"Downloaded clip '{fileName}' saved. No unassigned segments available.");
                }

                Settings.ClipUrl = string.Empty;
                OnPropertyChanged(nameof(Settings));

                if (showSuccessToast)
                {
                    System.Windows.MessageBox.Show(
                        $"Download complete!\n{downloadedPath}",
                        "Clip downloaded",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    "Download failed: " + ex.Message,
                    "Download error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Log("Download error: " + ex);
                return false;
            }
        }

        private async Task<bool> EnsureSegmentDownloadsAsync()
        {
            var segmentsNeedingDownload = Segments
                .Where(s => !string.IsNullOrWhiteSpace(s.ClipUrl))
                .Where(s => string.IsNullOrWhiteSpace(s.AssignedClip) || ResolveClipPath(s) is null)
                .OrderBy(s => s.Index)
                .ToList();

            foreach (var segment in segmentsNeedingDownload)
            {
                try
                {
                    Log($"Starting download for segment {segment.Index}: {segment.ClipUrl}");

                    var lastProgress = 0d;
                    var downloadedPath = await _downloadService.DownloadAsync(
                        Settings,
                        segment.ClipUrl!,
                        p =>
                        {
                            if (p - lastProgress >= 5 || p >= 100)
                            {
                                lastProgress = p;
                                Log($"Segment {segment.Index} download progress: {p:0.#}%");
                            }
                        },
                        Log);

                    segment.AssignedClip = Path.GetFileName(downloadedPath);
                    Settings.ClipsFolder = Settings.DownloadsFolder;
                    Log($"Assigned downloaded clip '{segment.AssignedClip}' to segment {segment.Index}.");
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show(
                        $"Download failed for segment {segment.Index}: {ex.Message}",
                        "Download error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    Log($"Download error for segment {segment.Index}: {ex}");
                    return false;
                }
            }

            return true;
        }

        private string? ResolveClipPath(Segment segment)
        {
            if (string.IsNullOrWhiteSpace(segment.AssignedClip))
            {
                return null;
            }

            var candidates = new[]
            {
                Path.IsPathRooted(segment.AssignedClip) ? segment.AssignedClip : null,
                string.IsNullOrWhiteSpace(Settings.ClipsFolder) ? null : Path.Combine(Settings.ClipsFolder, segment.AssignedClip),
                string.IsNullOrWhiteSpace(Settings.DownloadsFolder) ? null : Path.Combine(Settings.DownloadsFolder, segment.AssignedClip)
            };

            return candidates.FirstOrDefault(path => path is not null && File.Exists(path));
        }
    }
}
