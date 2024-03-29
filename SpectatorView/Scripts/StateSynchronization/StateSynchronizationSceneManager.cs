﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Microsoft.MixedReality.SpectatorView
{
    /// <summary>
    /// Service that registers ComponentBroadcasterDefinitions used to create ComponentBroadcasters for components
    /// and to manage both broadcasting and observing components.
    /// </summary>
    public class StateSynchronizationSceneManager : Singleton<StateSynchronizationSceneManager>
    {
        private const int FrameVotesUntilChangingFrameSkipCount = 10;
        private const int MaximumQueuedByteCount = 2048;
        private const float QueueAgeMillisecondsPerFrameSkip = 0.05f;
        private int framesToSkipForBuffering = 0;
        private int frameSkippingStride = 1;
        private int frameSkippingVoteCounter = 0;
        private short nextLocallyUniqueId;
        private bool shouldClearScene;

        public const string DefaultStateSynchronizationPerformanceParametersPrefabName = "DefaultStateSynchronizationPerformanceParameters";
        public const string CustomBroadcasterServicesPrefabName = "CustomBroadcasterServices";

        private Dictionary<short, GameObject> objectMirrors = new Dictionary<short, GameObject>();

        private Dictionary<ShortID, IComponentBroadcasterService> componentBroadcasterServices = new Dictionary<ShortID, IComponentBroadcasterService>();

        private readonly List<INetworkConnection> addedConnections = new List<INetworkConnection>();
        private readonly List<INetworkConnection> removedConnections = new List<INetworkConnection>();
        private readonly List<INetworkConnection> continuedConnections = new List<INetworkConnection>();

        private List<IComponentBroadcaster> broadcasterComponents = new List<IComponentBroadcaster>();
        internal IReadOnlyList<IComponentBroadcaster> BroadcasterComponents
        {
            get { return broadcasterComponents; }
        }

        private List<ComponentBroadcasterDefinition> componentBroadcasterDefinitions = new List<ComponentBroadcasterDefinition>();
        internal IReadOnlyList<ComponentBroadcasterDefinition> ComponentBroadcasterDefinitions
        {
            get { return componentBroadcasterDefinitions; }
        }

        /// <summary>
        /// Registers an IComponentBroadcasterService along with a ComponentBroadcasterDefinition. This service and definition
        /// control how a particular type of Component is both broadcast and observed.
        /// </summary>
        /// <param name="service">The service which controls broadcasting and observing component changes.</param>
        /// <param name="componentDefinition">The definition controlling when a component should be broadcast.</param>
        public void RegisterService(IComponentBroadcasterService service, ComponentBroadcasterDefinition componentDefinition)
        {
            if (componentBroadcasterServices.TryGetValue(service.GetID(), out IComponentBroadcasterService existingService))
            {
                Debug.LogError($"Duplicate IComponentBroadcasterService key detected: {service.GetID().ToString()} was previously registered for a service with type {existingService.GetType().Name} and now it is being re-registered for a service with type {service.GetType().Name}.");
                return;
            }

            componentBroadcasterDefinitions.Add(componentDefinition);
            componentBroadcasterServices.Add(service.GetID(), service);
        }

        internal Transform RootTransform
        {
            get { return transform; }
        }

        protected override void Awake()
        {
            base.Awake();

            GameObject customServices = Resources.Load<GameObject>(CustomBroadcasterServicesPrefabName);
            if (customServices != null)
            {
                Instantiate(customServices, null);
            }

            GameObject performanceParameters = Resources.Load<GameObject>(DefaultStateSynchronizationPerformanceParametersPrefabName);
            if (performanceParameters != null)
            {
                Instantiate(performanceParameters, this.transform);
            }
        }

        internal short GetNewTransformId()
        {
            short startId = nextLocallyUniqueId;
            while (nextLocallyUniqueId == TransformBroadcaster.NullTransformId || objectMirrors.ContainsKey(nextLocallyUniqueId))
            {
                nextLocallyUniqueId++;

                if (nextLocallyUniqueId == startId)
                {
                    throw new InvalidOperationException("Exceeded the maximum number of transforms");
                }
            }

            return nextLocallyUniqueId;
        }

        internal void AddComponentBroadcaster(IComponentBroadcaster ComponentBroadcaster)
        {
            broadcasterComponents.Add(ComponentBroadcaster);
        }

        private void Start()
        {
            if (StateSynchronizationBroadcaster.IsInitialized)
            {
                StateSynchronizationBroadcaster.Instance.Connected += OnClientConnected;
                StateSynchronizationBroadcaster.Instance.Disconnected += OnClientDisconnected;

                if (StateSynchronizationBroadcaster.Instance.IsConnected)
                {
                    foreach (var connection in StateSynchronizationBroadcaster.Instance.Connections)
                    {
                        OnClientConnected(connection);
                    }
                }
            }

            StartCoroutine(RunEndOfFrameUpdates());
        }

        private void OnClientConnected(INetworkConnection connection)
        {
            addedConnections.Add(connection);
        }

        private void OnClientDisconnected(INetworkConnection connection)
        {
            removedConnections.Add(connection);
        }

        internal void MarkSceneDirty()
        {
            shouldClearScene = true;
        }

        private void ClearScene()
        {
            int numChildren = transform.childCount;
            while (numChildren > 0)
            {
                numChildren--;
                DestroyImmediate(transform.GetChild(numChildren).gameObject);
            }

            objectMirrors.Clear();
            broadcasterComponents.Clear();
        }

        /// <summary>
        /// Writes the header for a ComponentBroadcaster's synchronize command, used
        /// to send a component's state change from the broadcaster to the observer.
        /// </summary>
        /// <param name="message">The message in which to write the header.</param>
        public void WriteSynchronizeCommandHeader(BinaryWriter message)
        {
            message.Write(StateSynchronizationObserver.SyncCommand);
            message.Write(Time.time);
        }

        /// <summary>
        /// Sends a message to a collection of INetworkConnections.
        /// </summary>
        /// <param name="connections">The connections to send the message to</param>
        /// <param name="data">A reference to the data to send</param>
        /// <param name="offset">The offset from the start of the array to use to obtain the data to send</param>
        /// <param name="length">The length of the data to send</param>
        public void Send(IEnumerable<INetworkConnection> connections, byte[] data, long offset, long length)
        {
            using (StateSynchronizationPerformanceMonitor.Instance.MeasureEventDuration(nameof(StateSynchronizationSceneManager), "Send"))
            {
                if (StateSynchronizationBroadcaster.IsInitialized && StateSynchronizationBroadcaster.Instance.HasConnections)
                {
                    foreach (INetworkConnection connection in connections)
                    {
                        StateSynchronizationPerformanceMonitor.Instance.IncrementEventCount(nameof(StateSynchronizationSceneManager), "Send");
                        connection.Send(data, offset, length);
                    }
                }
            }
        }

        internal void SendGlobalShaderProperties(IList<GlobalMaterialPropertyAsset> changedProperties, IEnumerable<INetworkConnection> connections)
        {
            using (MemoryStream memoryStream = new MemoryStream())
            using (BinaryWriter message = new BinaryWriter(memoryStream))
            {
                WriteSynchronizeCommandHeader(message);

                message.Write(SynchronizedSceneChangeTypeGlobalShaderProperty.Value);
                message.Write(changedProperties.Count);
                foreach (var propertyAccessor in changedProperties)
                {
                    propertyAccessor.Write(message);
                }

                message.Flush();
                foreach (INetworkConnection connection in connections)
                {
                    memoryStream.TryGetBuffer(out var buffer);
                    connection.Send(buffer.Array, buffer.Offset, buffer.Count);
                }
            }
        }

        private void ReadGlobalShaderProperties(BinaryReader message)
        {
            int count = message.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                GlobalMaterialPropertyAsset.Read(message);
            }
        }

        static readonly ShortID SynchronizedSceneChangeTypeGlobalShaderProperty = new ShortID("SHA");

        internal void ReceiveMessage(INetworkConnection connection, BinaryReader reader)
        {
            ShortID typeID = reader.ReadShortID();

            if (typeID == SynchronizedSceneChangeTypeGlobalShaderProperty)
            {
                ReadGlobalShaderProperties(reader);
            }
            else
            {
                if (shouldClearScene)
                {
                    ClearScene();
                    shouldClearScene = false;
                }

                IComponentBroadcasterService componentService;
                if (componentBroadcasterServices.TryGetValue(typeID, out componentService))
                {
                    short id = reader.ReadInt16();
                    ComponentBroadcasterChangeType changeType = (ComponentBroadcasterChangeType)reader.ReadByte();

                    switch (changeType)
                    {
                        case ComponentBroadcasterChangeType.Created:
                            {
                                GameObject mirror = GetOrCreateMirror(id);
                                componentService.Create(mirror);
                            }
                            break;
                        case ComponentBroadcasterChangeType.Updated:
                            {
                                GameObject mirror = GetOrCreateMirror(id);
                                componentService.Read(connection, reader, mirror);
                            }
                            break;
                        case ComponentBroadcasterChangeType.Destroyed:
                            {
                                GameObject mirror = FindGameObjectWithId(id);
                                if (mirror != null)
                                {
                                    componentService.Destroy(mirror);
                                }
                            }
                            break;
                    }
                }
            }
        }

        internal void LerpReceiveMessage(BinaryReader reader, float lerpVal)
        {
            ShortID typeID = reader.ReadShortID();

            IComponentBroadcasterService componentService;
            if (componentBroadcasterServices.TryGetValue(typeID, out componentService))
            {
                short id = reader.ReadInt16();
                ComponentBroadcasterChangeType changeType = (ComponentBroadcasterChangeType)reader.ReadByte();
                if (changeType == ComponentBroadcasterChangeType.Updated)
                {
                    GameObject mirror;
                    if (objectMirrors.TryGetValue(id, out mirror))
                    {
                        componentService.LerpRead(reader, mirror, lerpVal);
                    }
                }
            }
        }

        internal GameObject FindGameObjectWithId(short id)
        {
            GameObject objectMirror;
            objectMirrors.TryGetValue(id, out objectMirror);
            return objectMirror;
        }

        internal GameObject GetOrCreateMirror(short id)
        {
            GameObject objectMirror;
            if (!objectMirrors.TryGetValue(id, out objectMirror))
            {
                objectMirror = new GameObject(id.ToString());
                objectMirror.AddComponent<TransformObserver>().Id = id;
                objectMirror.transform.SetParent(RootTransform, worldPositionStays: false);
                objectMirrors.Add(id, objectMirror);
            }

            return objectMirror;
        }

        internal void AssignMirror(GameObject objectMirror, short id)
        {
            GameObject existingMirror;
            if (objectMirrors.TryGetValue(id, out existingMirror))
            {
                Debug.LogError("Attempting to assign a mirror ID that already exists: " + id + " is assigned to a GameObject with name " + existingMirror.name);
            }
            else
            {
                objectMirror.EnsureComponent<TransformObserver>().Id = id;
                objectMirrors.Add(id, objectMirror);
            }
        }

        internal void DestroyMirror(short id)
        {
            GameObject destroyedObject;
            if (objectMirrors.TryGetValue(id, out destroyedObject))
            {
                Destroy(destroyedObject);
                objectMirrors.Remove(id);
            }
        }

        internal void RemoveMirror(short id)
        {
            objectMirrors.Remove(id);
        }

        private IEnumerator RunEndOfFrameUpdates()
        {
            while (this != null && this.isActiveAndEnabled)
            {
                yield return new WaitForEndOfFrame();

                using (StateSynchronizationPerformanceMonitor.Instance.MeasureEventDuration(nameof(StateSynchronizationSceneManager), nameof(RunEndOfFrameUpdates)))
                {
                    if (framesToSkipForBuffering == 0)
                    {
                        if (StateSynchronizationBroadcaster.IsInitialized && StateSynchronizationBroadcaster.Instance != null)
                        {
                            int bytesQueued = StateSynchronizationBroadcaster.Instance.OutputBytesQueued;
                            if (bytesQueued > MaximumQueuedByteCount)
                            {
                                framesToSkipForBuffering = frameSkippingStride;

                                // We are about to render a frame but the buffer is too full, so we're going to skip more frames.
                                // Count this frame as a vote to increase the number of frames we skip each time this happens.
                                UpdateFrameSkippingVotes(1);
                            }
                            else if (frameSkippingStride > 1)
                            {
                                // We would be skipping more than one frame, but we just finished a frame without an expanded buffer.
                                // Count this frame as a vote to decrease the number of frames we skip each time this happens.
                                UpdateFrameSkippingVotes(-1);
                            }

                            StateSynchronizationBroadcaster.Instance.OnFrameCompleted();
                        }

                        NetworkConnectionDelta connectionDelta = GetFrameConnectionDelta();

                        // Any GameObjects destroyed since last update should be culled first before attempting to update
                        // components
                        UpdateDestroyedComponents(connectionDelta);

                        using (StateSynchronizationPerformanceMonitor.Instance.MeasureEventDuration(nameof(StateSynchronizationSceneManager), "ResetFrame"))
                        {
                            for (int i = broadcasterComponents.Count - 1; i >= 0; i--)
                            {
                                broadcasterComponents[i].ResetFrame();
                            }
                        }

                        if (connectionDelta.HasConnections)
                        {
                            if (GlobalShaderPropertiesBroadcaster.IsInitialized && GlobalShaderPropertiesBroadcaster.Instance != null)
                            {
                                GlobalShaderPropertiesBroadcaster.Instance.OnFrameCompleted(connectionDelta);
                            }

                            using (StateSynchronizationPerformanceMonitor.Instance.MeasureEventDuration(nameof(StateSynchronizationSceneManager), "ProcessNewConnections"))
                            {
                                for (int i = 0; i < broadcasterComponents.Count; i++)
                                {
                                    if (!IsComponentDestroyed(broadcasterComponents[i]))
                                    {
                                        broadcasterComponents[i].ProcessNewConnections(connectionDelta);
                                    }
                                }
                            }

                            using (StateSynchronizationPerformanceMonitor.Instance.MeasureEventDuration(nameof(StateSynchronizationSceneManager), "OnFrameCompleted"))
                            {
                                for (int i = 0; i < broadcasterComponents.Count; i++)
                                {
                                    if (!IsComponentDestroyed(broadcasterComponents[i]))
                                    {
                                        broadcasterComponents[i].OnFrameCompleted(connectionDelta);
                                    }
                                }
                            }
                        }

                        // Any components detected as removed should be destroyed and removed this frame.
                        UpdateDestroyedComponents(connectionDelta);

                        ApplyFrameConnectionDelta();
                        CheckForFinalDisconnect();
                    }
                    else
                    {
                        StateSynchronizationPerformanceMonitor.Instance.IncrementEventCount(nameof(StateSynchronizationSceneManager), "FrameSkipped");
                        framesToSkipForBuffering--;

                        int bytesQueued = StateSynchronizationBroadcaster.Instance.OutputBytesQueued;
                        if (bytesQueued <= MaximumQueuedByteCount)
                        {
                            // We're about to skip a frame but the buffer is currently under the capacity threshold for skipping.
                            // Count this frame as a vote to decrease the number of frames we skip each time this happens.
                            UpdateFrameSkippingVotes(-1);
                        }
                    }
                }
            }
        }

        private void UpdateDestroyedComponents(NetworkConnectionDelta connectionDelta)
        {
            using (StateSynchronizationPerformanceMonitor.Instance.MeasureEventDuration(nameof(StateSynchronizationSceneManager), nameof(UpdateDestroyedComponents)))
            {
                for (int i = broadcasterComponents.Count - 1; i >= 0; i--)
                {
                    if (IsComponentDestroyed(broadcasterComponents[i]))
                    {
                        SendComponentDestruction(connectionDelta.ContinuedConnections, broadcasterComponents[i]);
                        broadcasterComponents.RemoveAt(i);
                    }
                }
            }
        }

        private static bool IsComponentDestroyed(IComponentBroadcaster component)
        {
            // We need the MonoBehaviour override of the == operator in order to determine
            // if the component is destroyed, since destroyed components are non-null in the
            // CLR sense.
            MonoBehaviour behavior = (MonoBehaviour)component;
            return behavior == null;
        }

        private void SendComponentDestruction(IEnumerable<INetworkConnection> connections, IComponentBroadcaster component)
        {
            using (MemoryStream memoryStream = new MemoryStream())
            using (BinaryWriter message = new BinaryWriter(memoryStream))
            {
                component.ComponentBroadcasterService.WriteHeader(message, component, ComponentBroadcasterChangeType.Destroyed);
                message.Flush();
                memoryStream.TryGetBuffer(out var buffer);
                Send(connections, buffer.Array, buffer.Offset, buffer.Count);
            }
        }

        private void ApplyFrameConnectionDelta()
        {
            continuedConnections.AddRange(addedConnections);
            addedConnections.Clear();
            removedConnections.Clear();
        }

        private void CheckForFinalDisconnect()
        {
            if (continuedConnections.Count == 0 && BroadcasterComponents.Count > 0)
            {
                foreach (MonoBehaviour component in BroadcasterComponents.OfType<MonoBehaviour>())
                {
                    Destroy(component);
                }
            }
        }

        private NetworkConnectionDelta GetFrameConnectionDelta()
        {
            foreach (INetworkConnection removedConnection in removedConnections)
            {
                continuedConnections.Remove(removedConnection);
            }

            return new NetworkConnectionDelta(addedConnections, removedConnections, continuedConnections);
        }

        private void UpdateFrameSkippingVotes(int voteDelta)
        {
            frameSkippingVoteCounter += voteDelta;
            if (frameSkippingVoteCounter >= FrameVotesUntilChangingFrameSkipCount)
            {
                // We had a frame-skipping election, and the result was that we should skip
                // even more frames each time the buffer is too full to try to alleviate network pressure.
                frameSkippingStride++;
                frameSkippingVoteCounter = 0;
            }
            else if (frameSkippingVoteCounter <= -FrameVotesUntilChangingFrameSkipCount)
            {
                // We had a frame-skipping election, and the result was that we should skip
                // fewer frames each time the buffer is too full to try to alleviate network pressure.
                frameSkippingStride = Math.Max(1, frameSkippingStride - 1);
                frameSkippingVoteCounter = 0;
            }
        }
    }
}
