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

namespace DubboNet.Metadata
{
    /// <summary>
    /// ZooKeeper 中 FullServiceDefinition 元数据路径的固定片段。
    /// <para>EN: Fixed segments of the ZooKeeper FullServiceDefinition location.</para>
    /// </summary>
    internal static class DubboMetadataPaths
    {
        internal const string MetadataNode = "metadata";
        internal const string ProviderSide = "provider";
    }
}
