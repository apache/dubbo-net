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

namespace DubboNet.Testing;

internal static class EndToEndRegistryLayout
{
    // 与 start-providers.sh 使用相同前缀，供发现、元数据读取和示例调用使用。
    // EN: Match start-providers.sh for discovery, metadata reads, and examples.
    internal static string Group(string version)
    {
        string? prefix = Environment.GetEnvironmentVariable("DUBBONET_E2E_ROOT_PREFIX");
        if (string.IsNullOrEmpty(prefix)) prefix = "dubbo";
        if (prefix.Any(character => character is not (>= 'A' and <= 'Z' or >= 'a' and <= 'z'
                or >= '0' and <= '9' or '-' or '_' or '.')))
            throw new ArgumentException("DUBBONET_E2E_ROOT_PREFIX accepts ASCII letters, digits, dots, underscores, and hyphens.");
        return $"{prefix}-{version}";
    }

    internal static string RootPath(string version) => "/" + Group(version);
}
