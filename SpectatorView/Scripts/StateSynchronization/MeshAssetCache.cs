﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Microsoft.MixedReality.SpectatorView
{
    internal class MeshAssetCache : AssetCache<Mesh>
    {
        protected override IEnumerable<Mesh> EnumerateAllAssets()
        {
            foreach (MeshFilter meshFilter in EnumerateAllComponentsInScenesAndPrefabs<MeshFilter>())
            {
                if (meshFilter.sharedMesh != null)
                {
                    yield return meshFilter.sharedMesh;
                }
            }

            foreach (SkinnedMeshRenderer meshRenderer in EnumerateAllComponentsInScenesAndPrefabs<SkinnedMeshRenderer>())
            {
                if (meshRenderer.sharedMesh != null)
                {
                    yield return meshRenderer.sharedMesh;
                }
            }
        }
    }
}