﻿using ImageMagick;
using Microsoft.WindowsAPICodePack.Dialogs;
using StableDiffusionGui.Extensions;
using StableDiffusionGui.Io;
using StableDiffusionGui.Main;
using StableDiffusionGui.Main.Utils;
using StableDiffusionGui.MiscUtils;
using StableDiffusionGui.Ui;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using static StableDiffusionGui.Main.Enums.StableDiffusion;

namespace StableDiffusionGui.Forms
{
    public partial class MainForm
    {
        public void BrowseInitImage()
        {
            if (Program.Busy)
                return;

            if (MainUi.CurrentInitImgPaths.Any())
            {
                MainUi.CurrentInitImgPaths.Clear();
            }
            else
            {
                if (!Directory.Exists(Config.Instance.LastInitImageParentPath))
                    Config.Instance.LastInitImageParentPath = MainUi.CurrentInitImgPaths.Any() ? MainUi.CurrentInitImgPaths[0].GetParentDirOfFile() : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

                CommonOpenFileDialog dialog = new CommonOpenFileDialog { RestoreDirectory = false, InitialDirectory = Config.Instance.LastInitImageParentPath, IsFolderPicker = false, Multiselect = true };

                if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
                {
                    var paths = dialog.FileNames.Where(path => Constants.FileExts.ValidImages.Contains(Path.GetExtension(path).Lower()));

                    if (paths.Count() > 0)
                    {
                        MainUi.HandleDroppedFiles(paths.ToArray(), true);
                        Config.Instance.LastInitImageParentPath = new FileInfo(paths.First()).Directory.FullName;
                    }
                    else
                    {
                        UiUtils.ShowMessageBox(dialog.FileNames.Count() == 1 ? "Invalid file type." : "None of the selected files are valid.");
                    }
                }
            }

            TryRefreshUiState();
        }

        public async Task RegenerateImageWithCurrentSettings()
        {
            if (Program.Busy)
            {
                UiUtils.ShowMessageBox("Please wait until the current process has finished.");
                return;
            }

            var prevSeedVal = upDownSeed.Value;
            var prevIterVal = upDownIterations.Value;
            upDownSeed.Value = ImageViewer.CurrentImageMetadata.Seed;
            upDownIterations.Value = 1;
            await TryRun();
            SetSeed((long)prevSeedVal);
            upDownIterations.Value = prevIterVal;
        }

        public void TryOpenPostProcessingSettings()
        {
            var imp = Config.Instance.Implementation;
            var supportedImps = new List<Implementation> { Implementation.InvokeAi };

            if (!supportedImps.Contains(imp))
            {
                UiUtils.ShowMessageBox($"Post-processing is not available with your current implementation: {Strings.Implementation.Get(imp.ToString(), true)}.");
                return;
            }

            new PostProcSettingsForm().ShowDialogForm();
        }

        public void UpdateBusyState()
        {
            SetProgress(0);

            bool imageGen = Program.State == Program.BusyState.ImageGeneration;

            runBtn.Text = imageGen ? "Cancel" : "Generate!";
            runBtn.ForeColor = imageGen ? Color.IndianRed : Color.White;
            Control[] controlsToDisable = new Control[] { };
            Control[] controlsToHide = new Control[] { };
            progressCircle.SetVisible(Program.State != Program.BusyState.Standby);

            foreach (Control c in controlsToDisable)
                c.Enabled = !imageGen;

            foreach (Control c in controlsToHide)
                c.SetVisible(!imageGen);

            if (Program.State == Program.BusyState.Standby)
                SetProgress(0);

            if (!imageGen)
                SetProgressImg(0);

            progressBarImg.SetVisible(imageGen);
            UpdateWindowTitle();
        }

        public async Task TryRun()
        {
            if (Program.Busy)
            {
                TextToImage.CancelManually();
                return;
            }

            if (MainUi.Queue.Count > 0)
            {
                generateAllQueuedPromptsToolStripMenuItem.Text = $"Generate Queued Prompts ({MainUi.Queue.Count})";
                menuStripRunQueue.Show(Cursor.Position);
            }
            else
            {
                await Run();
            }
        }

        public async Task Run(bool fromQueue = false)
        {
            try
            {
                if (Program.Busy)
                {
                    TextToImage.Cancel($"Program is already busy ({Program.State})", false);
                    return;
                }
                else
                {
                    TextToImage.Canceled = false;

                    if (!MainUi.IsInstalledWithWarning())
                        return;

                    Logger.ClearLogBox();
                    CleanPrompt();
                    TryRefreshUiState();
                    Inpainting.DeleteMaskedImage();

                    if (fromQueue)
                    {
                        if (MainUi.Queue.Where(x => x != null).Count() < 0)
                        {
                            TextToImage.Cancel("Queue is empty.", true);
                            return;
                        }

                        await TextToImage.RunTti();
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(textboxPrompt.Text))
                        {
                            TextToImage.Cancel("No prompt was entered.", true);
                            return;
                        }

                        SaveControls();
                        await TextToImage.RunTti(GetCurrentTtiSettings());
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"{ex.Message}\n{ex.StackTrace}");
            }
        }

        public void TryUseCurrentImgAsInitImg(bool ignoreBusy = false)
        {
            if (Program.Busy && !ignoreBusy)
            {
                UiUtils.ShowMessageBox("Please wait until the generation has finished.");
                return;
            }

            MainUi.AddInitImages(new[] { ImageViewer.CurrentImagePath }.ToList(), true);
        }

        public void EditMask()
        {
            if (!MainUi.CurrentInitImgPaths.Any())
                return;

            Image img = IoUtils.GetImage(MainUi.CurrentInitImgPaths[0], false);

            if (img == null)
                return;

            Size targetSize = TextToImage.CurrentTaskSettings.Res;
            Size scaleSize = Config.Instance.InitImageRetainAspectRatio ? ImgMaths.FitIntoFrame(img.Size, targetSize) : targetSize;
            img = ImgUtils.ScaleAndPad(ImgUtils.GetMagickImage(img), scaleSize, targetSize).ToBitmap();

            Inpainting.EditCurrentMask(img, IsUsingInpaintingModel);
        }

        public void ModelDownloadPrompt(string text = "")
        {
            var form = new PromptForm("Enter Model ID or URL", "Enter a model repository ID or URL. \n(E.g. \"runwayml/stable-diffusion-v1-5\").", text, 1.4f, 1f);
            form.ShowDialog();

            if (form.DialogResult == DialogResult.OK)
                DownloadModels.DownloadModel(form.EnteredText.Trim());
        }
    }
}
