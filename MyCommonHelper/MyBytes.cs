using System;
using System.Collections.Generic;
using System.Text;

namespace MyCommonHelper
{
    /// <summary>
    /// hex 字符串显示时的分割方式
    /// </summary>
    public enum ShowHexMode
    {
        @null = 0,    //不风格每个字节
        space = 1,   //以空格分割
        spit0x = 2,   //以0x分割 (用于显示16进制)
        spitSpace0x = 3,   //以 0x分割 (用于显示16进制)
        spit0b = 4,   //以0b分割 (用于显示2进制)
        spitSpace0b = 5,   //以 0b分割 (用于显示2进制)
        spit0d = 6,   //以0d分割 (用于显示10进制)
        spitSpace0d = 7,   //以 0d分割 (用于显示10进制)
        spit_ = 8,    //以下划线分割
        spitM_ = 9  //以中划线分割

    }

    /// <summary>
    /// 表示要代表数据的进制
    /// </summary>
    public enum HexaDecimal
    {
        hex2 = 2,
        hex10 = 10,    //001 224 023  表示显示需要补0
        hex16 = 16
    }

    public class MyBytes
    {
        private static Dictionary<HexaDecimal, int> DictionaryHexaDecimal = new Dictionary<HexaDecimal, int>() { { HexaDecimal.hex2, 8 }, { HexaDecimal.hex10, 3 }, { HexaDecimal.hex16, 2 } };
        private static Dictionary<ShowHexMode, string> DictionaryShowHexMode = new Dictionary<ShowHexMode, string>() { { ShowHexMode.spitSpace0x, " 0x" }, { ShowHexMode.spit0x, "0x" }, { ShowHexMode.spitSpace0d, " 0d" }, { ShowHexMode.spit0d, "0d" }, { ShowHexMode.spitSpace0b, " 0b" }, { ShowHexMode.spit0b, "0b" }, { ShowHexMode.spitM_, "-" }, { ShowHexMode.spit_, "_" }, { ShowHexMode.space, " " }, { ShowHexMode.@null, "" } };


        /// <summary>
        /// 将字节数组转换为指定进制的可读字符串
        /// </summary>
        /// <param name="yourBytes">需要转换的字节数组</param>
        /// <param name="hexDecimal">指定进制</param>
        /// <param name="stringMode">指定格式</param>
        /// <returns>返回结果</returns>
        public static string ByteToHexString(byte[] yourBytes, HexaDecimal hexDecimal, ShowHexMode stringMode)
        {
            // 如果只考虑16进制对格式没有特殊要求 可以直接使用 ((byte)233).ToString("X2"); 或 BitConverter.ToString(new byte[]{1,2,3,10,12,233})
            if (yourBytes == null)
            {
                return null;
            }
            StringBuilder result = new StringBuilder(DictionaryHexaDecimal[hexDecimal] + DictionaryShowHexMode[stringMode].Length);

            for (int i = 0; i < yourBytes.Length; i++)
            {
                result.Append(DictionaryShowHexMode[stringMode]);
                result.Append(Convert.ToString(yourBytes[i], (int)hexDecimal).PadLeft(DictionaryHexaDecimal[hexDecimal], '0'));
            }
            return result.ToString().Trim();
        }

    }
}
