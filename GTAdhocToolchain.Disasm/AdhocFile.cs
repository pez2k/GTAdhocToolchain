﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

using Syroot.BinaryData.Memory;
using Syroot.BinaryData;

using GTAdhocToolchain.Core;
using GTAdhocToolchain.Core.Instructions;

namespace GTAdhocToolchain.Disasm
{
    public class AdhocFile
    {
        public const string MAGIC = "ADCH";
        public List<AdhocSymbol> SymbolTable { get; private set; }
        public byte[] _buffer;

        public AdhocCodeFrame TopLevelFrame { get; set; }
        public byte Version { get; set; }

        private AdhocFile(byte version)
        {
            Version = version;
        }

        public static AdhocFile ReadFromFile(string path)
        {
            var bytes = File.ReadAllBytes(path);
            using var fs = new FileStream(path, FileMode.Open);
            using var stream = new AdhocStream(fs, 12);

            string magic = stream.ReadString(StringCoding.ZeroTerminated);
            if (magic.AsSpan(0, 4).ToString() != MAGIC)
                throw new Exception("Invalid MAGIC, doesn't match ADCH.");

            byte version = (byte)int.Parse(magic.AsSpan(4, 3));
            var adhoc = new AdhocFile(version);

            stream.Version = version;

            if (adhoc.Version >= 9)
                stream.ReadSymbolTable();
            adhoc.SymbolTable = stream.Symbols;

            adhoc.TopLevelFrame = new AdhocCodeFrame();
            adhoc.TopLevelFrame.Read(stream);

            return adhoc;
        }


        public void PrintStringTable(AdhocStream stream, string outPath)
        {
            stream.Position = 8;

            using var sw = new StreamWriter(outPath);
            uint entryCount = (uint)stream.DecodeBitsAndAdvance();
            var results = new string[entryCount];
            for (var i = 0; i < entryCount; i++)
            {
                sw.WriteLine($"0x{stream.Position:X2} | {stream.ReadString(StringCoding.ByteCharCount)}");
            }
            sw.Flush();
        }

        public void Disassemble(string outPath, bool withOffset)
        {
            Console.WriteLine($"Dissasembling {outPath}...");
            using var sw = new StreamWriter(outPath);
            sw.WriteLine("==== Disassembly generated by GTAdhocTools by Nenkai#9075 ====");
            if (!string.IsNullOrEmpty(TopLevelFrame.SourceFilePath?.Name))
                sw.WriteLine($"Original File Name: {TopLevelFrame.SourceFilePath.Name}");

            sw.WriteLine($"Version: {Version}");
            if (SymbolTable != null)
                sw.WriteLine($"({SymbolTable.Count} strings)");
            sw.WriteLine($"Root Instructions: {TopLevelFrame.Instructions.Count}");
            sw.Write($"  > Stack Size: {TopLevelFrame.Stack.StackSize} - Variable Heap Size: {TopLevelFrame.Stack.LocalVariableStorageSize} - Variable Heap Size Static: {(TopLevelFrame.Version < 10 ? "=Variable Heap Size" : $"{TopLevelFrame.Stack.StaticVariableStorageSize}")}");
            sw.WriteLine();

            Stack<object> modOrClass = new Stack<object>();
            modOrClass.Push("TopLevel");

            int ifdepth = 0;
            for (var i = 0; i < TopLevelFrame.Instructions.Count; i++)
            {
                var inst = TopLevelFrame.Instructions[i];

                if (inst.IsFunctionOrMethod())
                    sw.WriteLine();

                if (ifdepth > 0)
                    sw.Write(new string(' ', 2 * ifdepth));

                if (withOffset)
                    sw.Write($"{inst.InstructionOffset - 5,6:X2}|");
                sw.Write($"{inst.LineNumber,4}|");
                sw.Write($"{i,3}| "); // Function Instruction Number
                sw.Write(TopLevelFrame.Instructions[i]);

                int depth = 0;
                if (inst.IsFunctionOrMethod())
                {
                    sw.WriteLine();
                    DisassembleSubroutine(sw, inst as SubroutineBase, withOffset, ref depth, ref modOrClass);
                    continue;
                }
                else if (inst.InstructionType == AdhocInstructionType.JUMP_IF_FALSE || inst.InstructionType == AdhocInstructionType.JUMP_IF_TRUE)
                    ifdepth++;
                else if (inst.InstructionType == AdhocInstructionType.LEAVE)
                    ifdepth--;
                else if (inst.InstructionType == AdhocInstructionType.MODULE_DEFINE)
                    modOrClass.Push((inst as InsModuleDefine).Names[^1].Name);
                else if (inst.InstructionType == AdhocInstructionType.CLASS_DEFINE)
                    modOrClass.Push((inst as InsClassDefine).Name.Name);
                else if (inst.InstructionType == AdhocInstructionType.TRY_CATCH)
                    modOrClass.Push("TryCatch");
                else if (inst.InstructionType == AdhocInstructionType.MODULE_CONSTRUCTOR)
                    modOrClass.Push("Module Constructor");
                else if (inst is InsSetState state && state.State == AdhocRunState.EXIT)
                    sw.Write($"  [EXIT {modOrClass.Pop()}]");

                sw.WriteLine();
            }

            sw.Flush();
        }

