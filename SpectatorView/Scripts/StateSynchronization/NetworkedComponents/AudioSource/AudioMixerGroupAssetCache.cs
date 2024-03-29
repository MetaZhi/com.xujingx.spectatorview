﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using UnityEngine.Audio;

namespace Microsoft.MixedReality.SpectatorView
{
    internal class AudioMixerGroupAssetCache : AssetCache<AudioMixerGroup>
    {
        private static bool IsAudioMixerFileExtension(string fileExtension)
        {
            return fileExtension == ".mixer";
        }

        protected override IEnumerable<AudioMixerGroup> EnumerateAllAssets()
        {
            foreach (AudioMixer mixer in EnumerateAllAssetsInAssetDatabase<AudioMixer>(IsAudioMixerFileExtension))
            {
                AudioMixerGroup[] groups = mixer.FindMatchingGroups("");

                foreach (AudioMixerGroup group in groups)
                {
                    yield return group;
                }
            }
        }
    }
}