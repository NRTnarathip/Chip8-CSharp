using Chip8;

internal class Program
{
    private static void Main(string[] args)
    {
        var cpu = new CPU();

        byte lastOne = 0b1111_0000;
        lastOne >>= 7;

        int delayMs = 16;
        while (true)
        {
            cpu.Tick();

            Thread.Sleep(delayMs);
        }

        Console.ReadKey();
    }
}