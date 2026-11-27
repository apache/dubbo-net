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
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DubboNet.DubboService.Triple
{
    // The suite owns the HttpClient. Every call owns only its request/response.
    // HTTP/2 sessions, TLS, flow control and stream cancellation belong to .NET.
    internal sealed class BinaryTripleTransport
    {
        private const int MetadataLimit = 8 * 1024;
        private readonly HttpClient _client;
        private readonly int _messageLimit;
        private static readonly HashSet<string> Reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "host", "content-type", "content-length", "content-encoding", "te", "generic", "user-agent",
            "connection", "transfer-encoding", "upgrade", "keep-alive", "proxy-connection",
            "tri-service-version", "tri-service-group", "tri-service-timeout", "tri-exception-code"
        };

        internal BinaryTripleTransport(HttpClient client, int messageLimit)
        { _client = client; _messageLimit = messageLimit; }

        internal async Task SendAsync(DubboInvocation invocation, string version, string group,
            int timeoutMilliseconds, Stopwatch elapsed, DubboRequestResult result, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var request = new HttpRequestMessage(HttpMethod.Post,
                "/" + Uri.EscapeDataString(invocation.Service) + "/" + Hessian2WrapperCodec.Method);
            HttpCompatibility.SetHttp2Exact(request);
            byte[] message = invocation.PreparedTripleWrapper
                ?? Hessian2WrapperCodec.EncodeRequest(invocation, _messageLimit, token);
            if (message.Length > _messageLimit)
                throw new InvalidDataException($"Binary Triple message exceeds the {_messageLimit}-byte limit.");
            request.Content = new ByteArrayContent(TripleUnaryFraming.Encode(message));
            // Java Triple's gRPC dialect carries its fixed Java wrapper as proto.
            // This content type does NOT claim support for user Protobuf/IDL services.
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc+proto");
            request.Headers.TryAddWithoutValidation("te", "trailers");
            request.Headers.TryAddWithoutValidation("grpc-accept-encoding", "identity");
            request.Headers.TryAddWithoutValidation("generic", "true");
            AddRouting(request, "tri-service-version", version);
            AddRouting(request, "tri-service-group", group);
            AddAttachments(request, invocation.Attachments);
            long remaining = timeoutMilliseconds - elapsed.ElapsedMilliseconds;
            if (invocation.DeadlineTimestamp.HasValue)
                remaining = Math.Min(remaining, (long)((invocation.DeadlineTimestamp.Value - Stopwatch.GetTimestamp())
                    * (1000.0 / Stopwatch.Frequency)));
            if (remaining <= 0) throw new TimeoutException($"Triple request timed out after {timeoutMilliseconds} ms.");
            request.Headers.TryAddWithoutValidation("grpc-timeout", remaining <= 99_999_999
                ? remaining.ToString(CultureInfo.InvariantCulture) + "m"
                : ((remaining + 999) / 1000).ToString(CultureInfo.InvariantCulture) + "S");
            // Check the aggregate BEFORE dispatch; user metadata may not create an
            // oversized request even when every individual entry is small.
            _ = ReadMetadata(request.Headers, request.Content.Headers);

            using HttpResponseMessage response = await _client.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            result.ResponseHttpVersion = response.Version;
            result.ResponseAttachments = ReadMetadata(response.Headers, response.Content?.Headers);
            if (response.Version.Major != 2)
                throw new InvalidDataException($"Binary Triple requires HTTP/2; received HTTP/{response.Version}.");
            if ((int)response.StatusCode != 200)
                throw new InvalidDataException($"Binary Triple provider returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
            string mediaType = response.Content?.Headers.ContentType?.MediaType;
            if (!IsWrapperContentType(mediaType))
                throw new InvalidDataException($"Unexpected binary Triple Content-Type '{mediaType}'.");

            // Reading to EOF is necessary for trailing headers. Abort only this
            // response when a host/custom stream ignores its cancellation token.
            using var abort = token.Register(() => { try { response.Dispose(); } catch { } });
            try
            {
                using Stream stream = await response.Content.ReadAsStreamAsync().WaitAsync(token).ConfigureAwait(false);
                byte[] payload = await TripleUnaryFraming.ReadAsync(stream, _messageLimit, token).ConfigureAwait(false);
                // An EOF read can complete synchronously while also cancelling
                // the token. WaitAsync then returns that completed task, so check
                // cancellation again before accepting the response as successful.
                token.ThrowIfCancellationRequested();
                HttpHeaders trailers = HttpCompatibility.GetTrailingHeaders(response);
                result.ResponseTrailers = ReadMetadata(trailers);
                int status = ReadStatus(response.Headers, trailers, payload != null);
                result.GrpcStatusCode = status;
                result.TripleExceptionCode = ReadExceptionCode(response.Headers, trailers);
                string description = ReadMessage(trailers) ?? ReadMessage(response.Headers);
                if (status != 0)
                {
                    result.UpdateQueryFailed();
                    result.ErrorMeaasge = $"Triple RPC failed with grpc-status {status}: {description ?? "No status message."}";
                    return;
                }
                if (payload == null)
                    throw new InvalidDataException("Successful Unary Triple response contains no message.");
                result.RawResult = Hessian2WrapperCodec.DecodeResponse(payload);
                token.ThrowIfCancellationRequested();
                // The decoded tree has already passed the Hessian expansion budget.
                result.Result = JsonSerializer.Serialize(result.RawResult, DubboRequestResult.BinaryTripleJsonOptions);
                token.ThrowIfCancellationRequested();
                // Java Triple deliberately uses grpc-status=0 for some wrapped
                // exceptions. Match its tri-exception-code contract (0 = absent),
                // never guess failure from a business map's "class" field.
                if (result.TripleExceptionCode.GetValueOrDefault() != 0)
                {
                    result.UpdateQueryFailed();
                    result.ErrorMeaasge = $"Triple Java exception (tri-exception-code {result.TripleExceptionCode}): " +
                        NativeDubboCodec.DescribeException(result.RawResult);
                }
            }
            catch (Exception exception) when (token.IsCancellationRequested && !(exception is OperationCanceledException))
            {
                throw new OperationCanceledException("Binary Triple response cancelled.", exception, token);
            }
        }

        private static bool IsWrapperContentType(string value) =>
            string.Equals(value, "application/grpc", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "application/grpc+proto", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "application/grpc+wrapper", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "application/triple+wrapper", StringComparison.OrdinalIgnoreCase);

        private static void AddRouting(HttpRequestMessage request, string key, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            ValidateAscii(value);
            if (!request.Headers.TryAddWithoutValidation(key, value))
                throw new ArgumentException($"Invalid Triple routing header '{key}'.");
        }

        private static void AddAttachments(HttpRequestMessage request, IReadOnlyDictionary<string, string> attachments)
        {
            if (attachments == null) return;
            foreach (KeyValuePair<string, string> item in attachments)
            {
                string key = item.Key?.ToLowerInvariant();
                if (string.IsNullOrEmpty(key) || Reserved.Contains(key) || key.StartsWith("grpc-", StringComparison.Ordinal)
                    || key.StartsWith(":", StringComparison.Ordinal)) continue;
                if (key.Any(c => !(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '-' || c == '_' || c == '.')))
                    throw new ArgumentException($"Invalid binary Triple metadata name '{key}'.");
                string value = item.Value ?? string.Empty;
                ValidateAscii(value);
                // Existing attachments are strings: -bin values must be Base64,
                // never Base64-encode an already encoded value a second time.
                if (key.EndsWith("-bin", StringComparison.Ordinal))
                {
                    string encoded = value.Trim();
                    // gRPC permits both padded and unpadded binary metadata.
                    encoded = encoded.PadRight(encoded.Length + (4 - encoded.Length % 4) % 4, '=');
                    value = Convert.ToBase64String(Convert.FromBase64String(encoded));
                }
                // HttpClient classifies names such as content-language as content
                // headers. Preserve allowed metadata in the collection it expects.
                if (!request.Headers.TryAddWithoutValidation(key, value)
                    && !request.Content.Headers.TryAddWithoutValidation(key, value))
                    throw new ArgumentException($"Invalid binary Triple metadata name '{key}'.");
            }
        }

        private static void ValidateAscii(string value)
        {
            if (value.Any(c => c < 0x20 || c > 0x7e))
                throw new ArgumentException("Binary Triple metadata must be printable ASCII; use Base64 with a -bin name for binary data.");
        }

        private static IReadOnlyDictionary<string, object> ReadMetadata(params HttpHeaders[] blocks)
        {
            var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            long size = 0;
            // HttpClient splits one HTTP header block into general and content
            // collections. Count them together, and preserve both for callers.
            foreach (HttpHeaders headers in blocks)
            {
                if (headers == null) continue;
                foreach (var header in headers)
                {
                    string[] items = header.Value.ToArray();
                    foreach (string value in items)
                    {
                        size += header.Key.Length + value.Length + 32;
                        if (size > MetadataLimit) throw new InvalidDataException($"Binary Triple metadata exceeds {MetadataLimit} bytes.");
                    }
                    values[header.Key] = items.Length == 1 ? (object)items[0] : items;
                }
            }
            return values;
        }

        private static int ReadStatus(HttpHeaders headers, HttpHeaders trailers, bool hasMessage)
        {
            bool leading = headers.TryGetValues("grpc-status", out var before);
            bool trailing = trailers.TryGetValues("grpc-status", out var after);
            if (leading == trailing)
                throw new InvalidDataException(leading ? "Duplicate leading/trailing grpc-status." : "Triple response is missing grpc-status.");
            // Leading status is valid only for Trailers-Only (no DATA message).
            // A message-bearing response must finish with status in its trailers.
            if (leading && hasMessage)
                throw new InvalidDataException("Leading grpc-status is only valid for a trailers-only response without a message.");
            string[] values = (trailing ? after : before).ToArray();
            if (values.Length != 1 || !int.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out int status)
                || status < 0 || status > 16 || values[0] != status.ToString(CultureInfo.InvariantCulture))
                throw new InvalidDataException("Invalid Triple grpc-status.");
            return status;
        }

        private static string ReadMessage(HttpHeaders headers)
        {
            if (!headers.TryGetValues("grpc-message", out var values)) return null;
            string encoded = string.Join(",", values);
            try { return Uri.UnescapeDataString(encoded); }
            catch (UriFormatException) { return encoded; }
        }

        private static int? ReadExceptionCode(HttpHeaders headers, HttpHeaders trailers)
        {
            bool leading = headers.TryGetValues("tri-exception-code", out var before);
            bool trailing = trailers.TryGetValues("tri-exception-code", out var after);
            if (!leading && !trailing) return null;
            if (leading && trailing) throw new InvalidDataException("Duplicate tri-exception-code.");
            string[] values = (trailing ? after : before).ToArray();
            if (values.Length != 1 || !int.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out int code) || code < 0)
                throw new InvalidDataException("Invalid tri-exception-code.");
            return code;
        }
    }
}
