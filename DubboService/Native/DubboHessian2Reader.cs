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
using System.IO;
using System.Text;
using DubboNet.DubboService.Native.Hessian2;

namespace DubboNet.DubboService.Native
{
    /// <summary>
    /// 将 Hessian2 值读取为动态 .NET 列表和字典，用于 CLR 返回类型未知的 Dubbo 泛化响应。
    /// <para>EN: Reads Hessian2 values into dynamic .NET lists and dictionaries for generic Dubbo responses whose CLR result type is unknown.</para>
    /// </summary>
    internal sealed class DubboHessian2Reader
    {
        // Wire nesting and JSON reference-expansion depth are independently bounded
        // by the same limit; chunk sequences are concatenation, not nesting.
        internal const int MaxNestingDepth = 64;
        private int _depth;
        private sealed class ClassDefinition
        {
            public string TypeName { get; set; }
            public string[] Fields { get; set; }
        }

        private readonly Hessian2ByteCursor _byteCursor;
        private readonly List<object> _references = new List<object>();
        private readonly List<string> _typeReferences = new List<string>();
        private readonly List<ClassDefinition> _classDefinitions = new List<ClassDefinition>();

        /// <summary>
        /// 创建读取指定 Hessian2 数据的解析器。
        /// <para>EN: Creates a reader for the supplied Hessian2 payload.</para>
        /// </summary>
        public DubboHessian2Reader(byte[] buffer)
        {
            _byteCursor = new Hessian2ByteCursor(buffer);
        }

        /// <summary>
        /// 读取一个 Hessian2 整数。
        /// <para>EN: Reads one Hessian2 integer.</para>
        /// </summary>
        public int ReadInt()
        {
            return ReadInt(_byteCursor.ReadByte());
        }

        /// <summary>
        /// 读取一个 Hessian2 字符串。
        /// <para>EN: Reads one Hessian2 string.</para>
        /// </summary>
        public string ReadString()
        {
            return ReadString(_byteCursor.ReadByte());
        }

        /// <summary>
        /// 读取下一个动态 Hessian2 值。
        /// <para>EN: Reads the next dynamic Hessian2 value.</para>
        /// </summary>
        public object ReadValue()
        {
            if (_depth >= MaxNestingDepth)
                throw new InvalidDataException($"Hessian2 wire nesting exceeds the limit of {MaxNestingDepth} levels.");
            _depth++;
            try { return ReadValueCore(); }
            finally { _depth--; }
        }

        private object ReadValueCore()
        {
            byte tag = _byteCursor.ReadByte();
            if (tag == Hessian2Tags.Null) return null;
            if (tag == Hessian2Tags.True) return true;
            if (tag == Hessian2Tags.False) return false;

            if (IsIntTag(tag)) return ReadInt(tag);
            if (IsLongTag(tag)) return ReadLong(tag);
            if (IsDoubleTag(tag)) return ReadDouble(tag);

            if (tag == Hessian2Tags.DateMilliseconds)
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(_byteCursor.ReadInt64()).UtcDateTime;
            }
            if (tag == Hessian2Tags.DateMinutes)
            {
                return DateTimeOffset.FromUnixTimeSeconds((long)_byteCursor.ReadInt32() * 60).UtcDateTime;
            }

            if (IsStringTag(tag)) return ReadString(tag);
            if (IsBinaryTag(tag)) return ReadBinary(tag);

            if (IsListTag(tag)) return ReadList(tag);
            if (tag == Hessian2Tags.ClassDefinition) return ReadClassDefinitionAndValue();
            if (IsMapOrObjectTag(tag)) return ReadMapOrObject(tag);

            if (tag == Hessian2Tags.Reference)
            {
                int reference = ReadInt();
                if (reference < 0 || reference >= _references.Count)
                {
                    throw new InvalidDataException($"Invalid Hessian2 reference index {reference}.");
                }
                return _references[reference];
            }

