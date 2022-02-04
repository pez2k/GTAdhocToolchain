﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Syroot.BinaryData;
using Syroot.BinaryData.Core;

using GTAdhocToolchain.Core;

namespace GTAdhocToolchain.Core
{
    public class AdhocStream : BinaryStream
    {
        public int Version { get; set; }

        public List<AdhocSymbol> Symbols { get; set; } = new();

        public AdhocStream(Stream baseStream, int version)
            : base(baseStream)
        {
            Version = version;
        }

        public void ReadSymbolTable()
        {
            uint entryCount = (uint)DecodeBitsAndAdvance();
            Symbols = new List<AdhocSymbol>((int)entryCount);
            for (var i = 0; i < entryCount; i++)
            {
                int strLen = (int)DecodeBitsAndAdvance();

                // Bugged, doesnt actually read the string length
                //StringTable[i] = sr.ReadStringRaw(strLen);
                Symbols.Add(new AdhocSymbol(Encoding.GetString(ReadBytes(strLen))));
            }
        }

        public void WriteSymbols(IEnumerable<AdhocSymbol> symbols)
        {
            WriteInt32(symbols.Count());
            foreach (var symb in symbols)
                WriteSymbol(symb);
        }

        public void WriteSymbol(AdhocSymbol symbol)
        {
            if (Version <= 8)
                WriteString(symbol.Name);
            else
                WriteVarInt(symbol.Id);
                    
        }

        public List<AdhocSymbol> ReadSymbols()
        {
            uint symbCount = ReadUInt32();
            List<AdhocSymbol> list = new List<AdhocSymbol>((int)symbCount);

            for (int i = 0; i < symbCount; i++)
            {
                AdhocSymbol symbol = ReadSymbol();
                list.Add(symbol);
            }

            return list;
        }

        public AdhocSymbol ReadSymbol()
        {
            if (Version <= 8)
                return new AdhocSymbol(this.ReadString(StringCoding.Int16CharCount));
            else
            {
                uint symbolTableIdx = (uint)DecodeBitsAndAdvance();
                return Symbols[(int)symbolTableIdx];
            }
        }

        public ulong DecodeBitsAndAdvance()
        {
            ulong value = (ulong)ReadByte();
            ulong mask = 0x80;

            while ((value & mask) != 0)
            {
                value = ((value - mask) << 8) | (Read1Byte());
                mask <<= 7;
            }
            return value;
        }

        public void WriteVarString(string str)
        {
            if (IsAscii(str))
            {
                // Non UTF8 operation, incase the string is a escaped byte array as string
                WriteVarInt(str.Length);
                byte[] data = new byte[str.Length];
                for (int i = 0; i < str.Length; i++)
                    data[i] = (byte)str[i];

                this.Write(data);
            }
            else
            {
                // Must convert, has some utf8 chars, i.e japanese
                WriteVarInt(Encoding.UTF8.GetByteCount(str));
                StreamExtensions.WriteString(this, str, StringCoding.Raw);
            }
        }

        public bool IsAscii(string str) 
        {
            for (int i = 0; i<str.Length; i++) 
            {
                if (str[i] < 0 || str[i] > 0xFF)
                    return false;
            }
            return true;
        }

        public void WriteVarInt(int val)
        {
            Span<byte> buffer = Array.Empty<byte>();

            if (val <= 0x7F)
            {
                WriteByte((byte)val);
                return;
            }
            else if (val <= 0x3FFF)
            {
                Span<byte> tempBuf = BitConverter.GetBytes(val).AsSpan();
                tempBuf.Reverse();
                buffer = tempBuf.Slice(2, 2);
            }
            else if (val <= 0x1FFFFF)
            {
                Span<byte> tempBuf = BitConverter.GetBytes(val).AsSpan();
                tempBuf.Reverse();
                buffer = tempBuf.Slice(1, 3);
            }
            else if (val <= 0xFFFFFFF)
            {
                buffer = BitConverter.GetBytes(val).AsSpan();
                buffer.Reverse();
            }
            else if (val <= 0xFFFFFFFF)
            {
                buffer = BitConverter.GetBytes(val);
                buffer.Reverse();
                buffer = new byte[] { 0, buffer[0], buffer[1], buffer[2], buffer[3] };
            }

            uint mask = 0x80;
            for (int i = 1; i < buffer.Length; i++)
            {
                buffer[0] += (byte)mask;
                mask >>= 1;
            }

            Write(buffer);
        }

        public static byte[] EncodeAndAdvance(uint value)
        {
            uint mask = 0x80;
            Span<byte> buffer = Array.Empty<byte>();

            if (value <= 0x7F)
            {
                return new[] { (byte)value };
            }
            else if (value <= 0x3FFF)
            {
                Span<byte> tempBuf = BitConverter.GetBytes(value).AsSpan();
                tempBuf.Reverse();
                buffer = tempBuf.Slice(2, 2);
            }
            else if (value <= 0x1FFFFF)
            {
                Span<byte> tempBuf = BitConverter.GetBytes(value).AsSpan();
                tempBuf.Reverse();
                buffer = tempBuf.Slice(1, 3);
            }
            else if (value <= 0xFFFFFFF)
            {
                buffer = BitConverter.GetBytes(value);
                buffer.Reverse();
            }
            else if (value <= 0xFFFFFFFF)
            {
                buffer = BitConverter.GetBytes(value);
                buffer.Reverse();
                buffer = new byte[] { 0, buffer[0], buffer[1], buffer[2], buffer[3] };
            }
            else
                throw new Exception("????");

            for (int i = 1; i < buffer.Length; i++)
            {
                buffer[0] += (byte)mask;
                mask >>= 1;
            }

            return buffer.ToArray();
        }
    }
}