        public void PrintStrings(string outPath)
        {
            if (TopLevelFrame.Version < 12)
            {
                Console.WriteLine("Not printing strings, script is version < 12");
                return;
            }

            using var sw = new StreamWriter(outPath);
            sw.WriteLine("==== Adhoc Strings generated by GTAdhocTools by Nenkai#9075 ====");
            if (!string.IsNullOrEmpty(TopLevelFrame.SourceFilePath?.Name))
                sw.WriteLine($"Original File Name: {TopLevelFrame.SourceFilePath.Name}");

            sw.Write($"Version: {Version}");
            if (SymbolTable != null)
                sw.Write($"{SymbolTable.Count} strings ({BitConverter.ToString(AdhocStream.EncodeAndAdvance((uint)SymbolTable.Count)).Replace('-', ' ')})");
            sw.WriteLine();

            for (int i = 0; i < SymbolTable.Count; i++)
            {
                sw.WriteLine($"{i} | {BitConverter.ToString(AdhocStream.EncodeAndAdvance((uint)i)).Replace('-', ' ')} | {SymbolTable[i]}");
            }
            sw.Flush();
        }


        public void DisassembleSubroutine(StreamWriter sw, SubroutineBase subroutine, bool withOffset, ref int depth, ref Stack<object> modOrClass)
        {
            depth++;

            int ifdepth = 0;
            string curDepthStr = new string(' ', 2 * depth);
            for (int i = 0; i < subroutine.CodeFrame.Instructions.Count; i++)
            {
                InstructionBase inst = subroutine.CodeFrame.Instructions[i];
                sw.Write(curDepthStr);
                if (ifdepth > 0)
                    sw.Write(new string(' ', 2 * ifdepth));

                if (withOffset)
                    sw.Write($"{inst.InstructionOffset - 5,6:X2}|");
                sw.Write($"{inst.LineNumber,4}|");
                sw.Write($"{i,3}| "); // Function Instruction Number
                sw.Write(inst);

                if (inst.IsFunctionOrMethod())
                    DisassembleSubroutine(sw, inst as SubroutineBase, withOffset, ref depth, ref modOrClass);
                else if (inst.InstructionType == AdhocInstructionType.JUMP_IF_FALSE || inst.InstructionType == AdhocInstructionType.JUMP_IF_TRUE)
                    ifdepth++;
                else if (inst.InstructionType == AdhocInstructionType.LEAVE)
                    ifdepth--;
                else if (inst.InstructionType == AdhocInstructionType.MODULE_DEFINE)
                    modOrClass.Push((inst as InsModuleDefine).Names[^1].Name);
                else if (inst.InstructionType == AdhocInstructionType.CLASS_DEFINE)
                    modOrClass.Push((inst as InsClassDefine).Name.Name);
                else if (inst.InstructionType == AdhocInstructionType.TRY_CATCH)
                    modOrClass.Push("TryCatch");
                else if (inst.InstructionType == AdhocInstructionType.MODULE_CONSTRUCTOR)
                    modOrClass.Push("Module Constructor");
                else if (inst is InsSetState state && state.State == AdhocRunState.EXIT)
                    sw.Write($"  [EXIT {modOrClass.Pop()}]");

                sw.WriteLine();
            }


            depth--;
            sw.WriteLine();
        }
    }
}
