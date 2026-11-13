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
    /// Metadata 默认值与 Nacos Naming、ZooKeeper 注册目录默认值分别管理。
    /// <para>EN: Metadata defaults are independent of Nacos Naming and ZooKeeper registry defaults.</para>
    /// </summary>
    internal static class DubboMetadataDefaults
    {
        internal const string NacosGroup = "dubbo";
        // Metadata hangs under the ZooKeeper registry root, including when Naming uses Nacos.
        // Keep this independently owned default in sync with ZooKeeperRegistryPaths.DefaultRoot.
        internal const string ZooKeeperRootPath = "/dubbo";
    }
}
