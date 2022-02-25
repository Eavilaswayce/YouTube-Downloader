using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Converter;

namespace Downloader
{
    /// <summary>
    /// Interaction logic for Downloader.xaml
    /// </summary>
    public partial class DownloadWindow : Window
    {
        CancellationTokenSource cancellationTokenSource = new();
        CancellationToken cancellationToken;

        public DownloadWindow()
        {
            InitializeComponent();
        }

        private void SelectDirectory(object sender, RoutedEventArgs e)
        {
            System.Windows.Forms.FolderBrowserDialog fbd = new();

            if (fbd.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;

            directoryBox.Text = fbd.SelectedPath;

            Properties.Settings.Default.savedDirectory = fbd.SelectedPath;
            Properties.Settings.Default.Save();
        }

        private async void VideoLinkKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            { 
                if (startDownload.IsEnabled)
                    await BeginDownload();
            }
        }

        private async void DownloadButtonClick(object sender, RoutedEventArgs e)
        {
            await BeginDownload();
        }

        public async Task BeginDownload()
        {
            if (string.IsNullOrEmpty(videoLink.Text))
            {
                MessageBox.Show("Please input a YouTube link.", "Downloader", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (string.IsNullOrEmpty(Properties.Settings.Default.savedDirectory) || saveMP3.IsChecked == false && saveMP4.IsChecked == false)
            {
                MessageBox.Show("Please select a directory and a file type to download.", "Downloader", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string link = videoLink.Text;
            string ?downloadFormat = null;
            string savedDirectory = Properties.Settings.Default.savedDirectory;

            if (saveMP3.IsChecked == true)
                downloadFormat = "mp3";
            else
                downloadFormat = "mp4";

            ChangeButtonStates(false);

            cancellationToken = cancellationTokenSource.Token;

            try
            {
                // If the given URL contains "&list" this means it is part of a playlist
                if (!link.Contains("&list"))
                {
                    await DownloadSingle(link, downloadFormat, savedDirectory);
                }
                else
                {
                    await DownloadPlaylist(link, downloadFormat, savedDirectory);
                }
            }
            catch (Exception ex)
            {
                new Thread(() =>
                {
                    MessageBox.Show($"An error occurred: \"{ex.Message}\".", "Downloader", MessageBoxButton.OK, MessageBoxImage.Error);

                }).Start();
            }

            ChangeButtonStates(true);
        }

        public async Task DownloadSingle(string link, string format, string path)
        {
            // Needed for security
            var handler = new HttpClientHandler();
            var httpClient = new HttpClient(handler, true);
            handler.UseCookies = false;

            // Get video data
            var youtube = new YoutubeClient(httpClient);
            var streamData = await youtube.Videos.GetAsync(link);
            var title = ReplaceInvalidCharacters(streamData.Title);

            var progress = new Progress<double>(value =>
            {
                // To split the progress bar into two halves, fill one half and then the next,
                // maximum of both progress bars is 50
                if (downloadProgressOne.Value != 50)
                {
                    downloadProgressOne.Value = value * 100.00f;
                }
                else
                {
                    downloadProgressTwo.Value = (value * 100.00f) - 50;
                }

                // Taskbar icon progress bar
                taskbarIcon.ProgressValue = value;

                downloadStatus.Text = $"Downloading... {Convert.ToInt32(value * 100.00f)}%";
            });

            try
            {
                // Download content
                await youtube.Videos.DownloadAsync(link, $"{path}\\{title}.{format}", o => o.SetFormat(format).SetPreset(ConversionPreset.UltraFast), progress, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                new Thread(() =>
                {
                    MessageBox.Show($"Successfully cancelled the download of: \"{title}\".", "Downloader", MessageBoxButton.OK, MessageBoxImage.Information);

                }).Start();

                File.Delete($"{path}\\{title}.{format}");
                return;
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

        public async Task DownloadPlaylist(string link, string format, string path)
        {
            // Create a string list incase any videos fail to download
            List<string> failedVideosTitles = new();
            string finalList = "";
            int failedVideosAmount = 0;

            // Needed for security
            var handler = new HttpClientHandler();
            var httpClient = new HttpClient(handler, true);
            handler.UseCookies = false;

            // Get playlist data
            var youtube = new YoutubeClient(httpClient);
            var playlistData = await youtube.Playlists.GetAsync(link);
            var playlistName = ReplaceInvalidCharacters(playlistData.Title);
            var total = await youtube.Playlists.GetVideosAsync(link);
            var totalNumber = total.Count;

            int currentNumber = 0;

            // Foreach video in the playlist try to download them as the desired format
            await foreach (var video in youtube.Playlists.GetVideosAsync(playlistData.Id))
            {
                currentNumber++;
                var title = ReplaceInvalidCharacters(video.Title);

                // Skip download of video if it already exists
                if (File.Exists($"{path}\\{title}.{format}"))
                {
                    downloadStatus.Text = $"Skipping {currentNumber}/{totalNumber}...";
                    await Task.Delay(100);
                    continue;
                }

                var progress = new Progress<double>(value =>
                {
                    // To split the progress bar into two halves, fill one half and then the next,
                    // maximum of both progress bars is 50
                    if (value < 0.5f || downloadProgressOne.Value < 50)
                    {
                        downloadProgressOne.Value = value * 100.00f;
                        downloadProgressTwo.Value = 0;
                    }
                    else
                        downloadProgressTwo.Value = (value * 100.00f) - 50;

                    // Taskbar icon progress bar
                    taskbarIcon.ProgressValue = value;

                    downloadStatus.Text = $"Downloading... {currentNumber}/{totalNumber} - {Convert.ToInt32(value * 100.00f)}%";
                });

                try
                {
                    // Download content
                    await youtube.Videos.DownloadAsync(video.Id, $"{path}\\{title}.{format}", o => o.SetFormat(format).SetPreset(ConversionPreset.UltraFast), progress, cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    new Thread(() =>
                    {
                        MessageBox.Show($"Successfully cancelled the download of playlist: \"{playlistName}\".\n\nFiles have not been deleted.", "Downloader", MessageBoxButton.OK, MessageBoxImage.Information);

                    }).Start();

                    File.Delete($"{path}\\{title}.{format}");
                    return;
                }
                catch (Exception ex)
                {
                    new Thread(() =>
                    {
                        // Increase the failed videos amount by one and add the title to the list
                        failedVideosAmount++;
                        failedVideosTitles.Add($"\"{title}\"");

                        MessageBox.Show($"Skipping download of video: \"{title}\" due to an error.\n\nReason: \"{ex.Message}\".", "Downloader", MessageBoxButton.OK, MessageBoxImage.Warning);

                    }).Start();
                }
            }

            if (failedVideosAmount != 0)
            {
                new Thread(() =>
                {
                    // Show a messagebox telling the user it failed to download X amount of videos
                    MessageBox.Show($"Downloaded playlist: \"{playlistName}\" but failed to download {failedVideosAmount} of the videos.\n\nPress OK to see list of failed videos.", "Downloader", MessageBoxButton.OK, MessageBoxImage.Warning);

                    // Loop for the length of the string list, build a final string containing
                    // a list of titles of failed videos then display it in a messagebox for the user
                    for (int i = 0; i < failedVideosTitles.Count; i++)
                    {
                        if (i == 0) { finalList = $"{finalList}{i + 1}. {failedVideosTitles[i]}."; }
                        else { finalList = $"{finalList}\n\n{i + 1}. {failedVideosTitles[i]}."; }
                    }

                    MessageBox.Show(finalList, "Downloader", MessageBoxButton.OK, MessageBoxImage.Information);

                }).Start();
            }
            else
            {
                new Thread(() =>
                {
                    // The entire playlist was downloaded successfully
                    MessageBox.Show($"Successfully downloaded playlist: \"{playlistName}\".", "Downloader", MessageBoxButton.OK, MessageBoxImage.Information);

                }).Start();
            }
        }

        public static string ReplaceInvalidCharacters(string text)
        {
            return text.Replace("\"", "'").Replace("\\", "").Replace("/", "").Replace(":", "").Replace("*", "").Replace("?", "").Replace("<", "").Replace(">", "").Replace("|", "");
        }

        public void ChangeButtonStates(bool state)
        {
            // Toggle the usability of the controls
            if (!state)
            {
                taskbarIcon.ProgressState = System.Windows.Shell.TaskbarItemProgressState.Normal;
                MinHeight = 280;
                downloadInfoRow.Height = new GridLength(1, GridUnitType.Star);
                downloadInfoGrid.Visibility = Visibility.Visible;
                cancelButtonGrid.Visibility = Visibility.Visible;
                directorySelect.IsEnabled = false;
                startDownload.IsEnabled = false;
                saveMP3.IsEnabled = false;
                saveMP4.IsEnabled = false;
                downloadStatus.Text = "Downloading... 0%";
            }
            else
            {
                taskbarIcon.ProgressState = System.Windows.Shell.TaskbarItemProgressState.None;
                MinHeight = 200;
                Height = 200;
                downloadInfoRow.Height = GridLength.Auto;
                downloadInfoGrid.Visibility = Visibility.Collapsed;
                cancelButtonGrid.Visibility = Visibility.Collapsed;
                directorySelect.IsEnabled = true;
                startDownload.IsEnabled = true;
                saveMP3.IsEnabled = true;
                saveMP4.IsEnabled = true;
                downloadStatus.Text = "";
                videoLink.Text = "";
                videoLink.Focus();
                directoryBox.Focus();
                downloadProgressOne.Value = 0;
                downloadProgressTwo.Value = 0;
            }
        }

        private void DownloaderLoaded(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(Properties.Settings.Default.savedDirectory))
                directoryBox.Text = Properties.Settings.Default.savedDirectory;

            if (Properties.Settings.Default.savedFileType == "MP3")
                saveMP3.IsChecked = true;

            if (Properties.Settings.Default.savedFileType == "MP4")
                saveMP4.IsChecked = true;
        }

        private void LinkHint(object sender, RoutedEventArgs e)
        {
            TextBox txtbx = (TextBox)sender;

            if (!txtbx.IsFocused)
            {
                if (string.IsNullOrEmpty(txtbx.Text))
                    videoLinkHint.Visibility = Visibility.Visible;
            }
            else
            {
                videoLinkHint.Visibility = Visibility.Hidden;
            }
        }

        private void CancelButtonClick(object sender, RoutedEventArgs e)
        {
            cancellationTokenSource.Cancel();
            cancellationTokenSource = new CancellationTokenSource();

        }

        private void TopBar(object sender, MouseButtonEventArgs e)
        {
            this.DragMove();
        }

        private void TopBarMouseMove(object sender, MouseEventArgs e)
        {
            // If the user tries to drag and move the window while in maximised mode, return the window state to normal first
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                if (WindowState == WindowState.Maximized)
                {
                    WindowState = WindowState.Normal;

                    System.Drawing.Point mousePosition = System.Windows.Forms.Control.MousePosition;
                    Left = mousePosition.X - (Width / 2);
                    Top = mousePosition.Y - (topBar.Height.Value / 2);
                }

                DragMove();
            }
        }

        private void ControlBarButton(object sender, RoutedEventArgs e)
        {
            Button btn = (Button)sender;

            // Perform action depending on the button name from sender
            switch (btn.Name)
            {
                case "minimiseButton":
                    this.WindowState = WindowState.Minimized;
                    break;
                case "restoreButton":
                    this.WindowState = WindowState.Normal;
                    break;
                case "maximiseButton":
                    this.WindowState = WindowState.Maximized;
                    break;
                case "exitButton":
                    this.Close();
                    break;
            }
        }

        private void DownloaderStateChanged(object sender, EventArgs e)
        {
            // If the window is maximised change the margin on the main grid, otherwise it's too close to the edges
            if (WindowState == WindowState.Maximized)
            {
                mainGrid.Margin = new Thickness(8);

                // Hide the maximise button image and show the restore window button image
                maximiseButton.Visibility = Visibility.Collapsed;
                restoreButton.Visibility = Visibility.Visible;
            }
            else
            {
                mainGrid.Margin = new Thickness(0);

                // Show the maximise button image and hide the restore window button image
                maximiseButton.Visibility = Visibility.Visible;
                restoreButton.Visibility = Visibility.Collapsed;
            }
        }

        private void DownloaderActivated(object sender, EventArgs e)
        {
            // Needed for a borderless window with custom chrome window style otherwise there are black bars
            SizeToContent = SizeToContent.Manual;

            Height = 200;
        }

        private void DownloaderClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;

            if (saveMP3.IsChecked == true)
                Properties.Settings.Default.savedFileType = "MP3";
            else
                Properties.Settings.Default.savedFileType = "MP4";

            Properties.Settings.Default.Save();

            e.Cancel = false;
        }
    }
}
