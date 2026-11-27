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
using System.Threading;
using System.Threading.Tasks;

namespace DubboNet.DubboService.Triple
{
    internal static class TripleUnaryFraming
    {
        internal static byte[] Encode(byte[] message)
        {
            byte[] frame = new byte[checked(message.Length + 5)];
            BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(1, 4), (uint)message.Length);
            Buffer.BlockCopy(message, 0, frame, 5, message.Length);
            return frame;
        }

        // HTTP/2 DATA boundaries do not coincide with message boundaries. Read
        // exactly one message and then EOF, which also makes trailers available.
        // Null is permitted here for trailers-only errors, never a successful unary.
        internal static async Task<byte[]> ReadAsync(Stream stream, int limit, CancellationToken token)
        {
            byte[] header = new byte[5];
            int first = await ReadPartAsync(stream, header, 0, 5, token).ConfigureAwait(false);
            if (first == 0) return null;
            await FillAsync(stream, header, first, header.Length, token).ConfigureAwait(false);
            if (header[0] == 1)
                throw new NotSupportedException("Compressed binary Triple messages are not supported; only identity is advertised.");
            if (header[0] != 0)
                throw new InvalidDataException($"Invalid Triple compression flag {header[0]}.");
            uint length = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(1, 4));
            if (length > limit)
                throw new InvalidDataException($"Binary Triple declared length {length} exceeds the {limit}-byte limit.");
            byte[] message = new byte[(int)length];
            await FillAsync(stream, message, 0, message.Length, token).ConfigureAwait(false);
            if (await ReadPartAsync(stream, header, 0, 1, token).ConfigureAwait(false) != 0)
                throw new InvalidDataException("Unary Triple response contains more than one message or trailing body bytes.");
            return message;
        }

        private static async Task FillAsync(Stream stream, byte[] buffer, int offset, int end, CancellationToken token)
        {
            while (offset < end)
            {
                int read = await ReadPartAsync(stream, buffer, offset, end - offset, token).ConfigureAwait(false);
                if (read == 0) throw new EndOfStreamException("Truncated binary Triple message.");
                offset += read;
            }
        }

        private static Task<int> ReadPartAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            // Also bound custom streams that ignore cancellation. The transport
            // disposes only this response if cancellation wins, not its shared pool.
            return stream.ReadAsync(buffer, offset, count, token).WaitAsync(token);
        }
    }
}
