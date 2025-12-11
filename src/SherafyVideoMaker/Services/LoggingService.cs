using System;
using System.IO;
using SherafyVideoMaker.Models;

namespace SherafyVideoMaker.Services
{
    public class LoggingService
    {
        private readonly ProjectSettings _settings;
        private readonly string _logFile;

        public LoggingService(ProjectSettings settings)
        {
            _settings = settings;
            _logFile = Path.Combine(settings.LogFolder, $"log_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        }

        public void EnsureLogFolder()
        {
            Directory.CreateDirectory(_settings.LogFolder);
        }

        public void Write(string message)
        {
            EnsureLogFolder();
            File.AppendAllText(_logFile, message + Environment.NewLine);
        }
    }
}