            throw new InvalidDataException($"Unsupported Hessian2 tag 0x{tag:x2}.");
        }

        private object ReadClassDefinitionAndValue()
        {
            string typeName = ReadString();
            int fieldCount = ReadInt();
            if (fieldCount < 0)
            {
                throw new InvalidDataException($"Invalid Hessian2 class field count {fieldCount}.");
            }

            EnsureCountFitsPayload(fieldCount);
            string[] fields = new string[fieldCount];
            for (int i = 0; i < fields.Length; i++)
            {
                fields[i] = ReadString();
            }
            _classDefinitions.Add(new ClassDefinition
            {
                TypeName = typeName,
                Fields = fields
            });
            return ReadValue();
        }

        private object ReadMapOrObject(byte tag)
        {
            if (tag == Hessian2Tags.UntypedMap || tag == Hessian2Tags.TypedMap)
            {
                string typeName = tag == Hessian2Tags.TypedMap ? ReadType() : null;
                Dictionary<object, object> map = NewMap(typeName);
                _references.Add(map);
                while (_byteCursor.PeekByte() != Hessian2Tags.End)
                {
                    object key = ReadValue();
                    object value = ReadValue();
                    map[key ?? "null"] = value;
                }
                _ = _byteCursor.ReadByte();
                return map;
            }

            int definitionIndex = tag == Hessian2Tags.Object ? ReadInt() : tag - Hessian2Tags.CompactObjectFirstTag;
            if (definitionIndex < 0 || definitionIndex >= _classDefinitions.Count)
            {
                throw new InvalidDataException(
                    $"Invalid Hessian2 class definition index {definitionIndex}.");
            }

            ClassDefinition definition = _classDefinitions[definitionIndex];
            Dictionary<object, object> instance = NewMap(definition.TypeName);
            _references.Add(instance);
            foreach (string field in definition.Fields)
            {
                instance[field] = ReadValue();
            }
            return instance;
        }

        private static Dictionary<object, object> NewMap(string typeName)
        {
            Dictionary<object, object> result = new Dictionary<object, object>();
            if (!string.IsNullOrWhiteSpace(typeName))
            {
                result["class"] = typeName;
            }
            return result;
        }

        private object ReadList(byte tag)
        {
            int? length = ReadListHeader(tag);
            if (length is int count)
            {
                if (count < 0)
                {
                    throw new InvalidDataException($"Invalid Hessian2 list length {count}.");
                }
                EnsureCountFitsPayload(count);
            }

            List<object> list = new List<object>(length ?? 0);
            _references.Add(list); // References may point to this list before it is complete.
            ReadListElements(list, length);
            return list;
        }

        // Null means the list ends with 'Z'; a value is its fixed element count.
        private int? ReadListHeader(byte tag)
        {
            switch (tag)
            {
                case Hessian2Tags.TypedVariableList:
                    _ = ReadType();
                    return null;
                case Hessian2Tags.TypedFixedList:
                    _ = ReadType();
                    return ReadInt();
                case Hessian2Tags.UntypedVariableList:
                    return null;
                case Hessian2Tags.UntypedFixedList:
                    return ReadInt();
                default:
                    if (tag >= Hessian2Tags.CompactTypedListFirstTag && tag <= Hessian2Tags.CompactTypedListLastTag)
                    {
                        int length = tag - Hessian2Tags.CompactTypedListFirstTag;
                        _ = ReadType();
                        return length;
                    }
                    return tag - Hessian2Tags.CompactUntypedListFirstTag;
            }
        }

        private void ReadListElements(List<object> list, int? length)
        {
            if (length is int count)
            {
                for (int i = 0; i < count; i++)
                {
                    list.Add(ReadValue());
                }
                return;
            }

            while (_byteCursor.PeekByte() != Hessian2Tags.End)
            {
                list.Add(ReadValue());
            }
            _ = _byteCursor.ReadByte();
        }

