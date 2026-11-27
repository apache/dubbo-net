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
using DubboNet.DubboService.Native;
using Google.Protobuf;
using NHessian.IO;
using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace DubboNet.DubboService.Triple
{
    // The fixed Apache triple_wrapper.proto schema, not a user Protobuf/IDL contract.
    // Use the maintained Protobuf wire reader/writer rather than a second varint parser.
    internal static class Hessian2WrapperCodec
    {
        internal const string Method = "$invoke";
        private static readonly string[] WrapperTypes =
            { "java.lang.String", "[Ljava.lang.String;", "[Ljava.lang.Object;" };

        internal static byte[] EncodeRequest(DubboInvocation invocation, int limit, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            invocation.Validate();
            if (invocation.Arguments.Count > 1024)
                throw new ArgumentException("Binary Triple supports at most 1024 Java method parameters.");
            // FullServiceDefinition may include source-style generic arguments.
            // Java resolves $invoke using erased classes. Use those same types
            // for value conversion so a generic Map is not tagged as a POJO.
            string[] parameterTypes = invocation.ParameterTypes.Select(JavaTypeName.EraseGenericArguments).ToArray();
            object[] generic =
            {
                invocation.Method,
                parameterTypes,
                NativeDubboCodec.PrepareGenericArguments(invocation.Arguments, parameterTypes, token, boundTraversal: true)
            };
            using var wrapper = new PayloadBuffer(limit, token);
            using (var output = new CodedOutputStream(wrapper, true))
            {
                // Dubbo's wrapper name hessian4 is the wire alias for Hessian2.
                output.WriteTag(1, WireFormat.WireType.LengthDelimited);
                output.WriteString("hessian4");
                foreach (object value in generic)
                {
                    // Each args[i] is an independent Hessian stream/reference table.
                    // A classic Dubbo frame/$invoke request body is NOT a wrapper arg.
                    using var bytes = new PayloadBuffer(limit, token);
                    using (var writer = new HessianStreamWriter(bytes, true))
                        new HessianOutputV2(writer, TypeBindings.Java).WriteObject(value);
                    output.WriteTag(2, WireFormat.WireType.LengthDelimited);
                    // Copy only the written range straight into ByteString;
                    // ToArray would allocate and copy the whole argument again.
                    output.WriteBytes(ByteString.CopyFrom(bytes.GetBuffer(), 0, checked((int)bytes.Length)));
                }
                foreach (string type in WrapperTypes)
                {
                    output.WriteTag(3, WireFormat.WireType.LengthDelimited);
                    output.WriteString(type);
                }
            }
            token.ThrowIfCancellationRequested();
            return wrapper.ToArray();
        }

        internal static object DecodeResponse(byte[] payload)
        {
            using var input = new CodedInputStream(payload);
            string serialization = null;
            byte[] value = null;
            bool hasType = false;
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                switch (tag)
                {
                    case 10:
                        if (serialization != null) throw new InvalidDataException("Duplicate Triple serializeType.");
                        serialization = input.ReadString();
                        break;
                    case 18:
                        if (value != null) throw new InvalidDataException("Duplicate Triple response data.");
                        value = input.ReadBytes().ToByteArray();
                        break;
                    case 26:
                        if (hasType) throw new InvalidDataException("Duplicate Triple response type.");
                        _ = input.ReadString(); // Type is descriptive; never load a CLR type from the wire.
                        hasType = true;
                        break;
                    default:
                        input.SkipLastField();
                        break;
                }
            }
            if (serialization != "hessian4" && serialization != "hessian2")
                throw new NotSupportedException($"Triple wrapper serialization '{serialization}' is not supported; expected Hessian2.");
            if (value == null || value.Length == 0)
                throw new InvalidDataException("Triple response wrapper has no Hessian2 value.");
            return NativeDubboCodec.DecodeStandaloneHessianValue(value);
        }
    }

    // Bound the request WHILE writing, not only after MemoryStream has grown.
    internal sealed class PayloadBuffer : MemoryStream
    {
        private readonly int _limit;
        private readonly CancellationToken _token;
        internal PayloadBuffer(int limit, CancellationToken token = default) { _limit = limit; _token = token; }
        private void Check(int count)
        {
            _token.ThrowIfCancellationRequested();
            if (count < 0 || Position > _limit - (long)count)
                throw new InvalidDataException($"Binary Triple message exceeds the {_limit}-byte limit.");
        }
        public override void Write(byte[] buffer, int offset, int count)
        { Check(count); base.Write(buffer, offset, count); }
        public override void WriteByte(byte value) { Check(1); base.WriteByte(value); }
#if !NETSTANDARD2_0
        public override void Write(ReadOnlySpan<byte> buffer)
        { Check(buffer.Length); base.Write(buffer); }
#endif
    }
}
