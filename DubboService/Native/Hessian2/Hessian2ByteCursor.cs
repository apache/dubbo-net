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
using System.Buffers.Binary;
using System.IO;

namespace DubboNet.DubboService.Native.Hessian2
{
    // Owns the input buffer and cursor; Hessian2 tags and decoded values belong to the caller.
    internal sealed class Hessian2ByteCursor
    {
        private readonly byte[] _buffer;
        private int _position;

        internal Hessian2ByteCursor(byte[] buffer)
        {
            _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        }

        internal int Remaining => _buffer.Length - _position;

        internal byte PeekByte()
        {
            EnsureAvailable(1);
            return _buffer[_position];
        }

        internal byte ReadByte()
        {
            EnsureAvailable(1);
            return _buffer[_position++];
        }

        internal int ReadUInt16()
        {
            EnsureAvailable(2);
            int value = BinaryPrimitives.ReadUInt16BigEndian(_buffer.AsSpan(_position, 2));
            _position += 2;
            return value;
        }

        internal int ReadInt32()
        {
            EnsureAvailable(4);
            int value = BinaryPrimitives.ReadInt32BigEndian(_buffer.AsSpan(_position, 4));
            _position += 4;
            return value;
        }

        internal long ReadInt64()
        {
            EnsureAvailable(8);
            long value = BinaryPrimitives.ReadInt64BigEndian(_buffer.AsSpan(_position, 8));
            _position += 8;
            return value;
        }

        internal byte[] ReadBytes(int count)
        {
            EnsureAvailable(count);
            byte[] result = new byte[count];
            Buffer.BlockCopy(_buffer, _position, result, 0, count);
            _position += count;
            return result;
        }

        /// <summary>
        /// Reads a bounded view and advances the cursor. The returned span aliases the
        /// input buffer supplied at construction; consume it before modifying that buffer.
        /// </summary>
        internal ReadOnlySpan<byte> ReadSpan(int count)
        {
            EnsureAvailable(count);
            ReadOnlySpan<byte> span = _buffer.AsSpan(_position, count);
            _position += count;
            return span;
        }

        // Stream.Write(ReadOnlySpan) is not in Standard 2.0. Copy directly from
        // the bounded backing array without allocating one array per Hessian chunk.
        internal void CopyTo(Stream destination, int count)
        {
            EnsureAvailable(count);
            destination.Write(_buffer, _position, count);
            _position += count;
        }

        private void EnsureAvailable(int count)
        {
            if (count < 0 || _position > _buffer.Length - count)
            {
                throw new EndOfStreamException("Unexpected end of Hessian2 payload.");
            }
        }
    }
}