        private void EnsureCountFitsPayload(int count)
        {
            // Each declared value or field name needs at least one wire byte.
            int remaining = _byteCursor.Remaining;
            if (count > remaining)
            {
                throw new InvalidDataException(
                    $"Hessian2 declared count {count} exceeds the {remaining} remaining payload bytes.");
            }
        }

        private string ReadType()
        {
            byte tag = _byteCursor.PeekByte();
            if (IsIntTag(tag))
            {
                int index = ReadInt();
                if (index < 0 || index >= _typeReferences.Count)
                {
                    throw new InvalidDataException($"Invalid Hessian2 type reference index {index}.");
                }
                return _typeReferences[index];
            }

            string typeName = ReadString();
            _typeReferences.Add(typeName);
            return typeName;
        }

        // Integer tags are biased around a tag that represents zero. The tag supplies
        // the signed high part; the following bytes supply the low part.
        private int ReadSignedCompactWithOneByte(byte tag, byte zeroTag)
        {
            int signedHighPart = tag - zeroTag;
            int lowByte = _byteCursor.ReadByte();
            return (signedHighPart << 8) + lowByte;
        }

        private int ReadSignedCompactWithTwoBytes(byte tag, byte zeroTag)
        {
            int signedHighPart = tag - zeroTag;
            int middleByte = _byteCursor.ReadByte();
            int lowByte = _byteCursor.ReadByte();
            return (signedHighPart << 16) + (middleByte << 8) + lowByte;
        }

        // String and binary length tags start at zero; the next byte is the low part.
        private int ReadCompactLengthWithOneByte(byte tag, byte firstTag)
        {
            int highLengthByte = tag - firstTag;
            int lowLengthByte = _byteCursor.ReadByte();
            return (highLengthByte << 8) + lowLengthByte;
        }

        private int ReadInt(byte tag)
        {
            if (tag >= Hessian2Tags.CompactIntFirstTag && tag <= Hessian2Tags.CompactIntLastTag)
            {
                return tag - Hessian2Tags.CompactIntZeroTag;
            }
            if (tag >= Hessian2Tags.CompactIntTwoByteFirstTag && tag <= Hessian2Tags.CompactIntTwoByteLastTag)
            {
                return ReadSignedCompactWithOneByte(tag, Hessian2Tags.CompactIntTwoByteZeroTag);
            }
            if (tag >= Hessian2Tags.CompactIntThreeByteFirstTag && tag <= Hessian2Tags.CompactIntThreeByteLastTag)
            {
                return ReadSignedCompactWithTwoBytes(tag, Hessian2Tags.CompactIntThreeByteZeroTag);
            }
            if (tag == Hessian2Tags.Int)
            {
                return _byteCursor.ReadInt32();
            }
            throw new InvalidDataException($"Hessian2 tag 0x{tag:x2} is not an integer.");
        }

        private long ReadLong(byte tag)
        {
            if (tag >= Hessian2Tags.CompactLongFirstTag && tag <= Hessian2Tags.CompactLongLastTag)
            {
                return tag - Hessian2Tags.CompactLongZeroTag;
            }
            if (tag >= Hessian2Tags.CompactLongTwoByteFirstTag && tag <= Hessian2Tags.CompactLongTwoByteLastTag)
            {
                return ReadSignedCompactWithOneByte(tag, Hessian2Tags.CompactLongTwoByteZeroTag);
            }
            if (tag >= Hessian2Tags.CompactLongThreeByteFirstTag && tag <= Hessian2Tags.CompactLongThreeByteLastTag)
            {
                return ReadSignedCompactWithTwoBytes(tag, Hessian2Tags.CompactLongThreeByteZeroTag);
            }
            if (tag == Hessian2Tags.LongAsInt)
            {
                return _byteCursor.ReadInt32();
            }
            if (tag == Hessian2Tags.Long)
            {
                return _byteCursor.ReadInt64();
            }
            throw new InvalidDataException($"Hessian2 tag 0x{tag:x2} is not a long.");
        }

