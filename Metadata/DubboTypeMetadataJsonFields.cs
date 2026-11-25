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

namespace DubboNet.Metadata
{
    /// <summary>
    /// FullServiceDefinition 类型字段，由 JSON 特性、读取器和写入器共享。
    /// <para>EN: FullServiceDefinition type fields shared by JSON attributes, the reader and the writer.</para>
    /// </summary>
    internal static class DubboTypeMetadataJsonFields
    {
        internal const string Id = "id";
        internal const string Type = "type";
        internal const string Items = "items";
        internal const string EnumValuesField = "enum";
        internal const string Enums = "enums";
        internal const string Reference = "$ref";
        internal const string Properties = "properties";
        internal const string TypeBuilderName = "typeBuilderName";
    }
}
