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

namespace DubboNet.DubboService.DataModle
{
    /// <summary>
    /// 配置单个服务节点执行器的连接、超时与诊断行为。
    /// <para>EN: Configures connection, timeout, and diagnostic behavior for one provider actuator.</para>
    /// </summary>
    public class DubboActuatorSuiteConf
    {
        /// <summary>
        /// 获取或设置最大连接数；对 <see cref="TripleDubboActuatorSuite"/> 表示每个 Provider 的最大 HTTP 连接数，
        /// 对 <see cref="TelnetDubboActuatorSuite"/> 表示 TCP 连接池上限。
        /// <see cref="NativeDubboActuatorSuite"/> 不使用此配置；默认 20。
        /// <para>EN: Gets or sets the maximum connection count. It limits HTTP connections per provider for
        /// <see cref="TripleDubboActuatorSuite"/> and the TCP connection pool for <see cref="TelnetDubboActuatorSuite"/>.
        /// <see cref="NativeDubboActuatorSuite"/> ignores it; the default is 20.</para>
        /// </summary>
        public int MaxConnections { get; set; } = 20;

        /// <summary>
        /// 获取或设置辅助连接的空闲存活时间（秒）；仅对 <see cref="TelnetDubboActuatorSuite"/> 生效，
        /// 0 表示不因空闲关闭。
        /// <para>EN: Gets or sets the idle lifetime of auxiliary connections in seconds. This setting applies only to
        /// <see cref="TelnetDubboActuatorSuite"/>; zero disables idle closing.</para>
        /// </summary>
        public int AssistConnectionAliveTime { get; set; } = 60 * 5;

        /// <summary>
        /// 获取或设置主连接的空闲存活时间（秒）；对 <see cref="TelnetDubboActuatorSuite"/> 表示主 TCP 连接的
        /// 空闲释放时间，对 <see cref="TripleDubboActuatorSuite"/> 表示 HTTP 连接池的空闲连接超时。
        /// <see cref="NativeDubboActuatorSuite"/> 不使用此配置；0 表示不主动释放。
        /// <para>EN: Gets or sets the primary-connection idle lifetime in seconds. It controls the primary TCP connection
        /// for <see cref="TelnetDubboActuatorSuite"/> and pooled HTTP connections for <see cref="TripleDubboActuatorSuite"/>.
        /// <see cref="NativeDubboActuatorSuite"/> ignores it; zero disables proactive release.</para>
        /// </summary>
        public int MasterConnectionAliveTime { get; set; } = 60 * 20;

        /// <summary>
        /// 获取或设置业务请求超时时间（毫秒）；对 Native、Triple 和 Telnet 执行器均生效，默认 10 秒。
        /// <para>EN: Gets or sets the business-request timeout in milliseconds. It applies to Native, Triple,
        /// and Telnet actuators; the default is 10 seconds.</para>
        /// </summary>
        public int DubboRequestTimeout { get; set; } = 10 * 1000;

        /// <summary>
        /// 获取或设置省略服务名时使用的默认 Dubbo 接口名；对 Native、Triple 和 Telnet 执行器生效。
        /// <para>EN: Gets or sets the default Dubbo interface name used when the service name is omitted. It applies to
        /// Native, Triple, and Telnet actuators.</para>
        /// </summary>
        public string DefaultServiceName { get; set; } = null;

        /// <summary>
        /// 获取或设置是否自动刷新节点状态信息；仅对 <see cref="TelnetDubboActuatorSuite"/> 生效。
        /// <para>EN: Gets or sets whether provider status information is refreshed automatically. This setting applies
        /// only to <see cref="TelnetDubboActuatorSuite"/>.</para>
        /// </summary>
        public bool IsAutoUpdateStatusInfo { get; set; } = true;
    }
}
