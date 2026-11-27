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
using System.Text;

namespace DubboNet.DubboService.DataModle
{
    internal static class JavaTypeName
    {
        /// <summary>
        /// 移除源码式泛型实参，保留用于 JVM 方法匹配的类名及数组后缀。
        /// EN: Erases source-style generic arguments while preserving class names and array suffixes for JVM method lookup.
        /// </summary>
        internal static string EraseGenericArguments(string declaration)
        {
            if (string.IsNullOrWhiteSpace(declaration))
                return null;

            string typeName = declaration.Trim();
            if (typeName.IndexOf('<') < 0 && typeName.IndexOf('>') < 0)
                return typeName;

            var erased = new StringBuilder(typeName.Length);
            int genericDepth = 0;
            foreach (char character in typeName)
            {
                if (character == '<')
                    genericDepth++;
                else if (character == '>')
                    genericDepth = Math.Max(0, genericDepth - 1);
                else if (genericDepth == 0)
                    erased.Append(character);
            }
            return erased.ToString().Trim();
        }
    }
}
