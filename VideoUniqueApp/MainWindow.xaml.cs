using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Globalization;
using NLog;

namespace VideoUniqueApp
{
    public partial class MainWindow : Window
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private Process _ffmpeg = null;
        private const string FfmpegDownloadUrl = "http://shadowxmnd.beget.tech/uploads/ffmpeg.zip";
        private const string FfmpegExecutableName = "ffmpeg.exe";
        private const string FfmpegZipFileName = "ffmpeg.zip";

        public MainWindow()
        {
            InitializeComponent();
            InitializeFfmpeg();
        }

        private async void InitializeFfmpeg()
        {
            if (!File.Exists(FfmpegExecutableName))
            {
                mainPanel.Visibility = Visibility.Collapsed;
                borderDownload.Visibility = Visibility.Visible;
                await DownloadAndExtractFfmpegAsync();
                borderDownload.Visibility = Visibility.Collapsed;
                mainPanel.Visibility = Visibility.Visible;
            }
        }

        private async void BtnBrowseInput_Click(object sender, RoutedEventArgs e)
        {
            var fileDialog = new OpenFileDialog
            {
                Filter = "Video Files|*.mp4;*.avi;*.mkv|All Files|*.*",
                Title = "Выберите видеофайл"
            };

            if (fileDialog.ShowDialog() == true)
            {
                txtInputFile.Text = fileDialog.FileName;
            }
        }

