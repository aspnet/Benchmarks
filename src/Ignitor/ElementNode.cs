// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Ignitor;
using Ignitor.Browser;
using Microsoft.AspNetCore.SignalR.Client;

namespace Ignitor
{
    internal class ElementNode : ContainerNode
    {
        private readonly Dictionary<string, object> _attributes;
        private readonly Dictionary<string, object> _properties;
        private readonly Dictionary<string, ElementEventDescriptor> _events;

        public ElementNode(string tagName)
        {
            TagName = tagName ?? throw new ArgumentNullException(nameof(tagName));
            _attributes = new Dictionary<string, object>(StringComparer.Ordinal);
            _properties = new Dictionary<string, object>(StringComparer.Ordinal);
            _events = new Dictionary<string, ElementEventDescriptor>(StringComparer.Ordinal);
        }
        public string TagName { get; }

        public IReadOnlyDictionary<string, object> Attributes => _attributes;

        public IReadOnlyDictionary<string, object> Properties => _properties;

        public IReadOnlyDictionary<string, ElementEventDescriptor> Events => _events;

        public void SetAttribute(string key, object value)
        {
            _attributes[key] = value;
        }

        public void RemoveAttribute(string key)
        {
            _attributes.Remove(key);
        }

        public void SetProperty(string key, object value)
        {
            _properties[key] = value;
        }

        public void SetEvent(string eventName, ElementEventDescriptor descriptor)
        {
            if (eventName is null)
            {
                throw new ArgumentNullException(nameof(eventName));
            }

            if (descriptor is null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }

            _events[eventName] = descriptor;
        }

        public class ElementEventDescriptor
        {
            public ElementEventDescriptor(string eventName, int eventId)
            {
                EventName = eventName ?? throw new ArgumentNullException(nameof(eventName));
                EventId = eventId;
            }

            public string EventName { get; }

            public int EventId { get; }
        }

        public async Task ClickAsync(HubConnection connection, CancellationToken cancellationToken)
        {
            if (!Events.TryGetValue("click", out var clickEventDescriptor))
            {
                throw new ArgumentException("Element cannot be clicked");
            }

            var mouseEventArgs = new UIMouseEventArgs()
            {
                Type = clickEventDescriptor.EventName,
                Detail = 1
            };
            var browserDescriptor = new RendererRegistryEventDispatcher.BrowserEventDescriptor()
            {
                BrowserRendererId = 0,
                EventHandlerId = clickEventDescriptor.EventId,
                EventArgsType = "mouse",
            };
            var serializedJson = JsonSerializer.Serialize(mouseEventArgs, new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var argsObject = new object[] { browserDescriptor, serializedJson };
            var callId = "0";
            var assemblyName = "Microsoft.AspNetCore.Components.Browser";
            var methodIdentifier = "DispatchEvent";
            var dotNetObjectId = 0;
            var clickArgs = JsonSerializer.Serialize(argsObject, new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            await connection.InvokeAsync("BeginInvokeDotNetFromJS", callId, assemblyName, methodIdentifier, dotNetObjectId, clickArgs, cancellationToken);
        }
    }
}
