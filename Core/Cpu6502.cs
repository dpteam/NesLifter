namespace NesLifter.Core;

public enum AddrMode
{
    Imp,
    Acc,
    Imm,
    Zp,
    ZpX,
    ZpY,
    Abs,
    AbsX,
    AbsY,
    Ind,
    XInd,
    IndY,
    Rel
}

public enum OpControl
{
    Normal,
    Branch,
    Jmp,
    JmpInd,
    Jsr,
    Rts,
    Rti,
    Brk,
    Invalid
}

public sealed class OpInfo
{
    public byte Opcode { get; }
    public string Mn { get; }
    public byte Len { get; }
    public AddrMode Mode { get; }
    public OpControl Ctrl { get; }
    public byte Cycles { get; }

    public OpInfo(
        byte opcode,
        string mn,
        byte len,
        AddrMode mode,
        OpControl ctrl,
        byte cycles)
    {
        Opcode = opcode;
        Mn = mn;
        Len = len;
        Mode = mode;
        Ctrl = ctrl;
        Cycles = cycles;
    }
}

public static class Cpu6502
{
    public static readonly OpInfo[] Table = new OpInfo[256];

    static Cpu6502()
    {
        foreach (var d in Definitions)
        {
            Table[d.Op] = new OpInfo(d.Op, d.Mn, d.Len, d.Mode, d.Ctrl, d.Cycles);
        }
    }

