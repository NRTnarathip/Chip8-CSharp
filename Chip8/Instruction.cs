namespace Chip8;

public sealed class Instruction
{
    public readonly ushort raw;
    public readonly ushort nnn;
    public readonly byte nn, n;
    public readonly byte x, y;

    public Instruction(ushort raw)
    {
        this.raw = raw;
        nnn = (ushort)(raw & 0x0FFF);
        nn = (byte)(raw & 0x00FF);
        n = (byte)(raw & 0x000F);
        x = (byte)((raw & 0x0F00) >> 8);
        y = (byte)((raw & 0x00F0) >> 4);
    }
}
