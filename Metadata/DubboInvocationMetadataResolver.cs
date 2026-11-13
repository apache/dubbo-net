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

using DubboNet.Clients;
using DubboNet.Clients.DataModle;
using DubboNet.DubboService.DataModle;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DubboNet.Metadata
{
    // Turns method definitions into an invocation; it never selects a Provider
    // or owns the business connection. DubboClient keeps dispatch coordination.
    internal static class DubboInvocationMetadataResolver
    {
        internal static async Task<DubboInvocation> CreateAsync(
            DubboMetadataManager metadataManager,
            string serviceName,
            string methodName,
            IReadOnlyList<object> arguments,
            DubboInvocation source = null)
        {
            ArgumentNullException.ThrowIfNull(metadataManager);
            if (string.IsNullOrWhiteSpace(serviceName)
                || string.IsNullOrWhiteSpace(methodName))
            {
                throw new ArgumentException("Can not find the Dubbo service or method name.");
            }

            IReadOnlyList<DubboMethodMetadata> methods;
            try
            {
                methods = await metadataManager.GetServiceMethodsAsync(serviceName)
                    .ConfigureAwait(false);
            }
            catch (DubboMetadataException) when (arguments.Count == 0)
            {
                // A no-argument invocation has one exact parameter signature: the empty array.
                // Keep the missing-node watch registered by the metadata manager, but do not
                // require metadata merely to send a signature that cannot be ambiguous by type.
                return new DubboInvocation(
                    serviceName,
                    methodName,
                    Array.Empty<string>(),
                    arguments)
                {
                    Version = source?.Version,
                    Group = source?.Group,
                    Attachments = source?.Attachments
                };
            }
            catch (DubboMetadataException exception)
            {
                throw new DubboMetadataException(
                    $"{exception.Message} Requested endpoint: '{serviceName}.{methodName}'. " +
                    $"Use QueryAsync(\"{serviceName}.{methodName}\", " +
                    "new[] { \"<exact-java-type>\" }, new object[] { argument }) to retry explicitly.",
                    exception);
            }
            DubboMethodMetadata resolved;
            try
            {
                resolved = DubboMethodMetadataResolver.Resolve(
                    serviceName,
                    methodName,
                    methods,
                    arguments);
            }
            catch (DubboMetadataException centerResolutionException)
                when (methods.Any(method =>
                    method.MetadataSource == DubboMetadataSource.MetadataCenter))
            {
                // A metadata center can be only partially populated during rolling publication or
                // when several provider applications expose the same interface. Supplement an
                // unresolved center result with the same one-time, service-level Telnet probe.
                DubboTelnetMetadataResult telnet =
                    await metadataManager.GetTelnetServiceMetadataAsync(serviceName)
                        .ConfigureAwait(false);
                if (telnet.Methods.Count == 0)
                {
                    throw new DubboMetadataException(
                        $"{centerResolutionException.Message} The metadata-center definition " +
                        $"could not resolve this invocation, and Telnet fallback could not " +
                        $"supplement it: {telnet.FailureReason}. " +
                        $"Use QueryAsync(\"{serviceName}.{methodName}\", " +
                        "new[] { \"<exact-java-type>\" }, new object[] { argument }) to retry explicitly.",
                        centerResolutionException);
                }

                IReadOnlyList<DubboMethodMetadata> combined = methods
                    .Concat(telnet.Methods)
                    .GroupBy(DubboMethodMetadataResolver.SignatureKey, StringComparer.Ordinal)
                    .Select(group => group.First())
                    .ToArray();
                resolved = DubboMethodMetadataResolver.Resolve(
                    serviceName,
                    methodName,
                    combined,
                    arguments);
            }

            return new DubboInvocation(
                serviceName,
                methodName,
                resolved.ParameterTypes,
                arguments)
            {
                Version = source?.Version,
                Group = source?.Group,
                Attachments = source?.Attachments
            };
        }

    }
}
