namespace Chip8;

public sealed class Instruction
{
    public readonly ushort nnn;
    public readonly byte nn, n;
    public readonly byte x, y;
    public readonly ushort opcode;
    public readonly byte firstN;
    readonly string opcodeString;

    public Instruction(ushort opcode)
    {
        this.opcode = opcode;
        firstN = (byte)(opcode >> 12);
        nnn = (ushort)(opcode & 0x0FFF);
        nn = (byte)(opcode & 0x00FF);
        n = (byte)(opcode & 0x000F);
        x = (byte)((opcode & 0x0F00) >> 8);
        y = (byte)((opcode & 0x00F0) >> 4);

        opcodeString = "0x" + opcode.ToString("X4");
    }
    public override string ToString() => opcodeString;
}
