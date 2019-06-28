// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Ignitor.Rendering;
using Microsoft.JSInterop;

namespace Ignitor.Browser
{
    /// <summary>
    /// Provides mechanisms for dispatching events to components in a <see cref="Renderer"/>.
    /// </summary>
    public static class RendererRegistryEventDispatcher
    {
        /// <summary>
        /// For framework use only.
        /// </summary>
        [JSInvokable(nameof(DispatchEvent))]
        public static Task DispatchEvent(
            BrowserEventDescriptor eventDescriptor, string eventArgsJson)
        {
            var eventArgs = ParseEventArgsJson(eventDescriptor.EventArgsType, eventArgsJson);
            var renderer = RendererRegistry.Current.Find(eventDescriptor.BrowserRendererId);
            return renderer.DispatchEventAsync(eventDescriptor.EventHandlerId, eventArgs);
        }

        private static UIEventArgs ParseEventArgsJson(string eventArgsType, string eventArgsJson)
        {
            switch (eventArgsType)
            {
                case "change":
                    return DeserializeUIEventChangeArgs(eventArgsJson);
                case "clipboard":
                    return Deserialize<UIClipboardEventArgs>(eventArgsJson);
                case "drag":
                    return Deserialize<UIDragEventArgs>(eventArgsJson);
                case "error":
                    return Deserialize<UIErrorEventArgs>(eventArgsJson);
                case "focus":
                    return Deserialize<UIFocusEventArgs>(eventArgsJson);
                case "keyboard":
                    return Deserialize<UIKeyboardEventArgs>(eventArgsJson);
                case "mouse":
                    return Deserialize<UIMouseEventArgs>(eventArgsJson);
                case "pointer":
                    return Deserialize<UIPointerEventArgs>(eventArgsJson);
                case "progress":
                    return Deserialize<UIProgressEventArgs>(eventArgsJson);
                case "touch":
                    return Deserialize<UITouchEventArgs>(eventArgsJson);
                case "unknown":
                    return Deserialize<UIEventArgs>(eventArgsJson);
                case "wheel":
                    return Deserialize<UIWheelEventArgs>(eventArgsJson);
                default:
                     throw new ArgumentException($"Unsupported value '{eventArgsType}'.", nameof(eventArgsType));
            }
        }

        private static T Deserialize<T>(string eventArgsJson)
        {
            return JsonSerializer.Deserialize<T>(eventArgsJson, JsonSerializerOptionsProvider.Options);
        }

        private static UIChangeEventArgs DeserializeUIEventChangeArgs(string eventArgsJson)
        {
            var changeArgs = Deserialize<UIChangeEventArgs>(eventArgsJson);
            var jsonElement = (JsonElement)changeArgs.Value;
            switch (jsonElement.ValueKind)
            {
                case JsonValueKind.Null:
                    changeArgs.Value = null;
                    break;
                case JsonValueKind.String:
                    changeArgs.Value = jsonElement.GetString();
                    break;
                case JsonValueKind.True:
                case JsonValueKind.False:
                    changeArgs.Value = jsonElement.GetBoolean();
                    break;
                default:
                    throw new ArgumentException($"Unsupported {nameof(UIChangeEventArgs)} value {jsonElement}.");
            }
            return changeArgs;
        }

        /// <summary>
        /// For framework use only.
        /// </summary>
        public class BrowserEventDescriptor
        {
            /// <summary>
            /// For framework use only.
            /// </summary>
            public int BrowserRendererId { get; set; }

            /// <summary>
            /// For framework use only.
            /// </summary>
            public int EventHandlerId { get; set; }

            /// <summary>
            /// For framework use only.
            /// </summary>
            public string EventArgsType { get; set; }
        }
    }
}
