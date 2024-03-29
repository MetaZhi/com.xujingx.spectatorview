﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Microsoft.MixedReality.SpectatorView
{
    internal class GlobalShaderPropertiesBroadcaster : Singleton<GlobalShaderPropertiesBroadcaster>
    {
        private object[] previousValues;
        private List<GlobalMaterialPropertyAsset> changedProperties = new List<GlobalMaterialPropertyAsset>();
        private CustomShaderPropertyAssetCache assetCache;

        protected virtual void Start()
        {
            assetCache = AssetCache.LoadAssetCache<CustomShaderPropertyAssetCache>();
        }

        private void InitializeValues()
        {
            previousValues = new object[assetCache.CustomGlobalShaderProperties.Length];
            for (int i = 0; i < assetCache.CustomGlobalShaderProperties.Length; i++)
            {
                previousValues[i] = assetCache.CustomGlobalShaderProperties[i].GetValue();
            }
        }

        public void OnFrameCompleted(NetworkConnectionDelta connectionDelta)
        {
            using (StateSynchronizationPerformanceMonitor.Instance.MeasureEventDuration(nameof(GlobalShaderPropertiesBroadcaster), nameof(OnFrameCompleted)))
            {
                if (assetCache?.CustomGlobalShaderProperties != null)
                {
                    if (previousValues == null)
                    {
                        InitializeValues();
                    }

                    if (connectionDelta.AddedConnections.Count > 0)
                    {
                        StateSynchronizationSceneManager.Instance.SendGlobalShaderProperties(assetCache.CustomGlobalShaderProperties, connectionDelta.AddedConnections);
                    }

                    if (connectionDelta.ContinuedConnections.Count > 0)
                    {
                        changedProperties.Clear();

                        for (int i = 0; i < assetCache.CustomGlobalShaderProperties.Length; i++)
                        {
                            object newValue = assetCache.CustomGlobalShaderProperties[i].GetValue();
                            if (!Equals(previousValues[i], newValue))
                            {
                                previousValues[i] = newValue;
                                changedProperties.Add(assetCache.CustomGlobalShaderProperties[i]);
                            }
                        }

                        if (changedProperties.Count > 0)
                        {
                            StateSynchronizationSceneManager.Instance.SendGlobalShaderProperties(changedProperties, connectionDelta.ContinuedConnections);
                        }
                    }
                }
            }
        }
    }
}