﻿using StableDiffusionGui.Data;
using StableDiffusionGui.Main;
using StableDiffusionGui.MiscUtils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ZetaLongPaths;
using static StableDiffusionGui.Main.Enums.Models;
using static StableDiffusionGui.Main.Enums.StableDiffusion;

namespace StableDiffusionGui.Io
{
    internal class Models
    {

        public static List<string> GetAllModelDirs(bool includeBuiltin = true)
        {
            List<string> mdlFolders = new List<string>();

            if (includeBuiltin)
                mdlFolders.Add(Paths.GetModelsPath());

            foreach (Enums.Models.Type type in Enum.GetValues(typeof(Enums.Models.Type)).Cast<Enums.Models.Type>())
            {
                List<string> customModelDirsList = Config.Instance.CustomModelDirs;

                if (customModelDirsList != null)
                    mdlFolders.AddRange(customModelDirsList, out mdlFolders);
            }

            return mdlFolders;
        }

        public static List<Model> GetEmbeddings ()
        {
            List<string> mdlFolders = GetAllModelDirs();
            var fileList = new List<ZlpFileInfo>();

            foreach (string folderPath in mdlFolders)
                fileList.AddRange(IoUtils.GetFileInfosSorted(Path.Combine(folderPath, Constants.Dirs.Models.Embeddings), false, "*.*").ToList());

            return fileList.Select(f => new Model(f, Format.Pytorch, Enums.Models.Type.Embedding)).ToList();
        }

        public static List<Model> GetModelsAll(bool removeUnknownModels = true)
        {
            List<Model> list = new List<Model>();

            try
            {
                var subDirs = new List<string>() { Constants.Dirs.Models.Vae, Constants.Dirs.Models.Embeddings };
                List<string> mdlFolders = GetAllModelDirs();
                var fileList = new List<ZlpFileInfo>();

                foreach (string folderPath in mdlFolders)
                {
                    var dirs = new List<string> { folderPath };
                    subDirs.ForEach(d => dirs.Add(Path.Combine(folderPath, d)));

                    foreach (string dir in dirs.Where(d => Directory.Exists(d)))
                        fileList.AddRange(IoUtils.GetFileInfosSorted(dir, false, "*.*").ToList());
                }

                list.AddRange(fileList.Select(f => new Model(f))); // Add file-based models to final list

                var dirList = new List<ZlpDirectoryInfo>();

                foreach (string folderPath in mdlFolders.Where(dir => Directory.Exists(dir)))
                {
                    var dirs = new List<string> { folderPath };
                    subDirs.ForEach(d => dirs.Add(Path.Combine(folderPath, d)));

                    foreach (string dir in dirs.Where(d => Directory.Exists(d)))
                        dirList.AddRange(Directory.GetDirectories(dir, "*", SearchOption.TopDirectoryOnly).Select(x => new ZlpDirectoryInfo(x)).ToList());
                }

                dirList = dirList.Where(d => IsDirDiffusersModel(d.FullName)).ToList();
                list.AddRange(dirList.Select(f => new Model(f))); // Add folder-based models to final list
            }
            catch (Exception ex)
            {
                Logger.Log($"Error getting models: {ex.Message}");
                Logger.Log(ex.StackTrace, true);
            }

            if (removeUnknownModels)
                list = list.Where(m => m.Format != (Enums.Models.Format)(-1)).ToList();

            return list.DistinctBy(x => x.Name).OrderBy(x => x.Name).ToList();
        }

        public static List<Model> GetModels(Enums.Models.Type type = Enums.Models.Type.Normal, Implementation implementation = Implementation.InvokeAi)
        {
            var sw = Program.Debug ? new NmkdStopwatch() : null;
            IEnumerable<Model> models = GetModelsAll();
            Format[] supportedFormats = implementation.GetInfo().SupportedModelFormats;
            models = models.Where(m => m.Type == type && supportedFormats.Contains(m.Format));
            List<Model> distinctOrderedList = models.DistinctBy(x => x.Name).OrderBy(x => x.FormatIndependentName).ToList();
            if (Program.Debug) Logger.Log($"GetModels took {sw.ElapsedMilliseconds} ms", true);
            return distinctOrderedList;
        }

        public static Model GetModel(List<Model> cachedModels, string filename, Enums.Models.Type type = Enums.Models.Type.Normal, Implementation imp = Implementation.InvokeAi)
        {
            Format[] supportedFormats = imp.GetInfo().SupportedModelFormats;
            return cachedModels.Where(m => m.Name == filename && m.Type == type && supportedFormats.Contains(m.Format)).FirstOrDefault();
        }

        public static Model GetModel(string filename, Enums.Models.Type type = Enums.Models.Type.Normal, Implementation imp = Implementation.InvokeAi)
        {
            return GetModels(type, imp).Where(x => x.Name == filename).FirstOrDefault();
        }

        public static Model GetModel(List<Model> cachedModels, string filename)
        {
            return cachedModels.Where(x => x.Name == filename).FirstOrDefault();
        }

        public static bool IsDirDiffusersModel(string path)
        {
            var jsons = IoUtils.GetFileInfosSorted(path, false, "*.json");

            foreach (var json in jsons.Take(20))
            {
                if (IoUtils.ReadLines(json.FullName).Any(l => l.Contains("_diffusers_version")))
                    return true;
            }

            return false;
        }

