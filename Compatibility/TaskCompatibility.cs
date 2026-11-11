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
using System.Threading;
using System.Threading.Tasks;

namespace DubboNet.Compatibility
{
    // Extensions are selected only when Task.WaitAsync is absent from the target.
    // Cancel/timeout ends the WAIT, never a shared registry/SDK operation itself.
    internal static class TaskCompatibility
    {
#if NETSTANDARD2_0
        private enum WaitSignal { Cancelled, TimedOut }

        internal static Task WaitAsync(this Task task, CancellationToken token) =>
            WaitCoreAsync(task, Timeout.InfiniteTimeSpan, token);

        internal static Task WaitAsync(this Task task, TimeSpan timeout) =>
            WaitCoreAsync(task, timeout, CancellationToken.None);

        internal static async Task<T> WaitAsync<T>(this Task<T> task, CancellationToken token)
        {
            await WaitCoreAsync(task, Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
            return await task.ConfigureAwait(false);
        }

        private static async Task WaitCoreAsync(Task task, TimeSpan timeout, CancellationToken token)
        {
            // Match Task.WaitAsync's millisecond truncation and Timer range.
            long milliseconds = (long)timeout.TotalMilliseconds;
            if (milliseconds < -1 || milliseconds > uint.MaxValue - 1)
                throw new ArgumentOutOfRangeException(nameof(timeout));
            if (task.IsCompleted) { await task.ConfigureAwait(false); return; }
            token.ThrowIfCancellationRequested();
            if (milliseconds == 0) throw new TimeoutException();
            if (milliseconds == -1 && !token.CanBeCanceled)
            { await task.ConfigureAwait(false); return; }

            // Use RESULTS for the private signal: a timer callback racing disposal
            // must not leave an unobserved TimeoutException on a losing private task.
            var signal = new TaskCompletionSource<WaitSignal>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (token.Register(() => signal.TrySetResult(WaitSignal.Cancelled)))
            using (var timer = milliseconds == -1 ? null :
                new Timer(_ => signal.TrySetResult(WaitSignal.TimedOut), null, milliseconds, -1))
            {
                Task winner = await Task.WhenAny(task, signal.Task).ConfigureAwait(false);
                if (winner != task)
                {
                    // An abandoned SDK wait may fault later; observe that fault without
                    // holding a timer/registration or changing the operation's outcome.
                    ObserveFault(task);
                    if (await signal.Task.ConfigureAwait(false) == WaitSignal.Cancelled)
                        throw new OperationCanceledException(token);
                    throw new TimeoutException();
                }
                await winner.ConfigureAwait(false);
            }
        }
#endif
        internal static void ObserveFault(Task task)
        {
            _ = task.ContinueWith(completed => { _ = completed.Exception; },
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted |
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
}
