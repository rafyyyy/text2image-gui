﻿using StableDiffusionGui.Main;
using System.Collections.Generic;
using static StableDiffusionGui.Main.Enums.Models;

namespace StableDiffusionGui.Data
{
    public class ImplementationInfo
    {
        public enum Feature { InteractiveCli, CustomModels, CustomVae, HalfPrecisionToggle, NegPrompts, NativeInpainting, DeviceSelection, MultipleSamplers, Embeddings, SeamlessMode, SymmetricMode, HiresFix }
        public List<Feature> SupportedFeatures = new List<Feature>();
        public Enums.Ai.Backend Backend { get; set; } = Enums.Ai.Backend.Cuda;
        public string[] ValidModelExts { get; set; } = new string[0];
        public string[] ValidModelExtsVae { get; set; } = new string[0];
        public Format[] SupportedModelFormats { get; set; } = new Format[0];

        public ImplementationInfo() { }

        public ImplementationInfo(Enums.StableDiffusion.Implementation imp)
        {
            if (imp == Enums.StableDiffusion.Implementation.InvokeAi)
            {
                Backend = Enums.Ai.Backend.Cuda;
                SupportedModelFormats = new Format[] { Format.Pytorch, Format.Safetensors, Format.Diffusers };
                ValidModelExts = new string[] { ".ckpt", ".safetensors" };
                ValidModelExtsVae = new string[] { ".ckpt", ".pt" };
                SupportedFeatures = new List<Feature> { Feature.InteractiveCli, Feature.CustomModels, Feature.CustomVae, Feature.HalfPrecisionToggle, Feature.NegPrompts, Feature.NativeInpainting, Feature.DeviceSelection,
                    Feature.MultipleSamplers, Feature.Embeddings, Feature.SeamlessMode, Feature.SymmetricMode, Feature.HiresFix };
            }
            else if (imp == Enums.StableDiffusion.Implementation.OptimizedSd)
            {
                Backend = Enums.Ai.Backend.Cuda;
                SupportedModelFormats = new Format[] { Format.Pytorch };
                ValidModelExts = new string[] { ".ckpt" };
                SupportedFeatures = new List<Feature> { Feature.CustomModels, Feature.HalfPrecisionToggle, Feature.DeviceSelection };
            }
            else if (imp == Enums.StableDiffusion.Implementation.DiffusersOnnx)
            {
                Backend = Enums.Ai.Backend.DirectMl;
                SupportedModelFormats = new Format[] { Format.DiffusersOnnx };
                SupportedFeatures = new List<Feature> { Feature.InteractiveCli, Feature.CustomModels, Feature.HalfPrecisionToggle, Feature.NegPrompts };
            }
            else if (imp == Enums.StableDiffusion.Implementation.InstructPixToPix)
            {
                Backend = Enums.Ai.Backend.Cuda;
                SupportedFeatures = new List<Feature> { Feature.InteractiveCli, Feature.NegPrompts };
            }
        }
    }
}
