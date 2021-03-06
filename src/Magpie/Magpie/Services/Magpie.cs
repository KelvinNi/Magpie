﻿using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using MagpieUpdater.Interfaces;
using MagpieUpdater.Models;
using MagpieUpdater.ViewModels;
using MagpieUpdater.Views;

namespace MagpieUpdater.Services
{
    public class Magpie : ISoftwareUpdater
    {
        public AppInfo AppInfo { get; private set; }
        private readonly IDebuggingInfoLogger _logger;
        private readonly IAnalyticsLogger _analyticsLogger;
        internal UpdateDecider UpdateDecider { get; set; }
        internal BestChannelFinder BestChannelFinder { get; set; }
        internal IRemoteContentDownloader RemoteContentDownloader { get; set; }
        public event EventHandler<SingleEventArgs<RemoteAppcast>> RemoteAppcastAvailableEvent;
        public event EventHandler<SingleEventArgs<string>> ArtifactDownloadedEvent;

        public Magpie(AppInfo appInfo, IDebuggingInfoLogger debuggingInfoLogger = null,
            IAnalyticsLogger analyticsLogger = null)
        {
            AppInfo = appInfo;
            _logger = debuggingInfoLogger ?? new DebuggingWindowViewModel();
            _analyticsLogger = analyticsLogger ?? new AnalyticsLogger();
            RemoteContentDownloader = new DefaultRemoteContentDownloader();
            UpdateDecider = new UpdateDecider(_logger);
            BestChannelFinder = new BestChannelFinder(_logger);
        }

        public async void CheckInBackground(string appcastUrl = null, bool showDebuggingWindow = false)
        {
            await Check(appcastUrl ?? AppInfo.AppCastUrl, AppInfo.SubscribedChannel, showDebuggingWindow)
                .ConfigureAwait(false);
        }

        public async void ForceCheckInBackground(string appcastUrl = null, bool showDebuggingWindow = false)
        {
            await Check(appcastUrl ?? AppInfo.AppCastUrl, AppInfo.SubscribedChannel, showDebuggingWindow, true)
                .ConfigureAwait(false);
        }

        public async void SwitchSubscribedChannel(int channelId, bool showDebuggingWindow = false)
        {
            AppInfo.SubscribedChannel = channelId;
            await Check(AppInfo.AppCastUrl, channelId, showDebuggingWindow, true).ConfigureAwait(false);
        }

        private async Task Check(string appcastUrl, int channelId = 1, bool showDebuggingWindow = false,
            bool forceCheck = false)
        {
            _logger.Log(string.Format("Starting fetching remote channel content from address: {0}", appcastUrl));
            try
            {
                var data = await RemoteContentDownloader.DownloadStringContent(appcastUrl, _logger).ConfigureAwait(true);
                if (string.IsNullOrWhiteSpace(data))
                {
                    if (forceCheck)
                    {
                        ShowErrorWindow();
                    }
                    return;
                }

                var appcast = ParseAppcast(data);
                OnRemoteAppcastAvailableEvent(new SingleEventArgs<RemoteAppcast>(appcast));
                var channelToUpdateFrom = BestChannelFinder.Find(channelId, appcast.Channels);
                if (UpdateDecider.ShouldUpdate(channelToUpdateFrom, forceCheck))
                {
                    _analyticsLogger.LogUpdateAvailable(channelToUpdateFrom);
                    await ShowUpdateWindow(channelToUpdateFrom);
                }
                else if (forceCheck)
                {
                    ShowNoUpdatesWindow();
                }
            }
            catch (Exception ex)
            {
                _logger.Log(string.Format("Error parsing remote channel: {0}", ex.Message));
            }
            finally
            {
                _logger.Log("Finished fetching remote channel content");
            }
        }

        protected virtual async Task ShowUpdateWindow(Channel channel)
        {
            var viewModel = new MainWindowViewModel(AppInfo, _logger, RemoteContentDownloader, _analyticsLogger);
            await viewModel.StartAsync(channel).ConfigureAwait(true);
            var window = new MainWindow {ViewModel = viewModel};
            viewModel.DownloadNowCommand = new DelegateCommand(async e =>
            {
                _analyticsLogger.LogDownloadNow();
                _logger.Log("Continuing with downloading the artifact");
                window.Close();
                await ShowDownloadWindow(channel);
            });
            SetOwner(window);
            window.ShowDialog();
        }

