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

namespace DubboNet.DubboService
{
    /// <summary>
    /// Provider 注册 URI 的协议 scheme，由发现与执行器选择共享；不用于 Nacos group 或消费者协议版本。
    /// <para>EN: Provider URI schemes shared by discovery and suite selection, not registry groups or consumer versions.</para>
    /// </summary>
    internal static class ProviderProtocolSchemes
    {
        internal const string Dubbo = "dubbo";
        internal const string Tri = "tri";
    }
}
