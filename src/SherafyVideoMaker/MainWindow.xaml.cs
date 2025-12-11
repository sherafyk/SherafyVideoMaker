using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using System.Windows.Forms;
using SherafyVideoMaker.Models;
using SherafyVideoMaker.Services;

namespace SherafyVideoMaker
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private readonly SrtParser _srtParser = new();
        private readonly FfmpegService _ffmpegService = new();
        private readonly LoggingService _logging;

        public ObservableCollection<Segment> Segments { get; } = new();
        public ProjectSettings Settings { get; } = new();
        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<string> AspectOptions { get; } = new(new[] { "16:9", "9:16", "1:1" });
        public ObservableCollection<int> FpsOptions { get; } = new(new[] { 24, 25, 30, 60 });

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

        private void BrowseAudio(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
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
            var dialog = new OpenFileDialog
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
            using var dialog = new FolderBrowserDialog();
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
                Log($"Loaded {Segments.Count} segments from SRT.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load SRT: " + ex.Message);
                Log("Error while loading SRT: " + ex);
            }
        }

        private void ValidateSegments(object sender, RoutedEventArgs e)
        {
            var validation = Settings.Validate();
            if (!validation.IsValid)
            {
                MessageBox.Show(validation.Message);
                Log(validation.Message);
                return;
            }

            foreach (var segment in Segments)
            {
                if (string.IsNullOrWhiteSpace(segment.AssignedClip))
                {
                    MessageBox.Show($"Segment {segment.Index} has no clip assigned.");
                    return;
                }

                var clipPath = Path.Combine(Settings.ClipsFolder, segment.AssignedClip);
                if (!File.Exists(clipPath))
                {
                    MessageBox.Show($"Clip not found: {clipPath}");
                    return;
                }
            }

            MessageBox.Show("Validation passed.");
            Log("Validation passed for audio, SRT, clips, and segment assignments.");
        }

        private void RenderVideo(object sender, RoutedEventArgs e)
        {
            var validation = Settings.Validate();
            if (!validation.IsValid)
            {
                MessageBox.Show(validation.Message);
                return;
            }

            if (Segments.Count == 0)
            {
                MessageBox.Show("Load SRT before rendering.");
                return;
            }

            try
            {
                Directory.CreateDirectory(Settings.TempFolder);
                Directory.CreateDirectory(Settings.OutputFolder);
                Directory.CreateDirectory(Settings.FfmpegFolder);
                _logging.EnsureLogFolder();

                foreach (var segment in Segments.OrderBy(s => s.Index))
                {
                    _ffmpegService.ProcessSegment(Settings, segment, Log);
                }

                var outputFile = _ffmpegService.ConcatAndMux(Settings, Segments.OrderBy(s => s.Index).ToList(), Log);
                if (outputFile is not null)
                {
                    MessageBox.Show("Render complete!\n" + outputFile);
                }
                else
                {
                    MessageBox.Show("Render failed. See log for details.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error during render: " + ex.Message);
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

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