        protected virtual void ShowNoUpdatesWindow()
        {
            var window = new NoUpdatesWindow();
            SetOwner(window);
            window.ShowDialog();
        }

        protected virtual void ShowErrorWindow()
        {
            var window = new ErrorWindow();
            SetOwner(window);
            window.ShowDialog();
        }

        private static string CreateTempPath(string url)
        {
            var uri = new Uri(url);
            var path = Path.GetTempPath();
            var fileName = string.Format(Guid.NewGuid() + Path.GetFileName(uri.LocalPath));
            return Path.Combine(path, fileName);
        }

        protected virtual async Task ShowDownloadWindow(Channel channel)
        {
            var viewModel = new DownloadWindowViewModel(AppInfo, _logger, RemoteContentDownloader);
            var artifactPath = CreateTempPath(channel.ArtifactUrl);
            var window = new DownloadWindow {DataContext = viewModel};
            bool[] finishedDownloading = {false};
            viewModel.ContinueWithInstallationCommand = new DelegateCommand(e =>
            {
                _logger.Log("Continue after downloading artifact");
                _analyticsLogger.LogContinueWithInstallation();
                OnArtifactDownloadedEvent(new SingleEventArgs<string>(artifactPath));
                window.Close();
                if (ShouldOpenArtifact(channel, artifactPath))
                {
                    OpenArtifact(artifactPath);
                    _logger.Log("Opened artifact");
                }
            }, o => finishedDownloading[0]);

            SetOwner(window);
            window.Show();

            var savedAt = await viewModel.StartAsync(channel, artifactPath).ConfigureAwait(true);
            finishedDownloading[0] = true;
            ((DelegateCommand)viewModel.ContinueWithInstallationCommand).RaiseCanExecuteChanged();

            if (string.IsNullOrWhiteSpace(savedAt))
            {
                window.Close();
                ShowErrorWindow();
            }
        }

        private bool ShouldOpenArtifact(Channel channel, string artifactPath)
        {
            if (string.IsNullOrEmpty(channel.DSASignature))
            {
                _logger.Log("No DSASignature provided. Skipping signature verification");
                return true;
            }
            _logger.Log("DSASignature provided. Verifying artifact's signature");
            if (VerifyArtifact(channel, artifactPath))
            {
                _logger.Log("Successfully verified artifact's signature");
                return true;
            }
            _logger.Log("Couldn't verify artifact's signature. The artifact will now be deleted.");
            var signatureWindowViewModel = new SignatureVerificationWindowViewModel(AppInfo);
            var signatureWindow = new SignatureVerificationWindow {DataContext = signatureWindowViewModel};
            signatureWindowViewModel.ContinueCommand = new DelegateCommand(e => { signatureWindow.Close(); });
            SetOwner(signatureWindow);
            signatureWindow.ShowDialog();
            return false;
        }

        protected virtual bool VerifyArtifact(Channel channel, string artifactPath)
        {
            var verifer = new SignatureVerifier(AppInfo.PublicSignatureFilename);
            return verifer.VerifyDSASignature(channel.DSASignature, artifactPath);
        }

        protected virtual void OpenArtifact(string artifactPath)
        {
            Process.Start(artifactPath);
        }

        protected virtual void SetOwner(Window window)
        {
            if (Application.Current != null && !Application.Current.MainWindow.Equals(window))
            {
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                window.Owner = Application.Current.MainWindow;
            }
        }

        private RemoteAppcast ParseAppcast(string content)
        {
            _logger.Log("Started deserializing remote channel content");
            var appcast = RemoteAppcast.MakeFromJson(content);
            _logger.Log("Finished deserializing remote channel content");
            return appcast;
        }

        protected virtual void OnRemoteAppcastAvailableEvent(SingleEventArgs<RemoteAppcast> args)
        {
            var handler = RemoteAppcastAvailableEvent;
            if (handler != null) handler(this, args);
        }

        protected virtual void OnArtifactDownloadedEvent(SingleEventArgs<string> args)
        {
            var handler = ArtifactDownloadedEvent;
            if (handler != null) handler(this, args);
        }
    }
}