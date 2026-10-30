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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DubboNet.DubboService.DataModle.DubboInfo
{
    /// <summary>
    /// Dubbo Telnet 诊断信息的公共节点和创建时间基类。
    /// EN: Base type for Dubbo Telnet diagnostics that records the provider endpoint and creation time.
    /// </summary>
    public class DubboInfoBase
    {
        /// <summary>
        /// 信息创建时间
        /// EN: Gets the time when this diagnostic snapshot was created.
        /// </summary>
        public DateTime InfoCreatTime { get; } = DateTime.Now;
        /// <summary>
        /// 服务Host地址
        /// EN: Gets the provider host address.
        /// </summary>
        public string DubboHost { get; protected set; }
        /// <summary>
        /// 服务Port端口
        /// EN: Gets the provider port.
        /// </summary>
        public int DubboPort { get; protected set; }

        /// <summary>
        /// 从执行器复制 provider 地址和端口。
        /// EN: Copies the provider host and port from an actuator.
        /// </summary>
        /// <param name="dubboActuator">Dubbo 执行器。EN: Dubbo actuator.</param>
        public void SetDubboActuatorInfo(DubboActuator dubboActuator)
        {
            DubboHost = dubboActuator.DubboHost;
            DubboPort = dubboActuator.DubboPort;
        }
    }
}
