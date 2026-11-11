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
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;

namespace DubboNet.Testing
{
    // Fail before network access if restore/reference selection picked a different
    // library target than requested. Used by E2E, integration and example programs.
    internal static class LibraryAssetVerification
    {
        internal static void Verify()
        {
            string expected = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(attribute => attribute.Key == "DubboNetExpectedTargetFramework").Value
                ?? throw new InvalidOperationException("Missing expected library target.");
            string framework = expected switch
            {
                "netstandard2.0" => ".NETStandard,Version=v2.0",
                "net8.0" => ".NETCoreApp,Version=v8.0",
                "net10.0" => ".NETCoreApp,Version=v10.0",
                _ => throw new InvalidOperationException("Unsupported test library target: " + expected)
            };
            string actual = typeof(DubboClient).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName
                ?? throw new InvalidOperationException("Missing library TargetFrameworkAttribute.");
            if (actual != framework)
                throw new InvalidOperationException("Wrong DubboNet asset: " + actual + "; expected " + framework);
        }
    }
}
