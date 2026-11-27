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
using DubboNet.DubboService.Triple;
using Google.Protobuf;
using System.IO;
using System.Text;
using System.Text.Json;

namespace UnitTestForDubboNet;

public class BinaryTripleCodecTest
{
    // Apache TripleResponseWrapper: serializeType=hessian4, data=Hessian "ok",
    // type=java.lang.String. Independent of all production framing/encoding helpers.
    internal const string OkWrapper = "0a086865737369616e341203026f6b1a106a6176612e6c616e672e537472696e67";
    internal static byte[] OkFrame => Convert.FromHexString("0000000021" + OkWrapper);

    [Theory]
    [InlineData("0a086865737369616e341203026f6b1a106a6176612e6c616e672e537472696e67", "ok")]
    [InlineData("0a086865737369616e321203026f6b", "ok")]
    [InlineData("0a086865737369616e3412014e", null)]
    [InlineData("0a086865737369616e34120191", 1)]
    public void IndependentWrapperBytesDecode(string hex, object? expected) =>
        Assert.Equal(expected, Hessian2WrapperCodec.DecodeResponse(Convert.FromHexString(hex)));

    [Theory]
    [InlineData("")]
    [InlineData("0a086865737369616e34")]
    [InlineData("0a086865737369616e341200")]
    [InlineData("0a086865737369616e3412024e4e")]
    [InlineData("0a086865737369616e3412014e12014e")]
    [InlineData("0a086865737369616e3412014e0a086865737369616e34")]
    [InlineData("0a086865737369616e34120a4e")]
    [InlineData("0a086f7468657278787812014e")]
    public void InvalidWrappersAreRejected(string hex) =>
        Assert.ThrowsAny<Exception>(() => Hessian2WrapperCodec.DecodeResponse(Convert.FromHexString(hex)));

