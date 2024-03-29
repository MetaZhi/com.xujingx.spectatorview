﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Microsoft.MixedReality.SpectatorView
{
    internal class CameraBroadcaster : ComponentBroadcaster<CameraService, CameraBroadcaster.ChangeType>
    {
        [Flags]
        public enum ChangeType : byte
        {
            None = 0x0,
            Properties = 0x1,
        }

        private Camera cameraBroadcaster;
        private CameraProperties previousProperties;

        protected override void Awake()
        {
            base.Awake();

            this.cameraBroadcaster = GetComponent<Camera>();
        }

        public static bool HasFlag(ChangeType changeType, ChangeType flag)
        {
            return (changeType & flag) == flag;
        }

        protected override bool HasChanges(ChangeType changeFlags)
        {
            return changeFlags != ChangeType.None;
        }

        protected override ChangeType CalculateDeltaChanges()
        {
            ChangeType changeType = ChangeType.None;
            CameraProperties newProperties = new CameraProperties(this.cameraBroadcaster);
            if (newProperties != previousProperties)
            {
                previousProperties = newProperties;
                changeType |= ChangeType.Properties;
            }

            return changeType;
        }

        protected override void SendCompleteChanges(IEnumerable<INetworkConnection> connections)
        {
            SendDeltaChanges(connections, ChangeType.Properties);
        }

        protected override void SendDeltaChanges(IEnumerable<INetworkConnection> connections, ChangeType changeFlags)
        {
            using (MemoryStream memoryStream = new MemoryStream())
            using (BinaryWriter message = new BinaryWriter(memoryStream))
            {
                ComponentBroadcasterService.WriteHeader(message, this);

                message.Write((byte)changeFlags);

                if (HasFlag(changeFlags, ChangeType.Properties))
                {
                    message.Write(previousProperties.isMainCamera);
                    message.Write(previousProperties.cullingMask);
                }

                message.Flush();

                memoryStream.TryGetBuffer(out var buffer);
                StateSynchronizationSceneManager.Instance.Send(connections, buffer.Array, buffer.Offset, buffer.Count);
            }
        }

        private struct CameraProperties
        {
            public int cullingMask;
            public bool isMainCamera;

            public CameraProperties(Camera camera)
            {
                cullingMask = camera.cullingMask;
                isMainCamera = (camera.tag == "MainCamera");
            }

            public static bool operator ==(CameraProperties first, CameraProperties second)
            {
                return first.Equals(second);
            }

            public static bool operator !=(CameraProperties first, CameraProperties second)
            {
                return !first.Equals(second);
            }

            public override bool Equals(object obj)
            {
                if (!(obj is CameraProperties))
                {
                    return false;
                }

                CameraProperties other = (CameraProperties)obj;
                return
                    other.isMainCamera == isMainCamera &&
                    other.cullingMask == cullingMask;
            }

            public override int GetHashCode()
            {
                return cullingMask.GetHashCode();
            }
        }
    }
}