        public static Format DetectModelFormat(string modelPath, bool print = true)
        {
            try
            {
                if (File.Exists(modelPath)) // Is file
                {
                    var file = new ZlpFileInfo(modelPath);

                    if (file.Length < 16 * 1024 * 1024) // Assume that a <16 MB file is not a valid model
                        return (Format)(-1);

                    if (file.FullName.Lower().EndsWith(".ckpt") || file.FullName.Lower().EndsWith(".pt"))
                        return Format.Pytorch;

                    if (file.FullName.Lower().EndsWith(".safetensors"))
                        return Format.Safetensors;
                }
                else if (Directory.Exists(modelPath)) // Is directory
                {
                    var dir = new ZlpDirectoryInfo(modelPath);

                    // List<string> subDirs = dir.GetDirectories().Select(d => d.Name).ToList();
                    // 
                    // bool diffusersStructureValid = new[] { "text_encoder", "tokenizer", "unet" }.All(d => subDirs.Contains(d));
                    // var unetDir = new ZlpDirectoryInfo(Path.Combine(dir.FullName, "unet"));
                    // bool unetValid = unetDir.Exists && IoUtils.GetDirSize(unetDir.FullName, false) >= 64 * 1024 * 1024; // Assume that a <64 MB unet file is not valid
                    string indexJsonPath = IoUtils.GetFileInfosSorted(dir.FullName, false, "*.json").OrderByDescending(f => f.Length).FirstOrDefault()?.FullName;

                    if (File.Exists(indexJsonPath))
                    {
                        var lines = File.ReadAllLines(indexJsonPath);

                        if (lines.Any(l => l.Contains("_diffusers_version")))
                        {
                            if (File.ReadAllLines(indexJsonPath).Any(l => l.Contains(@"""_class_name"": ""Onnx")))
                                return Format.DiffusersOnnx;
                            if (File.ReadAllLines(indexJsonPath).Any(l => l.Contains(@"""_class_name"":")))
                                return Format.Diffusers;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to detect model format: {ex.Message} ({modelPath})", !print);
            }

            return (Format)(-1);
        }

        public static Enums.Models.Type GetModelType(string modelPath, bool print = true)
        {
            try
            {
                string parentDirName = "";

                if (File.Exists(modelPath))
                {
                    parentDirName = new ZlpFileInfo(modelPath).Directory.Name;
                }
                else if (Directory.Exists(modelPath))
                {
                    parentDirName = new ZlpDirectoryInfo(modelPath).Parent.Name;
                }

                if (parentDirName == Constants.Dirs.Models.Vae)
                    return Enums.Models.Type.Vae;

                if (parentDirName == Constants.Dirs.Models.Embeddings)
                    return Enums.Models.Type.Embedding;

                return Enums.Models.Type.Normal;
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to detect model type: {ex.Message} ({modelPath})", !print);
            }

            return (Enums.Models.Type)(-1);
        }

        public static void SetClipSkip(Model model, int layersToSkip = 1)
        {
            if(layersToSkip > 0 && model.Format != Format.Diffusers)
            {
                Logger.Log($"Warning: Cannot apply CLIP Skip to this model because it is not a Diffusers model.");
                return;
            }

            if (!Directory.Exists(model.FullName))
            {
                Logger.Log($"Clip Skip Patcher: Not a model directory: {model.FullName}", true);
                return;
            }

            string jsonPath = Path.Combine(model.FullName, "text_encoder", "config.json");
            string srcJsonPath = jsonPath + ".original";

            if (!File.Exists(jsonPath))
            {
                Logger.Log($"Clip Skip Patcher: Can't find config json ({jsonPath})", true);
                return;
            }

            try
            {
                if (!File.Exists(srcJsonPath))
                    File.Copy(jsonPath, srcJsonPath);

                var lines = File.ReadAllLines(srcJsonPath);

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];

                    if (line.Trim().StartsWith("\"num_hidden_layers\": "))
                    {
                        int layers = line.Split(':').Last().GetInt(false);
                        int newLayers = (layers - layersToSkip).Clamp(1, int.MaxValue);

                        if (layers == newLayers) // Already set to the right value, return to avoid unnecessary I/O
                            return;

                        string newText = $"{line.Split("\"num_hidden_layers\": ")[0]}\"num_hidden_layers\": {newLayers},";
                        lines[i] = newText;
                        Logger.Log($"Clip Skip Patcher: Using {newLayers} out of {layers} layers (Skipping {layersToSkip})", true);
                    }
                }

                File.WriteAllLines(jsonPath, lines);
            }
            catch(Exception ex)
            {
                Logger.LogException(ex);
            }
        }

        public static bool HasAnyInpaintingModels (IEnumerable<Model> models = null, Implementation imp = (Implementation)(-1))
        {
            if (models == null)
                models = GetModelsAll();

            if(imp != (Implementation)(-1))
            {
                Format[] supportedFormats = imp.GetInfo().SupportedModelFormats;
                models = models.Where(m => supportedFormats.Contains(m.Format));
            }

            return models.Any(m => m.FormatIndependentName.Lower().EndsWith("inpainting"));
        }
    }
}