    private static readonly (
        byte Op,
        string Mn,
        byte Len,
        AddrMode Mode,
        OpControl Ctrl,
        byte Cycles
    )[] Definitions =
    {
        // Official opcodes
        (0x00, "BRK", 2, AddrMode.Imp, OpControl.Brk, 7),
        (0x01, "ORA", 2, AddrMode.XInd, OpControl.Normal, 6),
        (0x05, "ORA", 2, AddrMode.Zp, OpControl.Normal, 3),
        (0x06, "ASL", 2, AddrMode.Zp, OpControl.Normal, 5),
        (0x08, "PHP", 1, AddrMode.Imp, OpControl.Normal, 3),
        (0x0A, "ASL", 1, AddrMode.Acc, OpControl.Normal, 2),
        (0x0D, "ORA", 3, AddrMode.Abs, OpControl.Normal, 4),
        (0x0E, "ASL", 3, AddrMode.Abs, OpControl.Normal, 6),

        (0x10, "BPL", 2, AddrMode.Rel, OpControl.Branch, 2),
        (0x11, "ORA", 2, AddrMode.IndY, OpControl.Normal, 5),
        (0x15, "ORA", 2, AddrMode.ZpX, OpControl.Normal, 4),
        (0x16, "ASL", 2, AddrMode.ZpX, OpControl.Normal, 6),
        (0x18, "CLC", 1, AddrMode.Imp, OpControl.Normal, 2),
        (0x19, "ORA", 3, AddrMode.AbsY, OpControl.Normal, 4),
        (0x1D, "ORA", 3, AddrMode.AbsX, OpControl.Normal, 4),
        (0x1E, "ASL", 3, AddrMode.AbsX, OpControl.Normal, 7),

        (0x20, "JSR", 3, AddrMode.Abs, OpControl.Jsr, 6),
        (0x21, "AND", 2, AddrMode.XInd, OpControl.Normal, 6),
        (0x24, "BIT", 2, AddrMode.Zp, OpControl.Normal, 3),
        (0x25, "AND", 2, AddrMode.Zp, OpControl.Normal, 3),
        (0x26, "ROL", 2, AddrMode.Zp, OpControl.Normal, 5),
        (0x28, "PLP", 1, AddrMode.Imp, OpControl.Normal, 4),
        (0x2A, "ROL", 1, AddrMode.Acc, OpControl.Normal, 2),
        (0x2C, "BIT", 3, AddrMode.Abs, OpControl.Normal, 4),
        (0x2D, "AND", 3, AddrMode.Abs, OpControl.Normal, 4),
        (0x2E, "ROL", 3, AddrMode.Abs, OpControl.Normal, 6),

        (0x30, "BMI", 2, AddrMode.Rel, OpControl.Branch, 2),
        (0x31, "AND", 2, AddrMode.IndY, OpControl.Normal, 5),
        (0x35, "AND", 2, AddrMode.ZpX, OpControl.Normal, 4),
        (0x36, "ROL", 2, AddrMode.ZpX, OpControl.Normal, 6),
        (0x38, "SEC", 1, AddrMode.Imp, OpControl.Normal, 2),
        (0x39, "AND", 3, AddrMode.AbsY, OpControl.Normal, 4),
        (0x3D, "AND", 3, AddrMode.AbsX, OpControl.Normal, 4),
        (0x3E, "ROL", 3, AddrMode.AbsX, OpControl.Normal, 7),

        (0x40, "RTI", 1, AddrMode.Imp, OpControl.Rti, 6),
        (0x41, "EOR", 2, AddrMode.XInd, OpControl.Normal, 6),
        (0x45, "EOR", 2, AddrMode.Zp, OpControl.Normal, 3),
        (0x46, "LSR", 2, AddrMode.Zp, OpControl.Normal, 5),
        (0x48, "PHA", 1, AddrMode.Imp, OpControl.Normal, 3),
        (0x4A, "LSR", 1, AddrMode.Acc, OpControl.Normal, 2),
        (0x4C, "JMP", 3, AddrMode.Abs, OpControl.Jmp, 3),
        (0x4D, "EOR", 3, AddrMode.Abs, OpControl.Normal, 4),
        (0x4E, "LSR", 3, AddrMode.Abs, OpControl.Normal, 6),

        (0x50, "BVC", 2, AddrMode.Rel, OpControl.Branch, 2),
        (0x51, "EOR", 2, AddrMode.IndY, OpControl.Normal, 5),
        (0x55, "EOR", 2, AddrMode.ZpX, OpControl.Normal, 4),
        (0x56, "LSR", 2, AddrMode.ZpX, OpControl.Normal, 6),
        (0x58, "CLI", 1, AddrMode.Imp, OpControl.Normal, 2),
        (0x59, "EOR", 3, AddrMode.AbsY, OpControl.Normal, 4),
        (0x5D, "EOR", 3, AddrMode.AbsX, OpControl.Normal, 4),
        (0x5E, "LSR", 3, AddrMode.AbsX, OpControl.Normal, 7),

        (0x60, "RTS", 1, AddrMode.Imp, OpControl.Rts, 6),
        (0x61, "ADC", 2, AddrMode.XInd, OpControl.Normal, 6),
        (0x65, "ADC", 2, AddrMode.Zp, OpControl.Normal, 3),
        (0x66, "ROR", 2, AddrMode.Zp, OpControl.Normal, 5),
        (0x68, "PLA", 1, AddrMode.Imp, OpControl.Normal, 4),
        (0x6A, "ROR", 1, AddrMode.Acc, OpControl.Normal, 2),
        (0x6C, "JMP", 3, AddrMode.Ind, OpControl.JmpInd, 5),
        (0x6D, "ADC", 3, AddrMode.Abs, OpControl.Normal, 4),
        (0x6E, "ROR", 3, AddrMode.Abs, OpControl.Normal, 6),

        (0x70, "BVS", 2, AddrMode.Rel, OpControl.Branch, 2),
        (0x71, "ADC", 2, AddrMode.IndY, OpControl.Normal, 5),
        (0x75, "ADC", 2, AddrMode.ZpX, OpControl.Normal, 4),
        (0x76, "ROR", 2, AddrMode.ZpX, OpControl.Normal, 6),
        (0x78, "SEI", 1, AddrMode.Imp, OpControl.Normal, 2),
        (0x79, "ADC", 3, AddrMode.AbsY, OpControl.Normal, 4),
        (0x7D, "ADC", 3, AddrMode.AbsX, OpControl.Normal, 4),
        (0x7E, "ROR", 3, AddrMode.AbsX, OpControl.Normal, 7),

        (0x81, "STA", 2, AddrMode.XInd, OpControl.Normal, 6),
        (0x84, "STY", 2, AddrMode.Zp, OpControl.Normal, 3),
        (0x85, "STA", 2, AddrMode.Zp, OpControl.Normal, 3),
        (0x86, "STX", 2, AddrMode.Zp, OpControl.Normal, 3),
        (0x88, "DEY", 1, AddrMode.Imp, OpControl.Normal, 2),
        (0x8A, "TXA", 1, AddrMode.Imp, OpControl.Normal, 2),
        (0x8C, "STY", 3, AddrMode.Abs, OpControl.Normal, 4),
        (0x8D, "STA", 3, AddrMode.Abs, OpControl.Normal, 4),
        (0x8E, "STX", 3, AddrMode.Abs, OpControl.Normal, 4),

        (0x90, "BCC", 2, AddrMode.Rel, OpControl.Branch, 2),
        (0x91, "STA", 2, AddrMode.IndY, OpControl.Normal, 6),
        (0x94, "STY", 2, AddrMode.ZpX, OpControl.Normal, 4),
        (0x95, "STA", 2, AddrMode.ZpX, OpControl.Normal, 4),
        (0x96, "STX", 2, AddrMode.ZpY, OpControl.Normal, 4),
        (0x98, "TYA", 1, AddrMode.Imp, OpControl.Normal, 2),
        (0x99, "STA", 3, AddrMode.AbsY, OpControl.Normal, 5),
        (0x9A, "TXS", 1, AddrMode.Imp, OpControl.Normal, 2),
        (0x9D, "STA", 3, AddrMode.AbsX, OpControl.Normal, 5),

        (0xA0, "LDY", 2, AddrMode.Imm, OpControl.Normal, 2),
        (0xA1, "LDA", 2, AddrMode.XInd, OpControl.Normal, 6),
        (0xA2, "LDX", 2, AddrMode.Imm, OpControl.Normal, 2),
        (0xA4, "LDY", 2, AddrMode.Zp, OpControl.Normal, 3),
        (0xA5, "LDA", 2, AddrMode.Zp, OpControl.Normal, 3),
        (0xA6, "LDX", 2, AddrMode.Zp, OpControl.Normal, 3),
        (0xA8, "TAY", 1, AddrMode.Imp, OpControl.Normal, 2),
        (0xAA, "TAX", 1, AddrMode.Imp, OpControl.Normal, 2),
        (0xAC, "LDY", 3, AddrMode.Abs, OpControl.Normal, 4),
        (0xAD, "LDA", 3, AddrMode.Abs, OpControl.Normal, 4),
        (0xAE, "LDX", 3, AddrMode.Abs, OpControl.Normal, 4),

        (0xB0, "BCS", 2, AddrMode.Rel, OpControl.Branch, 2),
        (0xB1, "LDA", 2, AddrMode.IndY, OpControl.Normal, 5),
        (0xB4, "LDY", 2, AddrMode.ZpX, OpControl.Normal, 4),
        (0xB5, "LDA", 2, AddrMode.ZpX, OpControl.Normal, 4),
        (0xB6, "LDX", 2, AddrMode.ZpY, OpControl.Normal, 4),
        (0xB8, "CLV", 1, AddrMode.Imp, OpControl.Normal, 2),
        (0xB9, "LDA", 3, AddrMode.AbsY, OpControl.Normal, 4),
        (0xBA, "TSX", 1, AddrMode.Imp, OpControl.Normal, 2),
        (0xBC, "LDY", 3, AddrMode.AbsX, OpControl.Normal, 4),
        (0xBD, "LDA", 3, AddrMode.AbsX, OpControl.Normal, 4),
        (0xBE, "LDX", 3, AddrMode.AbsY, OpControl.Normal, 4),

        (0xC0, "CPY", 2, AddrMode.Imm, OpControl.Normal, 2),
        (0xC1, "CMP", 2, AddrMode.XInd, OpControl.Normal, 6),
        (0xC4, "CPY", 2, AddrMode.Zp, OpControl.Normal, 3),
        (0xC5, "CMP", 2, AddrMode.Zp, OpControl.Normal, 3),
        (0xC6, "DEC", 2, AddrMode.Zp, OpControl.Normal, 5),
        (0xC8, "INY", 1, AddrMode.Imp, OpControl.Normal, 2),
        (0xCA, "DEX", 1, AddrMode.Imp, OpControl.Normal, 2),
        (0xCC, "CPY", 3, AddrMode.Abs, OpControl.Normal, 4),
        (0xCD, "CMP", 3, AddrMode.Abs, OpControl.Normal, 4),
        (0xCE, "DEC", 3, AddrMode.Abs, OpControl.Normal, 6),

        (0xD0, "BNE", 2, AddrMode.Rel, OpControl.Branch, 2),
        (0xD1, "CMP", 2, AddrMode.IndY, OpControl.Normal, 5),
        (0xD5, "CMP", 2, AddrMode.ZpX, OpControl.Normal, 4),
        (0xD6, "DEC", 2, AddrMode.ZpX, OpControl.Normal, 6),
        (0xD8, "CLD", 1, AddrMode.Imp, OpControl.Normal, 2),
        (0xD9, "CMP", 3, AddrMode.AbsY, OpControl.Normal, 4),
        (0xDD, "CMP", 3, AddrMode.AbsX, OpControl.Normal, 4),
        (0xDE, "DEC", 3, AddrMode.AbsX, OpControl.Normal, 7),

        (0xE0, "CPX", 2, AddrMode.Imm, OpControl.Normal, 2),
        (0xE1, "SBC", 2, AddrMode.XInd, OpControl.Normal, 6),
        (0xE4, "CPX", 2, AddrMode.Zp, OpControl.Normal, 3),
        (0xE5, "SBC", 2, AddrMode.Zp, OpControl.Normal, 3),
        (0xE6, "INC", 2, AddrMode.Zp, OpControl.Normal, 5),
        (0xE8, "INX", 1, AddrMode.Imp, OpControl.Normal, 2),
        (0xEA, "NOP", 1, AddrMode.Imp, OpControl.Normal, 2),
        (0xEC, "CPX", 3, AddrMode.Abs, OpControl.Normal, 4),
        (0xED, "SBC", 3, AddrMode.Abs, OpControl.Normal, 4),
        (0xEE, "INC", 3, AddrMode.Abs, OpControl.Normal, 6),

        (0xF0, "BEQ", 2, AddrMode.Rel, OpControl.Branch, 2),
        (0xF1, "SBC", 2, AddrMode.IndY, OpControl.Normal, 5),
        (0xF5, "SBC", 2, AddrMode.ZpX, OpControl.Normal, 4),
        (0xF6, "INC", 2, AddrMode.ZpX, OpControl.Normal, 6),
        (0xF8, "SED", 1, AddrMode.Imp, OpControl.Normal, 2),
        (0xF9, "SBC", 3, AddrMode.AbsY, OpControl.Normal, 4),
        (0xFD, "SBC", 3, AddrMode.AbsX, OpControl.Normal, 4),
        (0xFE, "INC", 3, AddrMode.AbsX, OpControl.Normal, 7),

        // Immediate mode
        (0x09, "ORA", 2, AddrMode.Imm, OpControl.Normal, 2),
        (0x29, "AND", 2, AddrMode.Imm, OpControl.Normal, 2),
        (0x49, "EOR", 2, AddrMode.Imm, OpControl.Normal, 2),
        (0x69, "ADC", 2, AddrMode.Imm, OpControl.Normal, 2),
        (0xA9, "LDA", 2, AddrMode.Imm, OpControl.Normal, 2),
        (0xC9, "CMP", 2, AddrMode.Imm, OpControl.Normal, 2),
        (0xE9, "SBC", 2, AddrMode.Imm, OpControl.Normal, 2),

        // Unofficial NOP variants
        (0x1C, "NOP", 3, AddrMode.AbsX, OpControl.Normal, 4),
        (0x3C, "NOP", 3, AddrMode.AbsX, OpControl.Normal, 4),
        (0x5C, "NOP", 3, AddrMode.AbsX, OpControl.Normal, 4),
        (0x7C, "NOP", 3, AddrMode.AbsX, OpControl.Normal, 4),
        (0xDC, "NOP", 3, AddrMode.AbsX, OpControl.Normal, 4),
        (0xFC, "NOP", 3, AddrMode.AbsX, OpControl.Normal, 4),

        (0x1A, "NOP", 1, AddrMode.Imp, OpControl.Normal, 2),
        (0x3A, "NOP", 1, AddrMode.Imp, OpControl.Normal, 2),
        (0x5A, "NOP", 1, AddrMode.Imp, OpControl.Normal, 2),
        (0x7A, "NOP", 1, AddrMode.Imp, OpControl.Normal, 2),
        (0xDA, "NOP", 1, AddrMode.Imp, OpControl.Normal, 2),
        (0xFA, "NOP", 1, AddrMode.Imp, OpControl.Normal, 2),

        (0x80, "NOP", 2, AddrMode.Imm, OpControl.Normal, 2),
        (0x82, "NOP", 2, AddrMode.Imm, OpControl.Normal, 2),
        (0x89, "NOP", 2, AddrMode.Imm, OpControl.Normal, 2),
        (0xC2, "NOP", 2, AddrMode.Imm, OpControl.Normal, 2),
        (0xE2, "NOP", 2, AddrMode.Imm, OpControl.Normal, 2),

        (0x04, "NOP", 2, AddrMode.Zp, OpControl.Normal, 3),
        (0x44, "NOP", 2, AddrMode.Zp, OpControl.Normal, 3),
        (0x64, "NOP", 2, AddrMode.Zp, OpControl.Normal, 3),

        (0x14, "NOP", 2, AddrMode.ZpX, OpControl.Normal, 4),
        (0x34, "NOP", 2, AddrMode.ZpX, OpControl.Normal, 4),
        (0x54, "NOP", 2, AddrMode.ZpX, OpControl.Normal, 4),
        (0x74, "NOP", 2, AddrMode.ZpX, OpControl.Normal, 4),
        (0xD4, "NOP", 2, AddrMode.ZpX, OpControl.Normal, 4),
        (0xF4, "NOP", 2, AddrMode.ZpX, OpControl.Normal, 4),

        (0x0C, "NOP", 3, AddrMode.Abs, OpControl.Normal, 4),

        // Unofficial: LAX
        (0xA7, "LAX", 2, AddrMode.Zp, OpControl.Normal, 3),
        (0xB7, "LAX", 2, AddrMode.ZpY, OpControl.Normal, 4),
        (0xAF, "LAX", 3, AddrMode.Abs, OpControl.Normal, 4),
        (0xBF, "LAX", 3, AddrMode.AbsY, OpControl.Normal, 4),
        (0xA3, "LAX", 2, AddrMode.XInd, OpControl.Normal, 6),
        (0xB3, "LAX", 2, AddrMode.IndY, OpControl.Normal, 5),

        // Unofficial: SAX
        (0x87, "SAX", 2, AddrMode.Zp, OpControl.Normal, 3),
        (0x97, "SAX", 2, AddrMode.ZpY, OpControl.Normal, 4),
        (0x8F, "SAX", 3, AddrMode.Abs, OpControl.Normal, 4),
        (0x83, "SAX", 2, AddrMode.XInd, OpControl.Normal, 6),

        // Unofficial: DCP
        (0xC7, "DCP", 2, AddrMode.Zp, OpControl.Normal, 5),
        (0xD7, "DCP", 2, AddrMode.ZpX, OpControl.Normal, 6),
        (0xCF, "DCP", 3, AddrMode.Abs, OpControl.Normal, 6),
        (0xDF, "DCP", 3, AddrMode.AbsX, OpControl.Normal, 7),
        (0xDB, "DCP", 3, AddrMode.AbsY, OpControl.Normal, 7),
        (0xC3, "DCP", 2, AddrMode.XInd, OpControl.Normal, 8),
        (0xD3, "DCP", 2, AddrMode.IndY, OpControl.Normal, 8),

        // Unofficial: ISB
        (0xE7, "ISB", 2, AddrMode.Zp, OpControl.Normal, 5),
        (0xF7, "ISB", 2, AddrMode.ZpX, OpControl.Normal, 6),
        (0xEF, "ISB", 3, AddrMode.Abs, OpControl.Normal, 6),
        (0xFF, "ISB", 3, AddrMode.AbsX, OpControl.Normal, 7),
        (0xFB, "ISB", 3, AddrMode.AbsY, OpControl.Normal, 7),
        (0xE3, "ISB", 2, AddrMode.XInd, OpControl.Normal, 8),
        (0xF3, "ISB", 2, AddrMode.IndY, OpControl.Normal, 8),

        // Unofficial: SLO
        (0x07, "SLO", 2, AddrMode.Zp, OpControl.Normal, 5),
        (0x17, "SLO", 2, AddrMode.ZpX, OpControl.Normal, 6),
        (0x0F, "SLO", 3, AddrMode.Abs, OpControl.Normal, 6),
        (0x1F, "SLO", 3, AddrMode.AbsX, OpControl.Normal, 7),
        (0x1B, "SLO", 3, AddrMode.AbsY, OpControl.Normal, 7),
        (0x03, "SLO", 2, AddrMode.XInd, OpControl.Normal, 8),
        (0x13, "SLO", 2, AddrMode.IndY, OpControl.Normal, 8),

        // Unofficial: RLA
        (0x27, "RLA", 2, AddrMode.Zp, OpControl.Normal, 5),
        (0x37, "RLA", 2, AddrMode.ZpX, OpControl.Normal, 6),
        (0x2F, "RLA", 3, AddrMode.Abs, OpControl.Normal, 6),
        (0x3F, "RLA", 3, AddrMode.AbsX, OpControl.Normal, 7),
        (0x3B, "RLA", 3, AddrMode.AbsY, OpControl.Normal, 7),
        (0x23, "RLA", 2, AddrMode.XInd, OpControl.Normal, 8),
        (0x33, "RLA", 2, AddrMode.IndY, OpControl.Normal, 8),

        // Unofficial: SRE
        (0x47, "SRE", 2, AddrMode.Zp, OpControl.Normal, 5),
        (0x57, "SRE", 2, AddrMode.ZpX, OpControl.Normal, 6),
        (0x4F, "SRE", 3, AddrMode.Abs, OpControl.Normal, 6),
        (0x5F, "SRE", 3, AddrMode.AbsX, OpControl.Normal, 7),
        (0x5B, "SRE", 3, AddrMode.AbsY, OpControl.Normal, 7),
        (0x43, "SRE", 2, AddrMode.XInd, OpControl.Normal, 8),
        (0x53, "SRE", 2, AddrMode.IndY, OpControl.Normal, 8),

        // Unofficial: RRA
        (0x67, "RRA", 2, AddrMode.Zp, OpControl.Normal, 5),
        (0x77, "RRA", 2, AddrMode.ZpX, OpControl.Normal, 6),
        (0x6F, "RRA", 3, AddrMode.Abs, OpControl.Normal, 6),
        (0x7F, "RRA", 3, AddrMode.AbsX, OpControl.Normal, 7),
        (0x7B, "RRA", 3, AddrMode.AbsY, OpControl.Normal, 7),
        (0x63, "RRA", 2, AddrMode.XInd, OpControl.Normal, 8),
        (0x73, "RRA", 2, AddrMode.IndY, OpControl.Normal, 8),

        // KIL / JAM
        (0x02, "KIL", 1, AddrMode.Imp, OpControl.Normal, 1),
        (0x12, "KIL", 1, AddrMode.Imp, OpControl.Normal, 1),
        (0x22, "KIL", 1, AddrMode.Imp, OpControl.Normal, 1),
        (0x32, "KIL", 1, AddrMode.Imp, OpControl.Normal, 1),
        (0x42, "KIL", 1, AddrMode.Imp, OpControl.Normal, 1),
        (0x52, "KIL", 1, AddrMode.Imp, OpControl.Normal, 1),
        (0x62, "KIL", 1, AddrMode.Imp, OpControl.Normal, 1),
        (0x72, "KIL", 1, AddrMode.Imp, OpControl.Normal, 1),
        (0x92, "KIL", 1, AddrMode.Imp, OpControl.Normal, 1),
        (0xB2, "KIL", 1, AddrMode.Imp, OpControl.Normal, 1),
        (0xD2, "KIL", 1, AddrMode.Imp, OpControl.Normal, 1),
        (0xF2, "KIL", 1, AddrMode.Imp, OpControl.Normal, 1),

        // Other unofficial
        (0x93, "AXA", 2, AddrMode.IndY, OpControl.Normal, 6),
        (0x9F, "AXA", 3, AddrMode.AbsY, OpControl.Normal, 5),

        (0x0B, "ANC", 2, AddrMode.Imm, OpControl.Normal, 2),
        (0x2B, "ANC", 2, AddrMode.Imm, OpControl.Normal, 2),

        (0x4B, "ALR", 2, AddrMode.Imm, OpControl.Normal, 2),
        (0x6B, "ARR", 2, AddrMode.Imm, OpControl.Normal, 2),

        (0x8B, "ANE", 2, AddrMode.Imm, OpControl.Normal, 2),
        (0xAB, "LXA", 2, AddrMode.Imm, OpControl.Normal, 2),
        (0xCB, "AXS", 2, AddrMode.Imm, OpControl.Normal, 2),

        (0xEB, "SBC", 2, AddrMode.Imm, OpControl.Normal, 2),

        (0x9C, "SHY", 3, AddrMode.AbsX, OpControl.Normal, 5),
        (0x9E, "SHY", 3, AddrMode.AbsX, OpControl.Normal, 5),

        (0xBB, "LAS", 3, AddrMode.AbsY, OpControl.Normal, 4),
    };
}