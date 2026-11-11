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
using System.Globalization;
using System.Net;

namespace DubboNet.Compatibility
{
    internal static class EndpointCompatibility
    {
        internal static bool TryParse(string text, out IPEndPoint endpoint)
        {
#if NETSTANDARD2_0
            // Match numeric endpoint parsing, never resolve a hostname. An unbracketed
            // IPv6 literal has no port; a port on IPv6 requires [address]:port.
            endpoint = null;
            if (string.IsNullOrEmpty(text)) return false;
            string address = text;
            int port = 0;
            int separator = text.LastIndexOf(':');
            if (text[0] == '[')
            {
                int close = text.LastIndexOf(']');
                if (close < 0) return false;
                address = text.Substring(0, close + 1);
                if (close + 1 < text.Length)
                {
                    if (text[close + 1] != ':' ||
                        !TryPort(text.Substring(close + 2), out port)) return false;
                }
            }
            else if (separator >= 0 && text.IndexOf(':') == separator)
            {
                address = text.Substring(0, separator);
                if (!TryPort(text.Substring(separator + 1), out port)) return false;
            }
            if (!IPAddress.TryParse(address, out IPAddress ip)) return false;
            endpoint = new IPEndPoint(ip, port);
            return true;
#else
            return IPEndPoint.TryParse(text, out endpoint);
#endif
        }
#if NETSTANDARD2_0
        private static bool TryPort(string value, out int port)
        {
            bool parsed = uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out uint number);
            port = (int)number;
            return parsed && number <= IPEndPoint.MaxPort;
        }
#endif
    }
}
