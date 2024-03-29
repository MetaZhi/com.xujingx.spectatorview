﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Microsoft.MixedReality.SpectatorView
{
    internal class FontAssetCache : AssetCache<Font>
    {
        protected override IEnumerable<Font> EnumerateAllAssets()
        {
            foreach (TextMesh textMesh in EnumerateAllComponentsInScenesAndPrefabs<TextMesh>())
            {
                if (textMesh.font != null)
                {
                    yield return textMesh.font;
                }
            }

            foreach (Text text in EnumerateAllComponentsInScenesAndPrefabs<Text>())
            {
                if (text.font != null)
                {
                    yield return text.font;
                }
            }
        }
    }
}