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

using DubboNet.Clients;
using MyCommonHelper;
using NetService.Telnet;
using System.Reflection;

namespace UnitTestForDubboNet;

public class HistoricalHelperCleanupTest
{
    [Theory]
    [InlineData("MyCommonHelper.CommonLog")]
    [InlineData("MyCommonHelper.MyCommonTool")]
    [InlineData("MyCommonHelper.MyExtensionMethods")]
    [InlineData("MyCommonHelper.EnumerableExtensions")]
    [InlineData("MyCommonHelper.EncryptionHelper.MyAES")]
    [InlineData("MyCommonHelper.EncryptionHelper.MyDES")]
    [InlineData("MyCommonHelper.EncryptionHelper.MyEncryption")]
    [InlineData("MyCommonHelper.EncryptionHelper.SymmetricAlgorithmType")]
    [InlineData("MyCommonHelper.EncryptionHelper.MyRC4")]
    [InlineData("MyCommonHelper.EncryptionHelper.MyRSA")]
    [InlineData("MyCommonHelper.EncryptionHelper.MyTripleDES")]
    [InlineData("MyCommonHelper.EncryptionHelper.MyXOR")]
    [InlineData("NetService.WebService.MyWebTool")]
    [InlineData("NetService.Telnet.EsTelnet")]
    [InlineData("DubboNet.DubboService.HttpDubboActuatorSuite")]
    public void UnusedLegacyTypesAreNotShippedInTheLibrary(string name)
    {
        Assert.Null(typeof(DubboClient).Assembly.GetType(name));
    }

    [Fact]
    public void OnlyActivelyUsedHelperTypesRemain()
    {
        string[] names = typeof(DubboClient).Assembly.GetExportedTypes()
            .Where(type => type.Namespace?.StartsWith("MyCommonHelper", StringComparison.Ordinal) == true)
            .Select(type => type.FullName!).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[]
        {
            "MyCommonHelper.EncryptionHelper.MyHash",
            "MyCommonHelper.HexaDecimal",
            "MyCommonHelper.MyBytes",
            "MyCommonHelper.MyLogger",
            "MyCommonHelper.MyLogger+LogType",
            "MyCommonHelper.ShowHexMode"
        }, names);
        Assert.DoesNotContain(typeof(DubboClient).Assembly.GetTypes(), type => type.Namespace == "NetService.WebService");
    }

    [Fact]
    public void ByteHelperRetainsOnlyTheFormattingEntryPoint()
    {
        var method = Assert.Single(typeof(MyBytes).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly));
        Assert.Equal(nameof(MyBytes.ByteToHexString), method.Name);
        Assert.Equal(new[] { typeof(byte[]), typeof(HexaDecimal), typeof(ShowHexMode) }, method.GetParameters().Select(parameter => parameter.ParameterType));
    }

    public static IEnumerable<object[]> FormattingCases()
    {
        var digits = new[]
        {
            (HexaDecimal.hex2, new[] { "00000000", "00001111", "11111111" }),
            (HexaDecimal.hex10, new[] { "000", "015", "255" }),
            (HexaDecimal.hex16, new[] { "00", "0f", "ff" })
        };
        var formats = new[]
        {
            (ShowHexMode.@null, ""), (ShowHexMode.space, " "),
            (ShowHexMode.spit0x, "0x"), (ShowHexMode.spitSpace0x, " 0x"),
            (ShowHexMode.spit0b, "0b"), (ShowHexMode.spitSpace0b, " 0b"),
            (ShowHexMode.spit0d, "0d"), (ShowHexMode.spitSpace0d, " 0d"),
            (ShowHexMode.spit_, "_"), (ShowHexMode.spitM_, "-")
        };
        foreach (var (numberBase, values) in digits)
            foreach (var (mode, prefix) in formats)
                yield return new object[] { numberBase, mode, (prefix + string.Join(prefix, values)).Trim() };
    }

    [Theory]
    [MemberData(nameof(FormattingCases))]
    public void TelnetByteFormattingPreservesAllExistingBasesAndModes(HexaDecimal numberBase, ShowHexMode mode, string expected)
    {
        Assert.Equal(expected, MyBytes.ByteToHexString(new byte[] { 0, 15, 255 }, numberBase, mode));
        Assert.Equal(string.Empty, MyBytes.ByteToHexString(Array.Empty<byte>(), numberBase, mode));
        Assert.Null(MyBytes.ByteToHexString(null!, numberBase, mode));
    }

    [Fact]
    public void TelnetInvalidOptionStillReportsFormattedBytes()
    {
        string? message = null;
        var result = TelnetOptionHelper.GetResponseOption(new byte[] { 255, 1 }, (value, kind) =>
        {
            Assert.Equal(TelnetMessageType.Error, kind);
            message = Assert.IsType<string>(value);
        });
        Assert.Null(result);
        Assert.Contains("ff 01", message);
    }

    [Fact]
    public void ActiveTelnetParserStillHandlesEscapedIacAndNegotiation()
    {
        byte[] display = TelnetOptionHelper.DealRawBytes(new byte[] { 65, 255, 255, 255, 251, 1, 66 }, out var options);
        Assert.Equal(new byte[] { 65, 255, 66 }, display);
        Assert.Equal(new byte[] { 255, 251, 1 }, Assert.IsType<byte[]>(Assert.Single(options.Cast<object>())));
    }
}