        private async void BtnProcess_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(txtInputFile.Text))
                {
                    MessageBox.Show("Файл для уникализации не выбран.");
                    Logger.Warn("Файл для уникализации не выбран.");
                    return;
                }
                lblOriginalInfo.Content = "";
                lblProcessedInfo.Content = "";
                progressBar.Value = 0;
                lblStatus.Text = "Уникализация видео...";
                string inputFilePath = txtInputFile.Text;
                string outputFilePath = GetUniqueOutputFilePath(inputFilePath);

                if (File.Exists(outputFilePath))
                {
                    MessageBox.Show($"Файл с таким именем уже существует: {outputFilePath}");
                    Logger.Warn($"Файл с таким именем уже существует: {outputFilePath}");
                    return;
                }

                await StartFFmpegProcessing(inputFilePath, outputFilePath);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Ошибка при обработке видео.");
                MessageBox.Show("Произошла ошибка при обработке видео. Проверьте лог.");
            }
        }

        private string GetUniqueOutputFilePath(string inputFilePath)
        {
            string programDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string uniqueFilesDirectory = Path.Combine(programDirectory, "Unique");

            if (!Directory.Exists(uniqueFilesDirectory))
            {
                Directory.CreateDirectory(uniqueFilesDirectory);
            }

            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(inputFilePath);
            string fileExtension = Path.GetExtension(inputFilePath);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string uniqueFileName = $"unique_{fileNameWithoutExtension}_{timestamp}{fileExtension}";

            return Path.Combine(uniqueFilesDirectory, uniqueFileName);
        }

        private async Task DownloadAndExtractFfmpegAsync()
        {
            try
            {
                lblDownloadStatus.Text = "FFmpeg не найден. Скачиваем...";

                using (var httpClient = new HttpClient())
                {
                    httpClient.MaxResponseContentBufferSize = 256000;

                    var response = await httpClient.GetAsync(FfmpegDownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();

                    long totalBytes = response.Content.Headers.ContentLength.GetValueOrDefault(0);
                    long bytesDownloaded = 0;

                    using (var fileStream = new FileStream(FfmpegZipFileName, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        var responseStream = await response.Content.ReadAsStreamAsync();
                        byte[] buffer = new byte[8192];
                        int bytesRead;

                        while ((bytesRead = await responseStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            bytesDownloaded += bytesRead;
                            UpdateDownloadProgress(bytesDownloaded, totalBytes);
                        }
                    }

                    lblDownloadStatus.Text = "Распаковка FFmpeg...";
                    ZipFile.ExtractToDirectory(FfmpegZipFileName, ".");
                    Logger.Info("FFmpeg успешно загружен и распакован.");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Ошибка при загрузке или распаковке FFmpeg.");
                MessageBox.Show("Не удалось загрузить или распаковать FFmpeg. Проверьте лог.");
            }
        }


        private void UpdateDownloadProgress(long downloadedBytes, long totalBytes)
        {
            Dispatcher.Invoke(() =>
            {
                double progress = (double)downloadedBytes / totalBytes * 100;
                progressBarDownload.Value = progress;
                lblDownloadProgress.Text = $"{progress:0}% ({FormatBytes(downloadedBytes)} / {FormatBytes(totalBytes)})";
            });
        }

        private string FormatBytes(long byteCount)
        {
            if (byteCount >= 1048576)
                return $"{byteCount / 1048576} MB";
            if (byteCount >= 1024)
                return $"{byteCount / 1024} KB";
            return $"{byteCount} bytes";
        }
        private string GetFileSize(string filePath)
        {
            var fileInfo = new FileInfo(filePath);
            return fileInfo.Exists ? $"{fileInfo.Length / 1024} KB" : "File not found";
        }
        private async Task<string> GetVideoInfo(string inputFilePath)
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = FfmpegExecutableName,
                Arguments = $"-i \"{inputFilePath}\"",  // Получение информации о видео
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true



            };

            using (var process = new Process { StartInfo = processStartInfo })
            {
                process.Start();

                string output = await process.StandardError.ReadToEndAsync();
                process.WaitForExit();

       
                var durationMatch = Regex.Match(output, @"Duration:\s(?<time>\d{2}:\d{2}:\d{2}\.\d{2})");
                string duration = durationMatch.Success ? durationMatch.Groups["time"].Value : "Unknown";
              

                return $"Duration: {duration}";
            }
        }

        private async Task StartFFmpegProcessing(string inputFilePath, string outputFilePath)
        {
            try
            {
                if (!File.Exists(FfmpegExecutableName))
                {
                    MessageBox.Show("FFmpeg не найден. Пожалуйста, убедитесь, что он установлен.");
                    Logger.Error("FFmpeg не найден.");
                    return;
                }

                await SetTotalDuration(inputFilePath);

                if (_totalDuration == TimeSpan.Zero)
                {
                    MessageBox.Show("Не удалось определить длительность видео. Проверьте файл.");
                    Logger.Warn("Не удалось определить длительность видео.");
                    return;
                }

                progressBar.Value = 0;
                lblStatus.Text = "Начало обработки видео...";

                string originalInfo = await GetVideoInfo(inputFilePath);
                string originalSize = GetFileSize(inputFilePath); // Размер до обработки
                Dispatcher.Invoke(() =>
                {
                    lblOriginalInfo.Content = $"До: {originalInfo}, Размер: {originalSize}";
                });

                string arguments = $"-i \"{inputFilePath}\" ";

                if (chkRemoveMetadata.IsChecked == true)
                {
                    arguments += "-map_metadata -1 "; // Удаление метаданных
                }

                if (chkAddNoise.IsChecked == true)
                {
                    arguments += $"-vf \"noise=alls=1:allf=t\" "; // Добавление шума 
                }

                if (chkCompressVideo.IsChecked == true)
                {
                    arguments += "-crf 28 "; // Сжатие видео
                }

                arguments += $"\"{outputFilePath}\"";

                var processStartInfo = new ProcessStartInfo
                {
                    FileName = FfmpegExecutableName,
                    Arguments = arguments,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                _ffmpeg = new Process { StartInfo = processStartInfo };

                _ffmpeg.OutputDataReceived += Ffmpeg_OutputDataReceived;
                _ffmpeg.ErrorDataReceived += Ffmpeg_ErrorDataReceived;

                _ffmpeg.Start();
                _ffmpeg.BeginOutputReadLine();
                _ffmpeg.BeginErrorReadLine();

                await Task.Run(() => _ffmpeg.WaitForExit());
                if (_ffmpeg.ExitCode == 0)
                {
                    progressBar.Value = 100;
                    lblStatus.Text = "Обработка завершена!";

                    // Получаем информацию о видео после обработки
                    string processedInfo = await GetVideoInfo(outputFilePath);
                    string processedSize = GetFileSize(outputFilePath); // Размер после обработки
                    Dispatcher.Invoke(() =>
                    {
                        lblProcessedInfo.Content = $"После: {processedInfo}, Размер: {processedSize}";
                    });
                    MessageBox.Show("Видео успешно обработано.");
                    Logger.Info("Обработка видео завершена успешно.");
                }
                else
                {
                    lblStatus.Text = "Ошибка обработки. Проверьте входные данные.";
                    MessageBox.Show("Ошибка обработки видео. Проверьте лог FFmpeg.");
                    Logger.Error("Ошибка обработки видео. Код выхода: " + _ffmpeg.ExitCode);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Ошибка при обработке видео.");
                MessageBox.Show("Произошла ошибка при обработке видео. Проверьте лог.");
            }
        }



        private void Ffmpeg_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                if (e.Data.StartsWith("frame="))
                {
                    Dispatcher.Invoke(() =>
                    {
                        lblStatus.Text = $"Обработка: {e.Data}";
                    });
                }
            }
        }

        void Ffmpeg_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null && e.Data.StartsWith("frame="))
            {
                var timeIndex = e.Data.IndexOf("time=");
                if (timeIndex != -1)
                {
                    string timeInfo = e.Data.Substring(timeIndex + 5).Split(' ')[0];

                    if (timeInfo == "N/A")
                    {
                        return;
                    }

                    var timeSegments = timeInfo.Split(':');
                    double currentTimeInSeconds = 0;

                    if (timeSegments.Length == 3)
                    {
                        double hours = Convert.ToDouble(timeSegments[0], CultureInfo.InvariantCulture);
                        double minutes = Convert.ToDouble(timeSegments[1], CultureInfo.InvariantCulture);
                        double seconds = Convert.ToDouble(timeSegments[2], CultureInfo.InvariantCulture);

                        currentTimeInSeconds = hours * 3600 + minutes * 60 + seconds;
                    }
                    else if (timeSegments.Length == 1)
                    {
                        currentTimeInSeconds = Convert.ToDouble(timeSegments[0], CultureInfo.InvariantCulture);
                    }

                    double progress = (_totalDuration.TotalSeconds > 0) ? (currentTimeInSeconds / _totalDuration.TotalSeconds) * 100 : 0;

                    Dispatcher.Invoke(() =>
                    {
                        progressBar.Value = Math.Min(progress, 100);
                        lblStatus.Text = $"Обработка: {Math.Min(progress, 100):0.00}%";
                    });
                }
            }
        }

       
        private TimeSpan _totalDuration = TimeSpan.Zero;

        private async Task SetTotalDuration(string inputFilePath)
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = FfmpegExecutableName,
                Arguments = $"-i \"{inputFilePath}\"",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = new Process { StartInfo = processStartInfo })
            {
                process.Start();

                string output = await process.StandardError.ReadToEndAsync();
                process.WaitForExit();

                var match = Regex.Match(output, @"Duration:\s(?<time>\d{2}:\d{2}:\d{2}\.\d{2})");
                if (match.Success)
                {
                    _totalDuration = TimeSpan.Parse(match.Groups["time"].Value);
                }
                else
                {
                    _totalDuration = TimeSpan.Zero;
                }
            }
        }

        private void txtInputFile_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {

        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("tg://resolve?domain=shadowxpage") { UseShellExecute = true });
        }
    }
}
