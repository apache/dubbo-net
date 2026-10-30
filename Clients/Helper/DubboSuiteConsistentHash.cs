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
using System.Globalization;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace DubboNet.Clients.Helper
{
    public class DubboSuiteConsistentHash:ConsistentHash<IPEndPoint>
    {
        public DubboSuiteConsistentHash(int replicate = 100):base(replicate) { }

        public override int AddNodeHash(IPEndPoint node, int replicateIndex)
        {
            // HashCode.Combine uses a process-random seed; a routing ring must not.
            return BetterHash("[" + node.Address + "]:" + node.Port.ToString(CultureInfo.InvariantCulture)
                + "#" + replicateIndex.ToString(CultureInfo.InvariantCulture));
        }

    }
}
