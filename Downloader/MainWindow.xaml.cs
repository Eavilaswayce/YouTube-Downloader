using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Converter;

namespace Downloader
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public string link;
        public string downloadPath;
        public string downloadFormat;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void SelectDirectory(object sender, RoutedEventArgs e)
        {
            System.Windows.Forms.FolderBrowserDialog fbd = new System.Windows.Forms.FolderBrowserDialog();

            if (fbd.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            {
                return;
            }

            downloadPath = fbd.SelectedPath;
            directoryBox.Text = downloadPath;

            Properties.Settings.Default.savedDirectory = downloadPath;
            Properties.Settings.Default.Save();
        }

        private async void VideoLinkKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            { 
                if (startDownload.IsEnabled) { await BeginDownload(); }
                else { return; }
            }
        }

        private async void DownloadButtonClick(object sender, RoutedEventArgs e)
        {
            await BeginDownload();
        }

        public async Task BeginDownload()
        {
            link = videoLink.Text;

            if (string.IsNullOrEmpty(downloadPath) || !(bool)saveMP3.IsChecked && !(bool)saveMP4.IsChecked)
            {
                MessageBox.Show("Please select a directory and a file type to download.", "Downloader", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (string.IsNullOrEmpty(videoLink.Text))
            {
                MessageBox.Show("Please input a YouTube link.", "Downloader", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            downloadInfo.Height = new GridLength(76);
            this.Height = 280;

            directorySelect.IsEnabled = false;
            startDownload.IsEnabled = false;
            saveMP3.IsEnabled = false;
            saveMP4.IsEnabled = false;
            status.Text = "Downloading... 0%";

            if ((bool)saveMP3.IsChecked) { downloadFormat = "mp3"; }
            if ((bool)saveMP4.IsChecked) { downloadFormat = "mp4"; }

            if (link.Contains("list"))
            {
                try
                {
                    await DownloadPlaylist();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"An error occurred: \"{ex.Message}\".", "Downloader", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                try
                {
                    await DownloadSingle();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"An error occurred: \"{ex.Message}\".", "Downloader", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            downloadInfo.Height = new GridLength(0);
            this.Height = 190;

            directorySelect.IsEnabled = true;
            startDownload.IsEnabled = true;
            saveMP3.IsEnabled = true;
            saveMP4.IsEnabled = true;
            status.Text = "";
            videoLink.Text = "";
            videoLink.Focus();
            directoryBox.Focus();
            downloadProgress.Value = 0;
        }

        public async Task DownloadSingle()
        {
            var handler = new HttpClientHandler();
            var httpClient = new HttpClient(handler, true);
            handler.UseCookies = false;

            var youtube = new YoutubeClient(httpClient);
            var streamMetaData = await youtube.Videos.GetAsync(link);
            var title = streamMetaData.Title.Replace("\"", "'").Replace("\\", "").Replace("/", "").Replace(":", "").Replace("*", "").Replace("?", "").Replace("<", "").Replace(">", "").Replace("|", "");

            var progress = new Progress<double>(percent =>
            {
                downloadProgress.Value = Convert.ToInt32(percent * 100.00f);
                status.Text = "Downloading... " + Convert.ToInt32(percent * 100.00f) + "%";
            });

            try
            {
                await youtube.Videos.DownloadAsync(link, $"{downloadPath}\\{title}.{downloadFormat}", o => o.SetFormat(downloadFormat).SetPreset(ConversionPreset.UltraFast), progress);
            }
            catch (Exception ex)
            {
                new Thread(() => 
                {
                    MessageBox.Show($"Failed to download video: \"{title}\" due to an error.\n\nReason: \"{ex.Message}\".", "Downloader", MessageBoxButton.OK, MessageBoxImage.Warning);

                }).Start();

                return;
            }

            new Thread(() =>
            {
                MessageBox.Show($"Successfully downloaded video: \"{title}\".", "Downloader", MessageBoxButton.OK, MessageBoxImage.Information);

            }).Start();
        }

        public async Task DownloadPlaylist()
        {
            string finalList = "";
            List<string> failedVideosTitles = new List<string>();

            var handler = new HttpClientHandler();
            var httpClient = new HttpClient(handler, true);
            handler.UseCookies = false;

            var youtube = new YoutubeClient(httpClient);
            var playlistData = await youtube.Playlists.GetAsync(link);
            var playlistName = playlistData.Title.Replace("\"", "'").Replace("\\", "").Replace("/", "").Replace(":", "").Replace("*", "").Replace("?", "").Replace("<", "").Replace(">", "").Replace("|", "");
            var total = await youtube.Playlists.GetVideosAsync(link);
            var totalNumber = total.Count;

            int failedVideos = 0;
            int currentNumber = 0;

            await foreach (var video in youtube.Playlists.GetVideosAsync(playlistData.Id))
            {
                currentNumber++;
                var title = video.Title.Replace("\"", "'").Replace("\\", "").Replace("/", "").Replace(":", "").Replace("*", "").Replace("?", "").Replace("<", "").Replace(">", "").Replace("|", "");

                var progress = new Progress<double>(percent =>
                {
                    downloadProgress.Value = Convert.ToInt32(percent * 100.00f);
                    status.Text = "Downloading... " + currentNumber + "/" + totalNumber + " - " + Convert.ToInt32(percent * 100.00f) + "%";
                });

                try
                {
                    await youtube.Videos.DownloadAsync(video.Id, $"{downloadPath}\\{title}.{downloadFormat}", o => o.SetFormat(downloadFormat).SetPreset(ConversionPreset.UltraFast), progress);
                }
                catch (Exception ex)
                {
                    new Thread(() =>
                    {
                        failedVideos++;
                        failedVideosTitles.Add($"\"{title}\"");

                        MessageBox.Show($"Skipping download of video: \"{title}\" due to an error.\n\nReason: \"{ex.Message}\".", "Downloader", MessageBoxButton.OK, MessageBoxImage.Warning);

                    }).Start();
                }
            }

            if (failedVideos == 0)
            {
                new Thread(() =>
                {
                    MessageBox.Show($"Successfully downloaded playlist: \"{playlistName}\".", "Downloader", MessageBoxButton.OK, MessageBoxImage.Information);

                }).Start();
            }
            else
            {
                new Thread(() =>
                {
                    MessageBox.Show($"Downloaded playlist: \"{playlistName}\" but failed to download {failedVideos} of the videos.\n\nPress OK to see list of failed videos.", "Downloader", MessageBoxButton.OK, MessageBoxImage.Warning);

                    for (int i = 0; i < failedVideosTitles.Count; i++)
                    {
                        if (i == 0) { finalList = $"{finalList}{i + 1}. {failedVideosTitles[i]}."; }
                        else { finalList = $"{finalList}\n\n{i + 1}. {failedVideosTitles[i]}."; }
                    }

                    MessageBox.Show(finalList, "Downloader", MessageBoxButton.OK, MessageBoxImage.Information);

                }).Start();
            }
        }

        private void MP3Checked(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.savedFileType = "MP3";
            Properties.Settings.Default.Save();
        }

        private void MP4Checked(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.savedFileType = "MP4";
            Properties.Settings.Default.Save();
        }

        private void DownloaderLoaded(object sender, RoutedEventArgs e)
        {
            downloadPath = Properties.Settings.Default.savedDirectory;

            downloadInfo.Height = new GridLength(0);
            this.Height = 190;

            if (Properties.Settings.Default.savedDirectory != "") { directoryBox.Text = Properties.Settings.Default.savedDirectory; }
            if (Properties.Settings.Default.savedFileType == "MP3") { saveMP3.IsChecked = true; }
            if (Properties.Settings.Default.savedFileType == "MP4") { saveMP4.IsChecked = true; }
        }

        private void TopBar(object sender, MouseButtonEventArgs e)
        {
            this.DragMove();
        }

        private void ExitButton(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void MinimiseButton(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void VideoLinkLostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(videoLink.Text)) { videoLinkHint.Visibility = Visibility.Visible; }
        }

        private void VideoLinkGotFocus(object sender, RoutedEventArgs e)
        {
            videoLinkHint.Visibility = Visibility.Hidden;
        }
    }
}
