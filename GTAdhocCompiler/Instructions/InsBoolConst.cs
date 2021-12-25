﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GTAdhocCompiler.Instructions
{
    public class InsBoolConst : InstructionBase
    {
        public static readonly InsBoolConst True = new(true);
        public static readonly InsBoolConst False = new(false);

        public override AdhocInstructionType InstructionType => AdhocInstructionType.BOOL_CONST;

        public override string InstructionName => "BOOL_CONST";

        public bool Value { get; set; }
        public InsBoolConst(bool value)
        {
            Value = value;
        }
    }
}
