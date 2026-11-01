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
    /*   dubbo>ps -l 20882
    /172.16.189.88:40992 -> /172.16.69.118:20882
    /172.16.67.106:55998 -> /172.16.69.118:20882
    /172.16.189.61:41458 -> /172.16.69.118:20882
    /172.16.246.67:59054 -> /172.16.69.118:20882
    /172.16.193.136:59222 -> /172.16.69.118:20882
    */
    /// <summary>
    /// 表示 Dubbo Telnet <c>ps</c> 返回的连接端点对。
    /// EN: Represents connection endpoint pairs returned by Dubbo Telnet <c>ps</c>.
    /// </summary>
    public class DubboPsInfo : DubboInfoBase
    {
        public List<KeyValuePair<IPEndPoint, IPEndPoint>> Lines { get; set; } = new List<KeyValuePair<IPEndPoint, IPEndPoint>>();

        /// <summary>
        /// 解析 Dubbo Telnet <c>ps</c> 响应。
        /// EN: Parses a Dubbo Telnet <c>ps</c> response.
        /// </summary>
        public static DubboPsInfo GetDubboPsInfo(string source)
        {
            const string IP_START = "/";
            const string IP_SPIT = " -> /";
            const string IP_NEWLINE = "\r\n";

            DubboPsInfo dubboPsInfo = new DubboPsInfo();
            if (string.IsNullOrEmpty(source)) return null;
            string[] sourceLineArr = source.Split(IP_NEWLINE, StringSplitOptions.RemoveEmptyEntries);
            foreach (string oneLine in sourceLineArr)
            {
                if (oneLine.StartsWith(IP_START))
                {
                    int tempEnd = oneLine.IndexOf(IP_SPIT);
                    string epFrom = oneLine.Substring(1, tempEnd - 1);
                    string epTo = oneLine.Substring(tempEnd + IP_SPIT.Length);
                    IPEndPoint iPEndPointFrom, iPEndPointTo = null;
                    if (IPEndPoint.TryParse(epFrom, out iPEndPointFrom) && IPEndPoint.TryParse(epTo, out iPEndPointTo))
                    {
                        dubboPsInfo.Lines.Add(new KeyValuePair<IPEndPoint, IPEndPoint>(iPEndPointFrom, iPEndPointTo));
                    }
                }
            }
            return dubboPsInfo;
        }
    }

}
