﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Proxy
{
    internal static class ProxyAdvancedExtensions
    {
        private const int StreamCopyBufferSize = 81920;

        public static HttpRequestMessage CreateProxyHttpRequest(this HttpContext context, Uri uri)
        {
            var request = context.Request;

            var proxiedMessage = new HttpRequestMessage();
            var hasContent = SetupMethodAndContent(request, proxiedMessage);

            // Copy the request headers
            foreach (var header in request.Headers)
            {
                var headerValue = header.Value;
                if (headerValue.Count == 1)
                {
                    var value = headerValue.ToString();
                    if (!proxiedMessage.Headers.TryAddWithoutValidation(header.Key, value) && hasContent)
                    {
                        proxiedMessage.Content.Headers.TryAddWithoutValidation(header.Key, value);
                    }
                }
                else
                {
                    var value = headerValue.ToArray();
                    if (!proxiedMessage.Headers.TryAddWithoutValidation(header.Key, value) && hasContent)
                    {
                        proxiedMessage.Content.Headers.TryAddWithoutValidation(header.Key, value);
                    }
                }
            }

            proxiedMessage.Headers.Host = uri.Authority;
            proxiedMessage.RequestUri = uri;

            return proxiedMessage;
        }
        static bool SetupMethodAndContent(HttpRequest request, HttpRequestMessage proxiedMessage)
        {
            var hasContent = false;
            var requestMethod = request.Method;

            // Try to use the static HttpMethods rather than creating a new one.
            if (HttpMethods.IsGet(requestMethod))
            {
                proxiedMessage.Method = HttpMethod.Get;
            }
            else if (HttpMethods.IsHead(requestMethod))
            {
                proxiedMessage.Method = HttpMethod.Head;
            }
            else if (HttpMethods.IsDelete(requestMethod))
            {
                proxiedMessage.Method = HttpMethod.Delete;
            }
            else if (HttpMethods.IsTrace(requestMethod))
            {
                proxiedMessage.Method = HttpMethod.Trace;
            }
            else
            {
                hasContent = true;

                if (HttpMethods.IsPost(requestMethod))
                {
                    proxiedMessage.Method = HttpMethod.Post;
                }
                else if (HttpMethods.IsOptions(requestMethod))
                {
                    proxiedMessage.Method = HttpMethod.Options;
                }
                else if (HttpMethods.IsPut(requestMethod))
                {
                    proxiedMessage.Method = HttpMethod.Put;
                }
                else if (HttpMethods.IsPatch(requestMethod))
                {
                    proxiedMessage.Method = HttpMethod.Patch;
                }
                else
                {
                    proxiedMessage.Method = new HttpMethod(request.Method);
                }

                proxiedMessage.Content = new StreamContent(request.Body);
            }

            return hasContent;
        }

        public static async Task CopyProxyHttpResponse(this HttpContext context, HttpResponseMessage replyMessage)
        {
            if (replyMessage == null)
            {
                throw new ArgumentNullException(nameof(replyMessage));
            }

            var response = context.Response;
            response.StatusCode = (int)replyMessage.StatusCode;

            var responseHeaders = response.Headers;

            CopyHeaders(responseHeaders, replyMessage.Headers);
            CopyHeaders(responseHeaders, replyMessage.Content.Headers);

            // SendAsync removes chunking from the response. This removes the header so it doesn't expect a chunked response.
            responseHeaders.Remove(HeaderNames.TransferEncoding);

            using (var responseStream = await replyMessage.Content.ReadAsStreamAsync())
            {
                await responseStream.CopyToAsync(response.Body, StreamCopyBufferSize, context.RequestAborted);
            }
        }

        static void CopyHeaders(IHeaderDictionary responseHeaders, HttpHeaders replyHeaders)
        {
            foreach (var replyHeader in replyHeaders.NonValidated)
            {
                var replyValue = replyHeader.Value;

                StringValues headerValue = replyValue.Count <= 1
                    ? replyValue.ToString()
                    : ToArray(replyValue);

                responseHeaders[replyHeader.Key] = headerValue;
            }

            static StringValues ToArray(in HeaderStringValues values)
            {
                var array = new string[values.Count];
                var i = 0;
                foreach (var value in values)
                {
                    array[i++] = value;
                }
                return array;
            }
        }
    }
}
