﻿using StableDiffusionGui.Data;
using StableDiffusionGui.Io;
using StableDiffusionGui.Main;
using StableDiffusionGui.Main.Utils;
using StableDiffusionGui.MiscUtils;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static StableDiffusionGui.Main.Enums.StableDiffusion;

namespace StableDiffusionGui.Implementations
{
    internal class InvokeAiUtils
    {
        public static string ModelsYamlPath { get { return Path.Combine(Paths.GetDataPath(), Constants.Dirs.SdRepo, "invoke", "configs", "models.yaml"); } }
        private static EasyDict<string, Enums.Models.Format> _modelFormatCache = new EasyDict<string, Enums.Models.Format>();

        public static async Task<Model> ConvertVae(Model vae, bool print = true)
        {
            string outPath = Path.ChangeExtension(vae.FullName, null);

            if (DetectModelFormatCached(vae.FullName) == Enums.Models.Format.Diffusers) // Is already correct format
                return vae;

            if (DetectModelFormatCached(outPath) == Enums.Models.Format.Diffusers) // Conversion already exists at output path
                return new Model(outPath, Enums.Models.Format.Diffusers);

            if (print)
                Logger.Log($"VAE '{vae.FormatIndependentName.Trunc(50)}' is in legacy format, converting to Diffusers format...");

            await ConvertModels.ConvVaePytorchDiffusers(vae.FullName, outPath);
            _modelFormatCache.Clear(); // Clear model type detection cache because we just converted one
            Model convertedVae = new Model(outPath);

            if (print)
                Logger.Log($"Converted '{vae.FormatIndependentName.Trunc(50)}' to Diffusers format.", false, Logger.LastUiLine.EndsWith("converting to Diffusers format..."));

            return convertedVae;
        }

        private static Enums.Models.Format DetectModelFormatCached(string path)
        {
            if (!_modelFormatCache.ContainsKey(path))
                _modelFormatCache[path] = Models.DetectModelFormat(path);

            return _modelFormatCache[path];
        }

        /// <summary> Writes all models into models.yml for InvokeAI to use </summary>
        public static void WriteModelsYamlAll(List<Model> cachedModels = null, List<Model> cachedModelsVae = null, Enums.Models.SdArch ckptArch = Enums.Models.SdArch.Automatic, bool quiet = false)
        {
            try
            {
                _modelFormatCache.Clear();

                if (cachedModels == null || cachedModels.Count < 1)
                    cachedModels = Models.GetModels(Enums.Models.Type.Normal);

                if (cachedModelsVae == null || cachedModelsVae.Count < 1)
                    cachedModelsVae = Models.GetModels(Enums.Models.Type.Vae);

                cachedModelsVae = cachedModelsVae.DistinctBy(m => m.FormatIndependentName).ToList();

                string text = "";

                cachedModelsVae.Insert(0, null); // Insert null entry, for looping (this is the VAE-less model entry)
                string dataPath = Paths.GetDataPath();

                foreach (Model mdl in cachedModels)
                {
                    string weightsPath = $"{(mdl.Format == Enums.Models.Format.Diffusers ? "path" : "weights")}: {mdl.FullName.Replace(dataPath, "../..").Wrap(true)}"; // Weights path, use relative path if possible

                    List<string> ckptArgs = null;

                    if (mdl.Format != Enums.Models.Format.Diffusers)
                    {
                        string config = TtiUtils.GetCkptConfig(mdl, true);
                        int res = mdl.LoadArchitecture == Enums.Models.SdArch.V2V ? 768 : 512;
                        ckptArgs = new List<string>() { $"width: {res}", $"height: {res}" };

                        if (config.IsNotEmpty())
                            ckptArgs.Add($"config: {config}");
                    }

                    foreach (Model mdlVae in cachedModelsVae)
                    {
                        var vae = mdlVae == null ? null : mdlVae; //mdlVae.Format == Enums.Models.Format.Diffusers ? mdlVae : await ConvertVae(mdlVae, !quiet);
                        var properties = new List<string> { weightsPath };

                        if (mdl.Format == Enums.Models.Format.Diffusers)
                        {
                            properties.Add($"format: diffusers"); // Need to specify format for diffusers models
                        }
                        else if (ckptArgs != null)
                        {
                            properties.AddRange(ckptArgs);

                            if (vae != null && vae.FullName.IsNotEmpty()) // External VAE currently only supported with legacy models
                                properties.Add($"vae: {vae.FullName.Replace(dataPath, "../..").Wrap(true)}");
                        }

                        text += $"{GetMdlNameForYaml(mdl, vae)}:\n    {string.Join("\n    ", properties)}\n\n";

                        if (mdl.Format == Enums.Models.Format.Diffusers)
                            break; // Break in order to not write any additional VAE entries as this is currently unuspported with Diffusers models
                    }
                }

                Directory.CreateDirectory(Path.Combine(Paths.GetDataPath(), Constants.Dirs.SdRepo, "invoke", "configs"));
                File.WriteAllText(ModelsYamlPath, text);
            }
            catch (Exception ex)
            {
                Logger.Log($"Error writing model list: {ex.Message}.", true);
                Logger.Log(ex.StackTrace, true);
                TextToImage.Cancel($"Error writing model list: {ex.Message.Trunc(200)}.\nCheck logs for details.", true);
            }
        }

