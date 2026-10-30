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

namespace DubboNet.Clients.DataModle
{
    /// <summary>
    /// DubboClient 无法发现 Provider 元数据或无法唯一确定方法签名时抛出的异常。
    /// EN: Exception thrown when DubboClient cannot discover provider metadata or uniquely resolve a method signature.
    /// </summary>
    public class DubboMetadataException : Exception
    {
        /// <summary>
        /// 使用指定错误消息初始化异常。
        /// EN: Initializes the exception with an error message.
        /// </summary>
        /// <param name="message">包含失败原因和恢复建议的消息。EN: Message containing the failure reason and recovery guidance.</param>
        public DubboMetadataException(string message) : base(message)
        {
        }

        /// <summary>
        /// 使用指定错误消息和内部异常初始化异常。
        /// EN: Initializes the exception with an error message and inner exception.
        /// </summary>
        /// <param name="message">包含失败原因和恢复建议的消息。EN: Message containing the failure reason and recovery guidance.</param>
        /// <param name="innerException">导致元数据解析失败的底层异常。EN: Underlying exception that caused metadata resolution to fail.</param>
        public DubboMetadataException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
