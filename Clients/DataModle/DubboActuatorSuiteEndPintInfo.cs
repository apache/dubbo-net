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

using DubboNet.DubboService;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace DubboNet.Clients.DataModle
{
    /// <summary>
    /// 存储ActuatorSuite（IDubboActuatorSuite）的内部对象（主要用于保持ActuatorSuite的复用）
    /// EN: Internal holder used to share an actuator suite and track its references across services.
    /// </summary>
    internal class DubboActuatorSuiteEndPintInfo
    {
        /// <summary>执行器连接的 Provider 网络端点。EN: Provider network endpoint used by the actuator suite.</summary>
        public IPEndPoint EndPoint { get;internal set; }
        /// <summary>可在多个服务间复用的执行器套件。EN: Actuator suite shared across services.</summary>
        public IDubboActuatorSuite ActuatorSuite { get;internal set; }
        /// <summary>服务驱动及在途调用持有的引用数。EN: References held by service drivers and in-flight invocations.</summary>
        public int ReferenceCount { get; internal set; } = 0;
    }
}