        public static string GetMdlNameForYaml(Model mdl, Model vae)
        {
            return $"{mdl.Name}{(vae == null ? "" : $"-{vae.FormatIndependentName}")}".Replace(" ", "");
        }

        public static string GetModelsHash(List<Model> cachedModels = null, IoUtils.Hash hashType = IoUtils.Hash.CRC32)
        {
            if (cachedModels == null)
                cachedModels = Models.GetModelsAll();

            var models = cachedModels.Where(m => Implementation.InvokeAi.GetInfo().SupportedModelFormats.Contains(m.Format));
            models = models.Where(m => m.Type == Enums.Models.Type.Normal || m.Type == Enums.Models.Type.Vae);
            string modelsStr = string.Join("\n", models.Select(m => $"{m.FullName}{Config.Instance.ModelArchs.Get(m.FullName, Enums.Models.SdArch.Automatic)}").OrderBy(n => n));
            return IoUtils.GetHash(modelsStr, hashType, false);
        }

        public static string ConvertAttentionSyntax(string prompt)
        {
            if (!prompt.Contains("(") && !prompt.Contains("{")) // Skip if no parentheses/curly brackets were used
                return prompt;

            if (PromptUsesNewAttentionSyntax(prompt))
                return prompt;

            prompt = prompt.Replace("\\(", "escapedParenthesisOpen").Replace("\\)", "escapedParenthesisClose");

            var parentheses = Regex.Matches(prompt, @"\(((?>[^()]+|\((?<n>)|\)(?<-n>))+(?(n)(?!)))\)"); // Find parenthesis pairs

            for (int i = 0; i < parentheses.Count; i++)
            {
                string match = parentheses[i].Value;

                if (match.MatchesRegex(@":\d.\d+\)"))
                    continue;

                int count = match.Where(c => c == ')').Count();
                string converted = $"({match.Remove("(").Remove(")")}){new string('+', count)}";
                prompt = prompt.Replace(match, converted);
            }

            var curlyBrackets = Regex.Matches(prompt, @"\{((?>[^{}]+|\{(?<n>)|\}(?<-n>))+(?(n)(?!)))\}"); // Find curly bracket pairs

            for (int i = 0; i < curlyBrackets.Count; i++)
            {
                string match = curlyBrackets[i].Value;
                int count = match.Where(c => c == '}').Count();
                string converted = $"({match.Remove("{").Remove("}")}){new string('-', count)}";
                prompt = prompt.Replace(match, converted);
            }

            var weightsInsideParentheses = Regex.Matches(prompt, @":\d.\d+\)"); // Detect A1111 float weighted parentheses

            for (int i = 0; i < weightsInsideParentheses.Count; i++)
            {
                string match = weightsInsideParentheses[i].Value;
                float weight = match.TrimStart(':').TrimEnd(')').GetFloat();
                prompt = prompt.Replace(match, $"){weight.ToStringDot("0.#")}");
            }

            prompt = prompt.Replace("escapedParenthesisOpen", "\\(").Replace("escapedParenthesisClose", "\\)");

            return prompt;
        }

        private static bool PromptUsesNewAttentionSyntax(string p)
        {
            bool newSyntax = false;

            if (p.Contains(")+") || p.Contains(")-")) // Detect +/- weighted parentheses
                newSyntax = true;

            if (Regex.Matches(p, @"\)\d").Count >= 1 || Regex.Matches(p, @"\)\d.\d+").Count >= 1) // Detect int or float weighted parentheses
                newSyntax = true;

            if (p.Contains(".blend(") || p.Contains(".swap(")) // Check for blend and swap commands
                newSyntax = true;

            return newSyntax;
        }

        public static bool ValidateCommand(EasyDict<string, string> args, Size res)
        {
            if (args.Values.Contains("--force_outpaint"))
            {
                Bitmap img = (Bitmap)IoUtils.GetImage(args["initImg"].Split('"')[1]);
                bool transparency = ImgUtils.IsPartiallyTransparent(img);

                if (!transparency && (img.Width <= res.Height || img.Height <= res.Height))
                {
                    Logger.Log($"Can't apply outpainting because the output resolution ({res.AsString()}) is not bigger than the input ({img.Size.AsString()}), and the input image has no transparent regions.\n" +
                        $"Either increase the output resolution to be bigger than the input, or add a transparent border to your input image with an image editor.");
                    return false;
                }
            }

            return true;
        }

        public static string GetCombinedPrompt(string prompt, string negPrompt)
        {
            return $"{prompt.Trim()}{(string.IsNullOrWhiteSpace(negPrompt) ? "" : $" [{negPrompt.Trim()}]")}";
        }

        public static void LoadEmbeddingTriggerTable(string logLine, bool print = false)
        {
            if (logLine.IsEmpty())
                return;

            string text = logLine.Split(Constants.LogMsgs.Invoke.TiTriggers).Last();

            if (text.IsEmpty())
            {
                Logger.LogHidden($"Can't load TI triggers from log line because split string is empty: '{logLine}'");
                return;
            }

            InvokeAi.EmbeddingsFilesTriggers.Clear();

            foreach (string entry in text.Split(','))
            {
                try
                {
                    string[] triggerAndFile = entry.Split(" from ");
                    string trigger = triggerAndFile[0].Trim().Remove("<").Remove(">");
                    string file = Path.ChangeExtension(triggerAndFile[1].Trim(), null);
                    InvokeAi.EmbeddingsFilesTriggers[file] = trigger;
                    Logger.LogIf($"Mapped TI file to trigger: {file} => {trigger}", Program.Debug);
                }
                catch(Exception ex)
                {
                    Logger.LogHidden($"Exception trying to parse TI trigger from '{entry}': {ex.Message}");
                }
            }

            if (print)
                Logger.Log($"Compatible Embeddings: {string.Join(", ", InvokeAi.EmbeddingsFilesTriggers.Keys)}");
        }

        public static string FixPromptEmbeddingTriggers(string prompt, bool log = true)
        {
            List<string> embeddingsInPrompt = FindEmbeddingsInPrompt(prompt);
            var incompatEmbeddings = new List<string>();

            foreach (string embedding in embeddingsInPrompt)
            {
                if (InvokeAi.EmbeddingsFilesTriggers.ContainsValue(embedding)) // Already using trigger instead of filename
                    continue;

                if (InvokeAi.EmbeddingsFilesTriggers.ContainsKey(embedding))
                    prompt = prompt.Replace($"<{embedding}>", $"<{InvokeAi.EmbeddingsFilesTriggers[embedding]}>"); // Replace filename with trigger
                else
                    incompatEmbeddings.Add(embedding);
            }

            if (incompatEmbeddings.Any())
            {
                string incompatList = string.Join(", ", incompatEmbeddings.Distinct());
                Logger.LogIf($"The following embeddings are not compatible with the current model: {incompatList}", log, false);
            }

            return prompt;
        }

        public static List<string> FindEmbeddingsInPrompt(string s, bool distinct = true)
        {
            MatchCollection matches = Regex.Matches(s, @"<([^<>]+)>");
            List<string> triggers = matches.OfType<Match>().Select(match => match.Groups[1].Value).ToList();
            return distinct ? triggers.Distinct().ToList() : triggers;
        }
    }
}
