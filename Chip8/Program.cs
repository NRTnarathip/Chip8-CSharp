using Chip8;

internal class Program
{
    private static void Main(string[] args)
    {
        var cpu = new CPU();

        var gamePath = @"C:\Users\narat\Documents\Gameboy Learn\chip8\br8kout.ch8";

        var romBytes = File.ReadAllBytes(gamePath);
        cpu.LoadRom(romBytes);

        int delayMs = 16;

        while (true)
        {
            cpu.Cycle();

            //Console.WriteLine($"PC: {cpu.PC}");
            //Console.WriteLine($"I: {cpu.I}");

            Thread.Sleep(delayMs);
        }

        Console.ReadKey();
    }
}