        private double ReadDouble(byte tag)
        {
            switch (tag)
            {
                case Hessian2Tags.DoubleZero:
                    return 0d;
                case Hessian2Tags.DoubleOne:
                    return 1d;
                case Hessian2Tags.DoubleAsByte:
                    return (sbyte)_byteCursor.ReadByte();
                case Hessian2Tags.DoubleAsShort:
                    return unchecked((short)_byteCursor.ReadUInt16());
                case Hessian2Tags.DoubleAsThousandths:
                    return _byteCursor.ReadInt32() * 0.001d;
                case Hessian2Tags.Double:
                    return BitConverter.Int64BitsToDouble(_byteCursor.ReadInt64());
                default:
                    throw new InvalidDataException($"Hessian2 tag 0x{tag:x2} is not a double.");
            }
        }

        private string ReadString(byte tag)
        {
            StringBuilder chunks = null;
            while (tag == Hessian2Tags.StringChunk)
            {
                chunks ??= new StringBuilder();
                chunks.Append(ReadUtf8Chars(_byteCursor.ReadUInt16()));
                tag = _byteCursor.ReadByte();
            }

            string last;
            if (tag == Hessian2Tags.Null) last = null;
            else if (tag <= Hessian2Tags.ShortStringLastTag) last = ReadUtf8Chars(tag);
            else if (tag >= Hessian2Tags.CompactStringFirstTag && tag <= Hessian2Tags.CompactStringLastTag)
                last = ReadUtf8Chars(ReadCompactLengthWithOneByte(tag, Hessian2Tags.CompactStringFirstTag));
            else if (tag == Hessian2Tags.String) last = ReadUtf8Chars(_byteCursor.ReadUInt16());
            else throw new InvalidDataException($"Hessian2 tag 0x{tag:x2} is not a string.");
            return chunks == null ? last : chunks.Append(last).ToString();
        }

        private string ReadUtf8Chars(int charCount)
        {
            StringBuilder builder = new StringBuilder(charCount);
            int charsRead = 0;
            while (charsRead < charCount)
            {
                byte first = _byteCursor.ReadByte();
                if (first < 0x80)
                {
                    builder.Append((char)first);
                    charsRead++;
                }
                else if ((first & 0xe0) == 0xc0)
                {
                    int code = ((first & 0x1f) << 6) | ReadContinuationByte();
                    builder.Append((char)code);
                    charsRead++;
                }
                else if ((first & 0xf0) == 0xe0)
                {
                    int code = ((first & 0x0f) << 12)
                        | (ReadContinuationByte() << 6)
                        | ReadContinuationByte();
                    builder.Append((char)code);
                    charsRead++;
                }
                else if ((first & 0xf8) == 0xf0)
                {
                    int scalar = ((first & 0x07) << 18)
                        | (ReadContinuationByte() << 12)
                        | (ReadContinuationByte() << 6)
                        | ReadContinuationByte();
                    builder.Append(char.ConvertFromUtf32(scalar));
                    charsRead += 2;
                }
                else
                {
                    throw new InvalidDataException($"Invalid Hessian2 UTF-8 lead byte 0x{first:x2}.");
                }
            }

            if (charsRead != charCount)
            {
                throw new InvalidDataException("Hessian2 string length split a Unicode surrogate pair.");
            }
            return builder.ToString();
        }

        private int ReadContinuationByte()
        {
            byte value = _byteCursor.ReadByte();
            if ((value & 0xc0) != 0x80)
            {
                throw new InvalidDataException($"Invalid Hessian2 UTF-8 continuation byte 0x{value:x2}.");
            }
            return value & 0x3f;
        }

