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

using DubboNet.DubboService.Native;
using NHessian.IO;
using System.Text;
using System.Text.Json;

namespace UnitTestForDubboNet;

public class HessianResponseSafetyTest
{
    [Theory]
    [InlineData("5200016153000162", "ab", false)]
    [InlineData("52000161300162", "ab", false)]
    [InlineData("52000000", "", false)]
    [InlineData("4100016142000162", "ab", true)]
    [InlineData("41000161340162", "ab", true)]
    [InlineData("41000020", "", true)]
    public void ChunkTerminalFormsLeaveTheNextValueUnread(string hex, string expected, bool binary)
    {
        var reader = new DubboHessian2Reader(Convert.FromHexString(hex).Append((byte)'T').ToArray());
        if (binary) Assert.Equal(Encoding.ASCII.GetBytes(expected), Assert.IsType<byte[]>(reader.ReadValue()));
        else Assert.Equal(expected, reader.ReadValue());
        Assert.Equal(true, reader.ReadValue());
    }

    [Fact]
    public void ErrorStatusAlsoReadsChunkedStringsWithoutRecursion()
    {
        var body = Enumerable.Range(0, 20000).SelectMany(_ => new byte[] { (byte)'R', 0, 0 })
            .Concat(new byte[] { 3, (byte)'b', (byte)'a', (byte)'d' }).ToArray();
        Assert.Equal("bad", NativeDubboCodec.DecodeResponse(new NativeDubboFrame
        { Status = 70, SerializationId = 2, Body = body }).ErrorMessage);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void TwentyThousandChunksAreConcatenatedWithoutRecursion(bool binary, bool nonempty)
    {
        var body = new List<byte> { 0x91 };
        byte[] part = binary ? new byte[] { 0, 255 } : Encoding.UTF8.GetBytes("中😀");
        int length = binary ? part.Length : "中😀".Length;
        for (int i = 0; i < 20000; i++)
        {
            body.Add(binary ? (byte)'A' : (byte)'R');
            body.Add(0);
            body.Add(nonempty ? (byte)length : (byte)0);
            if (nonempty) body.AddRange(part);
        }
        body.Add(binary ? (byte)0x21 : (byte)1);
        body.Add((byte)'x');
        object value = Decode(body).Value;
        if (binary)
            Assert.Equal((nonempty ? Enumerable.Range(0, 20000).SelectMany(_ => part) : Array.Empty<byte>()).Append((byte)'x'), Assert.IsType<byte[]>(value));
        else
            Assert.Equal((nonempty ? string.Concat(Enumerable.Repeat("中😀", 20000)) : "") + "x", value);
    }

    [Theory]
    [InlineData("520000")]
    [InlineData("410000")]
    [InlineData("520001")]
    [InlineData("410001")]
    [InlineData("52000054")]
    [InlineData("41000054")]
    [InlineData("520000530001")]
    [InlineData("410000420001")]
    [InlineData("520001E4B8AD01FF")]
    public void TruncatedChunksAndInvalidTerminalTagsFailWithCatchableErrors(string hex)
    {
        byte[] body = new byte[] { 0x91 }.Concat(Convert.FromHexString(hex)).ToArray();
        Exception? error = Record.Exception(() => Decode(body));
        Assert.True(error is InvalidDataException or EndOfStreamException, error?.ToString() ?? "Expected rejection.");
    }

    [Theory]
    [InlineData("58497FFFFFFF")]
    [InlineData("4300497FFFFFFF")]
    [InlineData("58924E")]
    [InlineData("43009200")]
    public void DeclaredCountsExceedingRemainingBytesAreRejectedBeforeAllocation(string hex)
    {
        var reader = new DubboHessian2Reader(Convert.FromHexString(hex));
        var error = Assert.Throws<InvalidDataException>(() => reader.ReadValue());
        Assert.Contains("Hessian2 declared count", error.Message);
        Assert.Contains("remaining payload bytes", error.Message);
    }

    [Theory]
    [InlineData("588F")]
    [InlineData("56008F")]
    public void FixedLengthListsRejectNegativeDeclaredCounts(string hex)
    {
        var reader = new DubboHessian2Reader(Convert.FromHexString(hex));
        Assert.Throws<InvalidDataException>(() => reader.ReadValue());
    }

    [Theory]
    [InlineData("58497FFFFFFF")]
    [InlineData("4300497FFFFFFF")]
    public void DubboResponseRejectsOversizedDeclaredCountsDuringParsing(string hex)
    {
        byte[] body = new byte[] { 0x91 }.Concat(Convert.FromHexString(hex)).ToArray();
        Assert.Throws<InvalidDataException>(() => Decode(body));
    }

    [Fact]
    public void FixedLengthListCanUseExactlyTheRemainingPayloadBytes()
    {
        var reader = new DubboHessian2Reader(Convert.FromHexString("58924E4E"));
        var list = Assert.IsType<List<object>>(reader.ReadValue());
        Assert.Equal(2, list.Count);
        Assert.All(list, value => Assert.Null(value));
    }

    [Fact]
    public void SharedLargeAsciiStringWithinBudgetCanBeSerialized()
    {
        string expected = new string('x', 60_000);
        var body = new List<byte>
        {
            0x91, 0x57, 0x79, (byte)'S', 0xea, 0x60
        };

        body.AddRange(Encoding.ASCII.GetBytes(expected));
        for (int i = 0; i < 100; i++)
            body.AddRange(new byte[] { 0x51, 0x91 });

        body.Add((byte)'Z');

        NativeDubboResponse response = Decode(body);
        string json = NativeDubboCodec.SerializeResponseJson(response);

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(101, document.RootElement.GetArrayLength());

        foreach (JsonElement item in document.RootElement.EnumerateArray())
        {
            Assert.Equal(1, item.GetArrayLength());
            Assert.Equal(expected, item[0].GetString());
        }
    }

    [Theory]
    [InlineData("é", true)]
    [InlineData("中", true)]
    [InlineData("\\", true)]
    [InlineData("\b", true)]
    [InlineData("\f", true)]
    [InlineData("\n", true)]
    [InlineData("\r", true)]
    [InlineData("\t", true)]
    [InlineData("\"", false)]
    [InlineData("\u0001", false)]
    [InlineData("\U0001F600", false)]
    public void SharedUnicodeStringsRespectJsonSizeBudget(
        string unit, bool shouldSucceed)
    {
        string expected = string.Concat(
            Enumerable.Repeat(unit, 60_000 / unit.Length));

        var body = new List<byte>
        {
            0x91, 0x57, 0x79, (byte)'S', 0xea, 0x60
        };

        body.AddRange(Encoding.UTF8.GetBytes(expected));
        for (int i = 0; i < 100; i++)
            body.AddRange(new byte[] { 0x51, 0x91 });

        body.Add((byte)'Z');

        if (!shouldSucceed)
        {
            var error = Assert.Throws<InvalidDataException>(() => Decode(body));
            Assert.Contains("JSON size budget", error.Message);
            return;
        }

        NativeDubboResponse response = Decode(body);
        string json = NativeDubboCodec.SerializeResponseJson(response);

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(101, document.RootElement.GetArrayLength());

        foreach (JsonElement item in document.RootElement.EnumerateArray())
        {
            Assert.Equal(1, item.GetArrayLength());
            Assert.Equal(expected, item[0].GetString());
        }
    }

    [Theory]
    [InlineData(0xd800)]
    [InlineData(0xdc00)]
    public void UnpairedSurrogateStringsCannotBypassJsonSizeBudget(int codeUnit)
    {
        // Construct UTF-16 directly so UTF-8 encoding cannot replace the test data.
        string unit = new string((char)codeUnit, 1);
        string json = NativeDubboCodec.SerializeJson(unit);
        Assert.Equal("\ufffd", JsonSerializer.Deserialize<string>(json));

        string text = new string((char)codeUnit, 60_000);
        List<object> values = Enumerable.Repeat<object>(text, 101).ToList();

        var error = Assert.Throws<InvalidDataException>(
            () => NativeDubboCodec.SerializeJson(values));
        Assert.Contains("JSON size budget", error.Message);
    }

    [Fact]
    public void AlternatingStringsConsumeTheirOwnJsonSizeBudget()
    {
        string plain = new string('x', 60_000);
        string escaped = new string('\u0001', 60_000);
        List<object> values = Enumerable.Range(0, 160)
            .Select(index => (object)(index % 2 == 0 ? plain : escaped))
            .ToList();

        var error = Assert.Throws<InvalidDataException>(
            () => NativeDubboCodec.SerializeJson(values));
        Assert.Contains("JSON size budget", error.Message);
    }

    [Theory]
    [InlineData("574E5A")]
    [InlineData("55004E5A")]
    [InlineData("5600914E")]
    [InlineData("794E")]
    [InlineData("71004E")]
    public void ListFormsStillDecodeNormally(string hex)
    {
        var list = Assert.IsType<List<object>>(
            new DubboHessian2Reader(Convert.FromHexString(hex)).ReadValue());
        Assert.Null(Assert.Single(list));
    }

    [Theory]
    [InlineData("5751905A")]
    [InlineData("58915190")]
    public void ListReferencesRemainAvailableWhileReadingTheirElements(string hex)
    {
        var list = Assert.IsType<List<object>>(
            new DubboHessian2Reader(Convert.FromHexString(hex)).ReadValue());
        Assert.Same(list, Assert.Single(list));
    }

    [Fact]
    public void ClassDefinitionWithFieldStillDecodesNormally()
    {
        var reader = new DubboHessian2Reader(Convert.FromHexString("43009101786091"));
        var value = Assert.IsType<Dictionary<object, object>>(reader.ReadValue());
        Assert.Equal(1, value["x"]);
    }

    [Theory]
    [InlineData(0x90, "0")]
    [InlineData((byte)'N', "null")]
    [InlineData((byte)'T', "true")]
    [InlineData((byte)'F', "false")]
    public void LargeCompactScalarListsCanBeDecodedAndSerialized(
        byte itemTag, string expectedScalar)
    {
        const int count = 900_000;
        var body = new List<byte> { 0x91 };
        AddFlatList(body, count, itemTag);

        NativeDubboResponse response = Decode(body);
        Assert.Equal(count, Assert.IsType<List<object>>(response.Value).Count);

        string json = NativeDubboCodec.SerializeResponseJson(response);
        Assert.Equal(1L + count * (expectedScalar.Length + 1L),
            Encoding.UTF8.GetByteCount(json));

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(count, document.RootElement.GetArrayLength());
        Assert.Equal(expectedScalar, document.RootElement[0].GetRawText());
        Assert.Equal(expectedScalar, document.RootElement[count - 1].GetRawText());
    }

    [Fact]
    public void IntegerBoundariesRemainValuesInJson()
    {
        object[] values =
        {
            sbyte.MinValue, sbyte.MaxValue, byte.MinValue, byte.MaxValue,
            short.MinValue, short.MaxValue, ushort.MinValue, ushort.MaxValue,
            int.MinValue, int.MaxValue, uint.MinValue, uint.MaxValue,
            long.MinValue, long.MaxValue, ulong.MinValue, ulong.MaxValue
        };

        using JsonDocument document = JsonDocument.Parse(
            NativeDubboCodec.SerializeJson(values));
        Assert.Equal(values.Length, document.RootElement.GetArrayLength());
        for (int index = 0; index < values.Length; index++)
        {
            Assert.Equal(
                Convert.ToString(values[index], System.Globalization.CultureInfo.InvariantCulture),
                document.RootElement[index].GetRawText());
        }
    }

    [Fact]
    public void LargeLongListCanBeDecodedAndSerialized()
    {
        List<long> expected = Enumerable.Range(0, 150_000)
            .Select(value => (long)value)
            .ToList();

        byte[] body;
        using (MemoryStream stream = new MemoryStream())
        {
            using HessianStreamWriter writer = new HessianStreamWriter(stream, true);
            HessianOutputV2 output = new HessianOutputV2(writer, TypeBindings.Java);
            output.WriteInt(1); // Value
            output.WriteObject(expected);
            body = stream.ToArray();
        }

        Assert.True(body.Length < NativeDubboCodec.DefaultMaxPayloadLength);

        NativeDubboResponse response = Decode(body);
        Assert.Equal(
            expected.Count,
            Assert.IsType<List<object>>(response.Value).Count);

        string json = NativeDubboCodec.SerializeResponseJson(response);
        Assert.Equal(expected, JsonSerializer.Deserialize<List<long>>(json));
    }

    [Fact]
    public void SharedReferenceDagIsStoppedByOneWholeTreeNodeBudget()
    {
        // Each layer is a sibling on the wire, referencing its predecessor twice.
        // About 100 bytes encode an expansion with millions of nodes, not a cycle.
        var body = Dag(20);
        Assert.True(body.Count < 200);
        var error = Assert.Throws<InvalidDataException>(() => Decode(body));
        Assert.Contains("100000 nodes", error.Message);
    }

    [Fact]
    public void LegitimateSharedReferencesRemainValuesRatherThanCircularPlaceholders()
    {
        var root = Assert.IsType<List<object>>(Decode(Dag(3)).Value);
        Assert.Equal(4, root.Count);
        Assert.Equal("[[0],[[0],[0]],[[[0],[0]],[[0],[0]]],[[[[0],[0]],[[0],[0]]],[[[0],[0]],[[0],[0]]]]]",
            NativeDubboCodec.SerializeJson(root));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedLargeScalarsAlsoConsumeTheExpansionSizeBudget(bool binary)
    {
        var body = new List<byte> { 0x91, 0x57, 0x79, binary ? (byte)'B' : (byte)'S', 0xea, 0x60 };
        body.AddRange(Enumerable.Repeat((byte)'x', 60000));
        for (int i = 0; i < 600; i++) body.AddRange(new byte[] { 0x51, 0x91 }); // ref list 1
        body.Add((byte)'Z');
        var error = Assert.Throws<InvalidDataException>(() => Decode(body));
        Assert.Contains("JSON size budget", error.Message);
    }

    [Fact]
    public void ValueAndAttachmentsShareTheSameExpansionBudget()
    {
        var body = Dag(14);

        // The value alone fits within the budget.
        Assert.IsType<List<object>>(Decode(body).Value);

        body[0] = 0x94; // Value + attachments
        body.AddRange(new byte[]
        {
            (byte)'H', 1, (byte)'a',
            0x51, 0x90, // ref root list 0
            (byte)'Z'
        });

        var error = Assert.Throws<InvalidDataException>(() => Decode(body));
        Assert.Contains($"{NativeDubboCodec.BaseNormalizedNodes} nodes", error.Message);
    }

    [Fact]
    public void NodeBudgetAcceptsItsExactBoundaryAndRejectsOneMore()
    {
        var body = new List<byte> { 0x91 };
        AddFlatList(body, NativeDubboCodec.MaxNormalizedNodes - 1, itemTag: 0x00);
        Assert.Equal(NativeDubboCodec.MaxNormalizedNodes - 1, Assert.IsType<List<object>>(Decode(body).Value).Count);
        body = new List<byte> { 0x91 };
        AddFlatList(body, NativeDubboCodec.MaxNormalizedNodes, itemTag: 0x00);
        var error = Assert.Throws<InvalidDataException>(() => Decode(body));
        Assert.Contains($"{NativeDubboCodec.MaxNormalizedNodes} nodes", error.Message);
    }

    private static List<byte> Dag(int layers)
    {
        var body = new List<byte> { 0x91, 0x57, 0x79, 0x90 };
        for (int index = 1; index <= layers; index++)
            body.AddRange(new byte[] { 0x7a, 0x51, (byte)(0x90 + index), 0x51, (byte)(0x90 + index) });
        body.Add((byte)'Z');
        return body;
    }

    private static void AddFlatList(List<byte> body, int count, byte itemTag = (byte)'N')
    {
        body.AddRange(new byte[] { 0x58, (byte)'I', (byte)(count >> 24), (byte)(count >> 16), (byte)(count >> 8), (byte)count });
        body.AddRange(Enumerable.Repeat(itemTag, count));
    }

    private static NativeDubboResponse Decode(IEnumerable<byte> body) => NativeDubboCodec.DecodeResponse(
        new NativeDubboFrame { Status = 20, SerializationId = 2, Body = body.ToArray() });
}
