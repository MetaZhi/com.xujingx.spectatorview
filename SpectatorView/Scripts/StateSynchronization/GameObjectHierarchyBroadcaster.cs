﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using UnityEngine;

namespace Microsoft.MixedReality.SpectatorView
{
    /// <summary>
    /// Indicates to the StateSynchronizationBroadcaster that the GameObject
    /// this is attached to and all of its descendants should be broadcast
    /// to connected StateSynchronizationObservers.
    /// </summary>
    public class GameObjectHierarchyBroadcaster : MonoBehaviour
    {
        private TransformBroadcaster TransformBroadcaster;

        private void Start()
        {
            if (StateSynchronizationBroadcaster.IsInitialized)
            {
                StateSynchronizationBroadcaster.Instance.Connected += OnConnected;

                // If we have connected to other devices, make sure we immediately
                // add a new TransformBroadcaster.
                if (StateSynchronizationBroadcaster.Instance.HasConnections)
                {
                    OnConnected(null);
                }
            }
        }

        private void OnDestroy()
        {
            if (StateSynchronizationBroadcaster.IsInitialized && StateSynchronizationBroadcaster.Instance != null)
            {
                StateSynchronizationBroadcaster.Instance.Connected -= OnConnected;
            }
        }

        private void OnConnected(INetworkConnection connection)
        {
            if (TransformBroadcaster != null)
            {
                Destroy(TransformBroadcaster);
            }
            TransformBroadcaster = this.gameObject.EnsureComponent<TransformBroadcaster>();
        }
    }
}
