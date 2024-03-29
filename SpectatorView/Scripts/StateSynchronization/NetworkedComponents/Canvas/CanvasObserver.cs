﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO;
using UnityEngine;

namespace Microsoft.MixedReality.SpectatorView
{
    internal class CanvasObserver : ComponentObserver<Canvas>
    {
        public override void Read(INetworkConnection connection, BinaryReader message)
        {
            CanvasBroadcaster.ChangeType changeType = (CanvasBroadcaster.ChangeType)message.ReadByte();

            if (CanvasBroadcaster.HasFlag(changeType, CanvasBroadcaster.ChangeType.Enabled))
            {
                attachedComponent.enabled = message.ReadBoolean();
            }

            if (CanvasBroadcaster.HasFlag(changeType, CanvasBroadcaster.ChangeType.Properties))
            {
                attachedComponent.renderMode = (RenderMode)message.ReadByte();
                attachedComponent.sortingLayerID = message.ReadInt32();
                attachedComponent.sortingOrder = message.ReadInt32();
                attachedComponent.overrideSorting = message.ReadBoolean();
            }
        }
    }
}
