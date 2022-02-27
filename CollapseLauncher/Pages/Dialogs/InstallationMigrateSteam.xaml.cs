﻿using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage;
using Windows.Storage.Pickers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Foundation.Collections;

using Microsoft.Win32;

using CollapseLauncher.Dialogs;
using static CollapseLauncher.Dialogs.SimpleDialogs;

using Hi3Helper.Data;
using Hi3Helper.Shared.GameConversion;
using Hi3Helper.Shared.ClassStruct;
using static Hi3Helper.Data.ConverterTool;
using static Hi3Helper.Shared.Region.LauncherConfig;
using static Hi3Helper.Shared.Region.InstallationManagement;
using static Hi3Helper.Logger;
using static Hi3Helper.InvokeProp;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CollapseLauncher.Dialogs
{
    public partial class InstallationMigrateSteam : Page
    {
        string sourcePath;
        string targetPath;
        string endpointURL;
        CancellationTokenSource tokenSource = new CancellationTokenSource();
        List<FilePropertiesRemote> BrokenFileIndexesProperty = new List<FilePropertiesRemote>();

        public InstallationMigrateSteam()
        {
            try
            {
                this.InitializeComponent();
            }
            catch (Exception ex)
            {
                LogWriteLine($"{ex}", Hi3Helper.LogType.Error, true);
                ErrorSender.SendException(ex);
            }
        }

        public async void StartMigrationProcess()
        {
            try
            {
                endpointURL = string.Format(CurrentRegion.ZipFileURL, Path.GetFileNameWithoutExtension(regionResourceProp.data.game.latest.path));
                if (await DoCheckPermission())
                {
                    await DoMigrationProcess();
                }

                targetPath = GamePathOnSteam;
                await DoCompareProcess();
                await DoConversionProcess();
                await DoVerification();

                if (BrokenFileIndexesProperty.Count > 0)
                {
                    await Dialog_SteamConversionFailedDialog(Content);
                    OperationCancelled();
                    return;
                }

                ApplyConfiguration();
                OperationCancelled(true);
            }
            catch (OperationCanceledException)
            {
                LogWriteLine($"Conversion process is cancelled for Game Region: {CurrentRegion.ZoneName}");
                OperationCancelled();
            }
            catch (Exception ex)
            {
                LogWriteLine($"{ex}", Hi3Helper.LogType.Error, true);
                ErrorSender.SendException(ex);
            }
        }

        public void ApplyConfiguration()
        {
            gameIni.Profile["launcher"]["game_install_path"] = targetPath;
            gameIni.Profile.Save(gameIni.ProfilePath);

            gameIni.Config = new IniFile();
            gameIni.Config.Add("General", new Dictionary<string, IniValue>
            {
                    { "channel", new IniValue(1) },
                    { "cps", new IniValue() },
                    { "game_version", new IniValue(regionResourceProp.data.game.latest.version) },
                    { "sub_channel", new IniValue(1) },
                    { "sdk_version", new IniValue() },
            });
            gameIni.Config.Save(gameIni.ConfigStream = new FileStream(Path.Combine(targetPath, "config.ini"), FileMode.Create, FileAccess.Write));

            File.Delete(Path.Combine(targetPath, "_conversion_unfinished"));
        }

        private async Task DoConversionProcess()
        {
            long TotalSizeOfBrokenFile = 0;
            DispatcherQueue.TryEnqueue(() =>
            {
                ProgressSlider.Value = 3;

                Step4.Opacity = 1f;
                Step4ProgressRing.IsIndeterminate = true;
                Step4ProgressRing.Value = 0;
                Step4ProgressStatus.Text = "Waiting for Prompt...";
            });

            TotalSizeOfBrokenFile = BrokenFileIndexesProperty.Sum(x => x.S)
                                  + BrokenFileIndexesProperty.Where(x => x.BlkC != null).Sum(x => x.BlkC.Sum(x => x.BlockSize));

            LogWriteLine($"Steam to Global Version conversion will take {SummarizeSizeSimple(TotalSizeOfBrokenFile)} of file size to download!\r\n\tThe files are including:");

            foreach (var file in BrokenFileIndexesProperty)
            {
                LogWriteLine($"\t{file.N} {SummarizeSizeSimple(file.S)}", Hi3Helper.LogType.Empty);
            }

            switch (await Dialog_SteamConversionDownloadDialog(Content, SummarizeSizeSimple(TotalSizeOfBrokenFile)))
            {
                case ContentDialogResult.None:
                    OperationCancelled();
                    break;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                Step4ProgressRing.IsIndeterminate = false;
                Step4ProgressRing.Value = 0;
            });

            await Task.Run(() => StartConversionTask());

            DispatcherQueue.TryEnqueue(() =>
            {
                ProgressSlider.Value = 3;

                Step4.Opacity = 1f;
                Step4ProgressRing.IsIndeterminate = false;
                Step4ProgressRing.Value = 100;
                Step4ProgressStatus.Text = "Completed!";
            });
        }

        private void StartConversionTask()
        {
            SteamConversion conversionTool = new SteamConversion(targetPath, endpointURL, BrokenFileIndexesProperty, tokenSource);

            conversionTool.ProgressChanged += ConversionProgressChanged;
            conversionTool.StartConverting();
            conversionTool.ProgressChanged -= ConversionProgressChanged;
        }

        private async Task DoCompareProcess()
        {
            await Task.Run(() =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    ProgressSlider.Value = 2;

                    Step3.Opacity = 1f;
                    Step3ProgressRing.IsIndeterminate = false;
                    Step3ProgressRing.Value = 0;
                    Step3ProgressStatus.Text = "Fetching API...";
                });

                StartCheckIntegrity();

                DispatcherQueue.TryEnqueue(() =>
                {
                    ProgressSlider.Value = 2;

                    Step3.Opacity = 1f;
                    Step3ProgressRing.IsIndeterminate = false;
                    Step3ProgressRing.Value = 100;
                    Step3ProgressStatus.Text = "Completed!";
                });
            });
        }

        private async Task DoVerification()
        {
            await Task.Run(() =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    ProgressSlider.Value = 4;

                    Step5.Opacity = 1f;
                    Step5ProgressRing.IsIndeterminate = false;
                    Step5ProgressRing.Value = 0;
                    Step5ProgressStatus.Text = "Fetching API...";
                });

                StartCheckVerification();

                DispatcherQueue.TryEnqueue(() =>
                {
                    Step5.Opacity = 1f;
                    Step5ProgressRing.IsIndeterminate = false;
                    Step5ProgressRing.Value = 100;
                    Step5ProgressStatus.Text = "Completed!";
                });
            });
        }

        private void StartCheckIntegrity()
        {
            CheckIntegrity integrityTool = new CheckIntegrity(targetPath, endpointURL, tokenSource);

            integrityTool.ProgressChanged += IntegrityProgressChanged;
            integrityTool.StartCheckIntegrity();
            integrityTool.ProgressChanged -= IntegrityProgressChanged;

            BrokenFileIndexesProperty = integrityTool.GetNecessaryFileList();
        }

        private void StartCheckVerification()
        {
            CheckIntegrity integrityTool = new CheckIntegrity(targetPath, endpointURL, tokenSource);

            integrityTool.ProgressChanged += VerificationProgressChanged;
            integrityTool.StartCheckIntegrity();
            integrityTool.ProgressChanged -= VerificationProgressChanged;

            BrokenFileIndexesProperty = integrityTool.GetNecessaryFileList();
        }

        private void IntegrityProgressChanged(object sender, CheckIntegrityChanged e)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                Step3ProgressRing.Value = Math.Round(e.ProgressPercentage, 2);
                Step3ProgressStatus.Text = $"{e.Message} {Math.Round(e.ProgressPercentage, 0)}% ({SummarizeSizeSimple(e.CurrentSpeed)}/s)...";
            });
        }

        private void VerificationProgressChanged(object sender, CheckIntegrityChanged e)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                Step5ProgressRing.Value = Math.Round(e.ProgressPercentage, 2);
                Step5ProgressStatus.Text = $"{e.Message} {Math.Round(e.ProgressPercentage, 0)}% ({SummarizeSizeSimple(e.CurrentSpeed)}/s)...";
            });
        }

        private void ConversionProgressChanged(object sender, ConversionTaskChanged e)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                Step4ProgressRing.Value = Math.Round(e.ProgressPercentage, 2);
                Step4ProgressStatus.Text = $"{e.Message} {Math.Round(e.ProgressPercentage, 0)}% ({SummarizeSizeSimple(e.CurrentSpeed)}/s)...";
            });
        }

        private async Task DoMigrationProcess()
        {
            try
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    ProgressSlider.Value = 1;

                    Step2.Opacity = 1f;
                    Step2ProgressRing.IsIndeterminate = true;
                    Step2ProgressRing.Value = 0;
                    Step2ProgressStatus.Text = "Migration process will be running in 5 seconds. Please accept the UAC prompt to begin the migration process.";
                });

                Process proc = new Process();
                proc.StartInfo.FileName = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CollapseLauncher.Invoker.exe");
                proc.StartInfo.UseShellExecute = true;
                proc.StartInfo.Arguments = $"movesteam \"{sourcePath}\" \"{targetPath}\" \"{CurrentRegion.SteamInstallRegistryLocation}\" InstallLocation";
                proc.StartInfo.Verb = "runas";

                LogWriteLine($"Launching Invoker with Argument:\r\n\t{proc.StartInfo.Arguments}");

                await Task.Delay(5000);

                proc.Start();

                DispatcherQueue.TryEnqueue(() =>
                {
                    Step2ProgressStatus.Text = "Running...";
                });

                await Task.Run(() => proc.WaitForExit());

                DispatcherQueue.TryEnqueue(() =>
                {
                    Step2ProgressRing.IsIndeterminate = false;
                    Step2ProgressRing.Value = 100;
                    Step2ProgressStatus.Text = "Completed!";
                });
            }
            catch (Exception)
            {
                OperationCancelled();
            }
        }

        private async Task<bool> DoCheckPermission()
        {
            FolderPicker folderPicker = new FolderPicker();
            StorageFolder folder;

            DispatcherQueue.TryEnqueue(() => Step1.Opacity = 1f);

            if (IsUserHasPermission(GamePathOnSteam))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    ProgressSlider.Value = 2;

                    Step1ProgressRing.IsIndeterminate = false;
                    Step1ProgressRing.Value = 100;
                    Step1ProgressStatus.Text = "Completed!";

                    Step2.Opacity = 1f;
                    Step2ProgressRing.IsIndeterminate = false;
                    Step2ProgressRing.Value = 100;
                    Step2ProgressStatus.Text = "Skipped!";
                });

                return false;
            }

            bool isChoosen = false;
            string choosenFolder = "";
            while (!isChoosen)
            {
                switch (await Dialog_SteamConversionNoPermission(Content))
                {
                    case ContentDialogResult.None:
                        OperationCancelled();
                        break;
                    case ContentDialogResult.Primary:
                        sourcePath = GamePathOnSteam;
                        choosenFolder = Path.Combine(AppGameFolder, CurrentRegion.ProfileName);
                        targetPath = Path.Combine(choosenFolder, Path.GetFileName(GamePathOnSteam));
                        break;
                    case ContentDialogResult.Secondary:
                        folderPicker.FileTypeFilter.Add("*");
                        WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, m_windowHandle);
                        folder = await folderPicker.PickSingleFolderAsync();

                        if (folder == null)
                            OperationCancelled();

                        choosenFolder = folder.Path;

                        sourcePath = GamePathOnSteam;
                        targetPath = Path.Combine(choosenFolder, Path.GetFileName(GamePathOnSteam));
                        break;
                }

                if (!(isChoosen = IsUserHasPermission(choosenFolder)))
                    await Dialog_InsufficientWritePermission(Content, choosenFolder);
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                Step1ProgressRing.IsIndeterminate = false;
                Step1ProgressRing.Value = 100;
                Step1ProgressStatus.Text = "Completed!";
            });

            return true;
        }

        private void OperationCancelled(bool noException = false)
        {
            MigrationWatcher.IsMigrationRunning = false;

            if (!noException)
                throw new OperationCanceledException();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            StartMigrationProcess();
        }
    }
}