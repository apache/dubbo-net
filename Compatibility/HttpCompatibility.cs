/*
 * Licensed to the Apache Software Foundation (ASF) under one or more
 * contributor license agreements. See the NOTICE file distributed with
 * this work for additional information regarding copyright ownership.
 * The ASF licenses this file to You under the Apache License, Version 2.0
 * (the "License"); you may not use this file except in compliance with
 * the License. You may obtain a copy of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MyCommonHelper;

namespace DubboNet.Compatibility
{
    internal static class HttpCompatibility
    {
#if NETSTANDARD2_0
        private static readonly System.Reflection.PropertyInfo VersionPolicy =
            typeof(HttpRequestMessage).GetProperty("VersionPolicy");
        private static readonly object ExactVersionPolicy = VersionPolicy == null ? null :
            Enum.Parse(VersionPolicy.PropertyType, "RequestVersionExact");

        // Bind once, avoiding reflection per body read. A Standard DLL hosted by
        // modern .NET must still pass cancellation to custom HttpContent overrides.
        private static readonly Func<HttpContent, CancellationToken, Task<string>> CancellableRead =
            (Func<HttpContent, CancellationToken, Task<string>>)typeof(HttpContent)
                .GetMethod("ReadAsStringAsync", new[] { typeof(CancellationToken) })
                ?.CreateDelegate(typeof(Func<HttpContent, CancellationToken, Task<string>>));
#endif
        internal static HttpMessageHandler CreateHandler(int maximumConnections, TimeSpan idleTimeout)
        {
#if NETSTANDARD2_0
            // Standard is an API contract, not a runtime. Light up the SAME pool
            // implementation when the Standard DLL runs on modern .NET (including
            // net6/net7). Reflection is isolated to handler creation, not the RPC path.
            Type socketsType = typeof(HttpClient).Assembly.GetType("System.Net.Http.SocketsHttpHandler");
            if (socketsType != null && typeof(HttpMessageHandler).IsAssignableFrom(socketsType))
            {
                var handler = (HttpMessageHandler)Activator.CreateInstance(socketsType);
                try
                {
                    socketsType.GetProperty("MaxConnectionsPerServer").SetValue(handler, maximumConnections);
                    socketsType.GetProperty("PooledConnectionIdleTimeout").SetValue(handler, idleTimeout);
                    return handler;
                }
                catch { handler.Dispose(); throw; }
            }
            return CreateLegacyHandler(maximumConnections);
#else
            return new SocketsHttpHandler
            {
                MaxConnectionsPerServer = maximumConnections,
                PooledConnectionIdleTimeout = idleTimeout
            };
#endif
        }

        internal static HttpMessageHandler CreateLegacyHandler(int maximumConnections)
        {
            // Framework has no per-handler idle eviction setting. Do not change
            // process-wide ServicePointManager/TLS settings to emulate one.
            MyLogger.LogWarning("Legacy HTTP handler: Triple pool idle eviction follows the runtime defaults; " +
                "MasterConnectionAliveTime cannot be applied per handler on this runtime.");
            return new HttpClientHandler { MaxConnectionsPerServer = maximumConnections };
        }

        internal static void SetHttp11Exact(HttpRequestMessage request)
        {
            request.Version = HttpVersion.Version11;
#if NETSTANDARD2_0
            // VersionPolicy is absent from Standard; apply it when the host supports
            // it. The legacy handler only has HTTP/1.x, so Version=1.1 is sufficient.
            VersionPolicy?.SetValue(request, ExactVersionPolicy);
#else
            request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
#endif
        }

        internal static async Task<string> ReadAsStringAsync(HttpResponseMessage response, CancellationToken token)
        {
#if NETSTANDARD2_0
            if (CancellableRead != null)
                return await CancellableRead(response.Content, token).ConfigureAwait(false);
            return await ReadLegacyAsStringAsync(response, token).ConfigureAwait(false);
#else
            return await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
#endif
        }

        internal static async Task<string> ReadLegacyAsStringAsync(HttpResponseMessage response, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            // Preserve HttpContent's charset/BOM decoding. The old overload has no
            // token: abort ONLY this response, never the shared HttpClient/pool, and
            // bound the wait even if a custom stream does not implement cancellation.
            using (token.Register(() =>
            {
                try { response.Dispose(); }
                catch (Exception exception)
                {
                    // Even an application-supplied logger/console must not make
                    // CancellationTokenSource.Cancel throw from this callback.
                    try { MyLogger.LogDebug("Could not dispose a cancelled HTTP response.", exception); }
                    catch { }
                }
            }))
            {
                try
                {
                    return await response.Content.ReadAsStringAsync().WaitAsync(token).ConfigureAwait(false);
                }
                catch (Exception exception) when (token.IsCancellationRequested &&
                    !(exception is OperationCanceledException))
                { throw new OperationCanceledException("HTTP response read cancelled.", exception, token); }
            }
        }
    }
}
