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
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace DubboNet.DubboService.DataModle.DubboInfo
{
    /*
    dubbo>ls
    PROVIDER:
    com.xxxxx.service.api.WorkOrderProductRemoteService
    com.xxxxx.service.api.UserAccountRemoteService
    com.xxxxx.service.api.PhoneHomeLocationRemoteService
    CONSUMER:
    com.xxx.decision.api.record.MiniAppRecordReadRemoteServicecom.xxx.decision.api.dialoguebot.globalstrategy.TBotGlobalStrategyKnowledgeReadRemote
    //or
    dubbo>ls
    com.xxxxx.pay.api.PayRemoteService
    */
    /// <summary>
    /// 表示 Dubbo Telnet <c>ls</c> 返回的 provider 和 consumer 服务列表。
    /// EN: Represents provider and consumer service lists returned by Dubbo Telnet <c>ls</c>.
    /// </summary>
    public class DubboLsInfo : DubboInfoBase
    {
        public List<string> Providers { get; set; } = new List<string>();
        public List<string> Consumers { get; set; } = new List<string>();

        /// <summary>
        /// 解析 Dubbo Telnet <c>ls</c> 响应。
        /// EN: Parses a Dubbo Telnet <c>ls</c> response.
        /// </summary>
        public static DubboLsInfo GetDubboLsInfo(string source)
        {
            const string LS_NEWLINE = "\r\n";
            DubboLsInfo dubboLsInfo = new DubboLsInfo();
            if (string.IsNullOrEmpty(source)) return null;
            string[] sourceLineArr = source.Split(LS_NEWLINE, StringSplitOptions.RemoveEmptyEntries);
            if (sourceLineArr.Length > 0)
            {
                List<string> activeList = dubboLsInfo.Providers;
                for (int i = 0; i < sourceLineArr.Length; i++)
                {
                    if (sourceLineArr[i].StartsWith("PROVIDER:"))
                    {
                        activeList = dubboLsInfo.Providers;
                        activeList.Clear();
                    }
                    else if (sourceLineArr[i].StartsWith("CONSUMER:"))
                    {
                        activeList = dubboLsInfo.Consumers;
                        activeList.Clear();
                    }
                    else
                    {
                        activeList?.Add(sourceLineArr[i]);
                    }
                }
            }
            return dubboLsInfo;
        }

    }

}
