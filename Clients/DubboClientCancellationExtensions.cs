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

using DubboNet.DubboService.DataModle;
using System.Threading;
using System.Threading.Tasks;

namespace DubboNet.Clients
{
    /// <summary>
    /// 为结构化调用提供取消及完整调用期限。
    /// EN: Adds caller cancellation and a whole-call deadline to structured invocations.
    /// </summary>
    /// <remarks>
    /// 扩展方法让既有实例重载继续优先，例如 <c>QueryAsync(null, default)</c>。
    /// EN: Extension methods preserve existing instance overload resolution, including <c>QueryAsync(null, default)</c>.
    /// </remarks>
    public static class DubboClientCancellationExtensions
    {
        /// <summary>
        /// 使用取消令牌及客户端的完整调用期限执行请求，期限包含元数据和注册发现等待。
        /// EN: Invokes with caller cancellation and a whole-call deadline, including metadata and discovery waits.
        /// </summary>
        /// <param name="client">Dubbo 客户端。EN: The Dubbo client.</param>
        /// <param name="invocation">调用描述。EN: Invocation descriptor.</param>
        /// <param name="cancellationToken">调用方取消令牌。EN: Caller cancellation token.</param>
        /// <returns>调用结果，取消及超时返回失败结果。EN: Invocation result. Cancellation and deadline expiry return a failed result.</returns>
        public static Task<DubboRequestResult> QueryAsync(this DubboClient client,
            DubboInvocation invocation, CancellationToken cancellationToken)
        {
            Guard.ThrowIfNull(client, nameof(client));
            return client.QueryInvocationAsync(invocation, cancellationToken, boundEntireCall: true);
        }

        /// <summary>
        /// 使用取消令牌、完整调用期限和指定的响应模型执行请求。
        /// EN: Invokes with caller cancellation, a whole-call deadline, and a typed response model.
        /// </summary>
        /// <typeparam name="T_Rsp">响应数据类型。EN: Response CLR type.</typeparam>
        /// <param name="client">Dubbo 客户端。EN: The Dubbo client.</param>
        /// <param name="invocation">调用描述。EN: Invocation descriptor.</param>
        /// <param name="cancellationToken">调用方取消令牌。EN: Caller cancellation token.</param>
        /// <returns>强类型调用结果。EN: The typed invocation result.</returns>
        public static async Task<DubboRequestResult<T_Rsp>> QueryAsync<T_Rsp>(this DubboClient client,
            DubboInvocation invocation, CancellationToken cancellationToken)
        {
            var result = await client.QueryAsync(invocation, cancellationToken).ConfigureAwait(false);
            return new DubboRequestResult<T_Rsp>(result);
        }
    }
}