        private byte[] ReadBinary(byte tag)
        {
            if (tag == Hessian2Tags.BinaryChunk)
            {
                // Append directly from the bounded input buffer. No recursion or
                // repeated full-prefix copies, even for many empty chunks.
                using MemoryStream chunks = new MemoryStream();
                do
                {
                    int length = _byteCursor.ReadUInt16();
                    _byteCursor.CopyTo(chunks, length);
                    tag = _byteCursor.ReadByte();
                } while (tag == Hessian2Tags.BinaryChunk);
                int finalLength = ReadFinalBinaryLength(tag);
                _byteCursor.CopyTo(chunks, finalLength);
                return chunks.ToArray();
            }
            return _byteCursor.ReadBytes(ReadFinalBinaryLength(tag));
        }

        private int ReadFinalBinaryLength(byte tag)
        {
            if (tag >= Hessian2Tags.ShortBinaryFirstTag && tag <= Hessian2Tags.ShortBinaryLastTag)
            {
                return tag - Hessian2Tags.ShortBinaryFirstTag;
            }
            if (tag >= Hessian2Tags.CompactBinaryFirstTag && tag <= Hessian2Tags.CompactBinaryLastTag)
            {
                return ReadCompactLengthWithOneByte(tag, Hessian2Tags.CompactBinaryFirstTag);
            }
            if (tag == Hessian2Tags.Binary)
            {
                return _byteCursor.ReadUInt16();
            }
            throw new InvalidDataException($"Hessian2 tag 0x{tag:x2} is not binary data.");
        }

        private static bool IsIntTag(byte tag)
        {
            return tag == Hessian2Tags.Int || (tag >= Hessian2Tags.CompactIntFirstTag && tag <= Hessian2Tags.CompactIntThreeByteLastTag);
        }

        private static bool IsLongTag(byte tag)
        {
            return tag == Hessian2Tags.Long
                || tag == Hessian2Tags.LongAsInt
                || (tag >= Hessian2Tags.CompactLongThreeByteFirstTag && tag <= Hessian2Tags.CompactLongThreeByteLastTag)
                || (tag >= Hessian2Tags.CompactLongFirstTag && tag <= Hessian2Tags.CompactLongLastTag)
                || (tag >= Hessian2Tags.CompactLongTwoByteFirstTag && tag <= Hessian2Tags.CompactLongTwoByteLastTag);
        }

        private static bool IsDoubleTag(byte tag)
        {
            return tag == Hessian2Tags.Double || (tag >= Hessian2Tags.DoubleZero && tag <= Hessian2Tags.DoubleAsThousandths);
        }

        private static bool IsStringTag(byte tag)
        {
            return tag == Hessian2Tags.String
                || tag == Hessian2Tags.StringChunk
                || tag <= Hessian2Tags.ShortStringLastTag
                || (tag >= Hessian2Tags.CompactStringFirstTag && tag <= Hessian2Tags.CompactStringLastTag);
        }

        private static bool IsBinaryTag(byte tag)
        {
            return tag == Hessian2Tags.BinaryChunk
                || tag == Hessian2Tags.Binary
                || (tag >= Hessian2Tags.ShortBinaryFirstTag && tag <= Hessian2Tags.ShortBinaryLastTag)
                || (tag >= Hessian2Tags.CompactBinaryFirstTag && tag <= Hessian2Tags.CompactBinaryLastTag);
        }

        private static bool IsListTag(byte tag)
        {
            return (tag >= Hessian2Tags.TypedVariableList && tag <= Hessian2Tags.UntypedFixedList)
                || (tag >= Hessian2Tags.CompactTypedListFirstTag && tag <= Hessian2Tags.CompactUntypedListLastTag);
        }

        private static bool IsMapOrObjectTag(byte tag)
        {
            return tag == Hessian2Tags.UntypedMap
                || tag == Hessian2Tags.TypedMap
                || tag == Hessian2Tags.Object
                || (tag >= Hessian2Tags.CompactObjectFirstTag && tag <= Hessian2Tags.CompactObjectLastTag);
        }
    }
}
