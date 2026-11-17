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
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace DubboNet.Compatibility
{
    internal static class NetworkCompatibility
    {
        internal static async Task ConnectOwnedAsync(TcpClient client, IPEndPoint endpoint, CancellationToken token)
        {
#if NETSTANDARD2_0
            token.ThrowIfCancellationRequested();
            // This client is a NEW, unpublished connection attempt, never a shared
            // RPC channel. Disposing it aborts the old uncancellable ConnectAsync.
            using (token.Register(client.Dispose))
            {
                try
                {
                    await client.ConnectAsync(endpoint.Address, endpoint.Port).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                }
                catch (Exception exception) when (token.IsCancellationRequested &&
                    (exception is ObjectDisposedException || exception is SocketException))
                { throw new OperationCanceledException("Connection cancelled.", exception, token); }
            }
#else
            await client.ConnectAsync(endpoint.Address, endpoint.Port, token).ConfigureAwait(false);
#endif
        }

        internal static async Task ConnectOwnedAsync(Socket socket, EndPoint endpoint, CancellationToken token)
        {
#if NETSTANDARD2_0
            token.ThrowIfCancellationRequested();
            using (token.Register(socket.Dispose))
            {
                try
                {
                    await socket.ConnectAsync(endpoint).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                }
                catch (Exception exception) when (token.IsCancellationRequested &&
                    (exception is ObjectDisposedException || exception is SocketException))
                { throw new OperationCanceledException("Connection cancelled.", exception, token); }
            }
#else
            await socket.ConnectAsync(endpoint, token).ConfigureAwait(false);
#endif
        }

        internal static ValueTask WriteAsync(Stream stream, byte[] buffer, CancellationToken token)
        {
#if NETSTANDARD2_0
            // Use the existing array directly. Cancellation/failure of a partial
            // frame is still handled by NativeDubboConnection.FaultConnection.
            return new ValueTask(WriteLegacyAsync(stream, buffer, token));
#else
            return stream.WriteAsync(buffer, token);
#endif
        }

        internal static ValueTask<int> ReadAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken token)
        {
#if NETSTANDARD2_0
            // Legacy NetworkStream may ignore cancellation after starting I/O.
            // Bound the wait; the owner disposes the connection on lifetime end.
            return new ValueTask<int>(ReadLegacyAsync(stream, buffer, offset, count, token));
#else
            return stream.ReadAsync(buffer.AsMemory(offset, count), token);
#endif
        }

        // Bound legacy I/O even when its Stream implementation ignores the token.
        // The Native owner faults/disposes a channel after a cancelled partial write;
        // a read is cancelled only for that channel's lifetime, not for one RPC.
        internal static Task WriteLegacyAsync(Stream stream, byte[] buffer, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return stream.WriteAsync(buffer, 0, buffer.Length, token).WaitAsync(token);
        }

        internal static Task<int> ReadLegacyAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return stream.ReadAsync(buffer, offset, count, token).WaitAsync(token);
        }

        internal static ValueTask<int> SendAsync(Socket socket, byte[] buffer, int offset)
        {
#if NETSTANDARD2_0
            return new ValueTask<int>(socket.SendAsync(new ArraySegment<byte>(buffer, offset, buffer.Length - offset), SocketFlags.None));
#else
            return socket.SendAsync(buffer.AsMemory(offset), SocketFlags.None);
#endif
        }
    }
}