    [Fact]
    public void RequestContainsThreeIndependentGenericArgumentsAndExactJavaSignature()
    {
        var invocation = new DubboInvocation("demo.Service", "overload",
            new[] { "java.lang.Integer", "long[]", "java.util.Map<java.lang.String,java.lang.Long>",
                "java.util.List<java.util.Map<java.lang.String,java.lang.Long>>[]" },
            new object[] { null!, new long[] { 1, 2 }, new Dictionary<string, long> { ["value"] = 1L },
                Array.Empty<object>() });
        byte[] wrapper = Hessian2WrapperCodec.EncodeRequest(invocation, 8192);
        using var input = new CodedInputStream(wrapper);
        var values = new List<byte[]>();
        var types = new List<string>();
        string? serialization = null;
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            if (tag == 10) serialization = input.ReadString();
            else if (tag == 18) values.Add(input.ReadBytes().ToByteArray());
            else if (tag == 26) types.Add(input.ReadString());
            else throw new Xunit.Sdk.XunitException("Unexpected request field.");
        }
        Assert.Equal("hessian4", serialization);
        Assert.Equal(new[] { "java.lang.String", "[Ljava.lang.String;", "[Ljava.lang.Object;" }, types);
        Assert.Equal(3, values.Count);
        Assert.Equal(Convert.FromHexString("086f7665726c6f6164"), values[0]);
        Assert.Equal(new object[] { "java.lang.Integer", "long[]", "java.util.Map", "java.util.List[]" },
            Assert.IsType<List<object>>(new DubboHessian2Reader(values[1]).ReadValue()));
        var arguments = Assert.IsType<List<object>>(new DubboHessian2Reader(values[2]).ReadValue());
        Assert.Null(arguments[0]);
        Assert.Equal(2, Assert.IsType<List<object>>(arguments[1]).Count);
        var map = Assert.IsType<Dictionary<object, object>>(arguments[2]);
        Assert.Equal(1L, Assert.Single(map).Value);
        Assert.Equal(1L, map["value"]);
        Assert.Empty(Assert.IsType<List<object>>(arguments[3]));
    }

    [Fact]
    public void RequestEncodingEnforcesLimitWhileWriting() =>
        Assert.Throws<InvalidDataException>(() => Hessian2WrapperCodec.EncodeRequest(
            new DubboInvocation("demo.Service", "echo", new[] { "java.lang.String" },
                new object[] { new string('x', 4096) }), 128));

    [Fact]
    public void DeepJsonRequestIsRejectedBeforeRecursiveProjection()
    {
        string json = new string('[', 5000) + "1" + new string(']', 5000);
        using var document = System.Text.Json.JsonDocument.Parse(json,
            new System.Text.Json.JsonDocumentOptions { MaxDepth = 6000 });
        var invocation = new DubboInvocation("demo.Service", "overload", new[] { "java.util.List" },
            new object[] { new object[] { document.RootElement } });
        var error = Assert.Throws<InvalidDataException>(() => Hessian2WrapperCodec.EncodeRequest(invocation, 8192));
        Assert.Contains("request nesting", error.Message);
    }

    [Fact]
    public void JsonRequestProjectionAndNormalizationDoNotDoubleChargeTheSameValues()
    {
        const int count = 1_100_000;
        string json = "[" + string.Join(",", Enumerable.Repeat("0", count)) + "]";
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var invocation = new DubboInvocation("demo.Service", "overload", new[] { "java.util.List" },
            new object[] { document.RootElement });
        Assert.NotEmpty(Hessian2WrapperCodec.EncodeRequest(invocation, 8 * 1024 * 1024));
    }

    [Fact]
    public void JsonPojoProjectionDoesNotSerializeItsProjectedChildrenAgain()
    {
        string json = "{\"values\":[" + string.Join(",", Enumerable.Repeat("0", 1_100_000)) + "]}";
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var invocation = new DubboInvocation("demo.Service", "overload", new[] { "demo.Request" },
            new object[] { document.RootElement });
        Assert.NotEmpty(Hessian2WrapperCodec.EncodeRequest(invocation, 8 * 1024 * 1024));
    }

    [Fact]
    public void RequestTraversalRejectsTooManyValuesBeforeWritingTheWrapper()
    {
        var values = Enumerable.Repeat<object>(0, NativeDubboCodec.MaxNormalizedNodes + 1);
        var invocation = new DubboInvocation("demo.Service", "overload", new[] { "java.util.List" }, new object[] { values });
        var error = Assert.Throws<InvalidDataException>(() => Hessian2WrapperCodec.EncodeRequest(invocation, 8 * 1024 * 1024));
        Assert.Contains("request traversal", error.Message);
    }

    [Theory]
    [InlineData(62, true)]
    [InlineData(63, false)]
    public void RequestNestingIncludesTheGenericArgumentArray(int nesting, bool allowed)
    {
        object value = 1;
        for (int i = 0; i < nesting; i++) value = new[] { value };
        var invocation = new DubboInvocation("demo.Service", "overload", new[] { "java.util.List" }, new[] { value });
        if (!allowed)
        {
            var error = Assert.Throws<InvalidDataException>(() => Hessian2WrapperCodec.EncodeRequest(invocation, 8192));
            Assert.Contains("request nesting", error.Message);
            return;
        }
        byte[] wrapper = Hessian2WrapperCodec.EncodeRequest(invocation, 8192);
        using var input = new CodedInputStream(wrapper);
        var fields = new List<byte[]>();
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            if (tag == 18) fields.Add(input.ReadBytes().ToByteArray());
            else input.SkipLastField();
        }
        Assert.IsType<List<object>>(new DubboHessian2Reader(fields[2]).ReadValue());
    }

    [Fact]
    public void StandaloneHessianDecodeBoundsDeclaredValueCount()
    {
        // Large legitimate lists now fit main's payload-scaled budget, while
        // declarations above its absolute cap still fail before list allocation.
        int count = NativeDubboCodec.MaxNormalizedNodes + 1;
        byte[] payload = new byte[count + 6];
        payload[0] = 0x58; payload[1] = 0x49;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(2, 4), count);
        Array.Fill(payload, (byte)'N', 6, count);
        Assert.Throws<InvalidDataException>(() => NativeDubboCodec.DecodeStandaloneHessianValue(payload));
    }

    [Theory]
    [InlineData(120_000)]
    [InlineData(900_000)]
    public void LargeCompactWrappersUseThePayloadSizedNodeBudget(int count)
    {
        byte[] hessian = new byte[count + 6];
        hessian[0] = 0x58; hessian[1] = 0x49;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(hessian.AsSpan(2, 4), count);
        Array.Fill(hessian, (byte)'N', 6, count);
        object value = Hessian2WrapperCodec.DecodeResponse(Wrap(hessian));
        Assert.Equal(count, Assert.IsType<List<object>>(value).Count);
        string json = JsonSerializer.Serialize(value, DubboRequestResult.BinaryTripleJsonOptions);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(count, document.RootElement.GetArrayLength());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, document.RootElement[0].ValueKind);
    }

    [Fact]
    public void SmallWrapperCannotExpandAReferenceDagToTheAbsoluteNodeCap()
    {
        // A tiny wire DAG must retain the base 100k allowance; it does not get
        // the 2m allowance reserved for genuinely large payloads.
        var hessian = new List<byte> { 0x57, 0x79, 0x90 };
        for (int layer = 1; layer <= 20; layer++)
            hessian.AddRange(new byte[] { 0x7a, 0x51, (byte)(0x90 + layer), 0x51, (byte)(0x90 + layer) });
        hessian.Add((byte)'Z');
        var error = Assert.Throws<InvalidDataException>(() => Hessian2WrapperCodec.DecodeResponse(Wrap(hessian.ToArray())));
        Assert.Contains($"{NativeDubboCodec.BaseNormalizedNodes} nodes", error.Message);
    }

    [Theory]
    [InlineData((byte)'x', true)]
    [InlineData((byte)'"', false)]
    public void SharedWrapperStringsFollowActualJsonEscapingCosts(byte character, bool succeeds)
    {
        var hessian = new List<byte> { 0x57, 0x79, (byte)'S', 0xea, 0x60 };
        hessian.AddRange(Enumerable.Repeat(character, 60_000));
        for (int index = 0; index < 100; index++) hessian.AddRange(new byte[] { 0x51, 0x91 });
        hessian.Add((byte)'Z');
        byte[] wrapper = Wrap(hessian.ToArray());
        if (!succeeds)
        {
            var error = Assert.Throws<InvalidDataException>(() => Hessian2WrapperCodec.DecodeResponse(wrapper));
            Assert.Contains("JSON size budget", error.Message);
            return;
        }
        object value = Hessian2WrapperCodec.DecodeResponse(wrapper);
        Assert.Equal(101, Assert.IsType<List<object>>(value).Count);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value, DubboRequestResult.BinaryTripleJsonOptions));
        Assert.Equal(new string((char)character, 60_000), document.RootElement[100][0].GetString());
    }

    private static byte[] Wrap(byte[] hessian)
    {
        using var stream = new MemoryStream();
        using (var output = new CodedOutputStream(stream, true))
        {
            output.WriteTag(1, WireFormat.WireType.LengthDelimited); output.WriteString("hessian4");
            output.WriteTag(2, WireFormat.WireType.LengthDelimited); output.WriteBytes(ByteString.CopyFrom(hessian));
        }
        return stream.ToArray();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    public async Task FragmentedHttpDataReadsOneWholeMessage(int fragment)
    {
        using var stream = new FragmentedStream(OkFrame, fragment);
        Assert.Equal(Convert.FromHexString(OkWrapper), await TripleUnaryFraming.ReadAsync(stream, 8192, default));
    }

    [Theory]
    [InlineData("00")]
    [InlineData("000000")]
    [InlineData("00000000034e")]
    [InlineData("00000000014e00")]
    [InlineData("00000000014e00000000014e")]
    [InlineData("00ffffffff")]
    [InlineData("0200000000")]
    public async Task InvalidUnaryFramesFailWithoutUnboundedAllocation(string hex)
    {
        using var stream = new FragmentedStream(Convert.FromHexString(hex), 1);
        Exception error = await Assert.ThrowsAnyAsync<Exception>(() => TripleUnaryFraming.ReadAsync(stream, 8192, default));
        Assert.True(error is EndOfStreamException or InvalidDataException, error.ToString());
    }

    [Fact]
    public async Task CompressionRequiresAnExplicitSupportedImplementation()
    {
        using var stream = new MemoryStream(Convert.FromHexString("0100000000"));
        await Assert.ThrowsAsync<NotSupportedException>(() => TripleUnaryFraming.ReadAsync(stream, 8192, default));
    }

    [Fact]
    public async Task EmptyBodyCanBeClassifiedAsTrailersOnlyByTransport()
    {
        using var stream = new MemoryStream();
        Assert.Null(await TripleUnaryFraming.ReadAsync(stream, 8192, default));
    }

    private sealed class FragmentedStream : MemoryStream
    {
        private readonly int _fragment;
        internal FragmentedStream(byte[] bytes, int fragment) : base(bytes) => _fragment = fragment;
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) =>
            base.ReadAsync(buffer, offset, Math.Min(count, _fragment), token);
    }
}
