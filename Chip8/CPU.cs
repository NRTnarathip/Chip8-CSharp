using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Chip8;

public sealed class CPU
{
    // Register
    public ushort I, PC;
    private void MoveNextOpcode() => PC += 2;
    public void MoveBackOpcode() => PC -= 2;
    private void Jump(int addr) => PC = (ushort)addr;

    // 0 -> 14. 15 it's FlagCarry
    public byte[] V = new byte[16];
    public byte V0 => V[0];
    public byte DelayTime;

    // Stack
    public readonly Stack<ushort> Stack = new();
    void PushToStack(ushort address)
    {
        if (Stack.Count >= 16)
            throw new OverflowException();

        Stack.Push(address);
    }
    ushort PopFromStack()
    {
        if (Stack.Count <= 0)
            throw new OverflowException();

        return Stack.Pop();
    }

    // Flags
    public byte FlagCarry
    {
        get => V[0xF];
        set => V[0xF] = value;
    }
    public void SetFlagCarry(bool val) => FlagCarry = val ? (byte)1 : (byte)0;
    public void SetFlagCarry(int val) => FlagCarry = (byte)val;

    // Opcodes
    public readonly Dictionary<byte, Action<Instruction>> OpcodeExecuteMap = new();

    // Ram
    public const int K_MaxRam = 4096;
    public const int K_MaxRamIndex = K_MaxRam - 1;
    public readonly byte[] Ram = new byte[K_MaxRam];

    // Random
    public readonly Random rng = new();
    public byte RandomNextByte() => (byte)rng.Next(256);

    // Keyboard key down sets
    readonly HashSet<byte> KeyDownSets = new();
    public byte GetLastKey()
    {
        if (KeyDownSets.Count > 0)
            return KeyDownSets.Last();
        return 0xFF;
    }
    public bool UpdateKeyState(byte keyHex, bool isKeyDown)
    {
        if (isKeyDown)
        {
            if (IsKeyDown(keyHex) is false)
            {
                KeyDownSets.Add(keyHex);
                return true;
            }
        }
        else
        {
            if (IsKeyDown(keyHex))
            {
                KeyDownSets.Remove(keyHex);
                return true;
            }
        }

        return false;
    }

    public bool IsKeyDown(byte keyHex) => KeyDownSets.Contains(keyHex);

    // Display
    public const int ScreenWidth = 64;
    public const int ScreenHeight = 32;
    public const int ScreenPixelSize = ScreenWidth * ScreenHeight;
    public readonly bool[,] Display = new bool[ScreenWidth, ScreenHeight];
    readonly bool[,] DisplayPendingClear = new bool[ScreenWidth, ScreenHeight];
    public bool DisplayNeedRedraw = true;

    public CPU()
    {
        #region Register Opcodes

        RegisterOpcodeImplement(0x0, (ins) =>
        {
            switch (ins.nn)
            {
                // clear screen
                case 0xE0:
                    ClearScreen_00E0();
                    break;
                case 0xEE:
                    ReturnFromSubroutine_00EE();
                    break;

                default:
                    throw new NotImplementedException();
            }
        });
        RegisterOpcodeImplement(0x1, Jump_1NNN);
        RegisterOpcodeImplement(0x2, CallSubroutine_2NNN);
        RegisterOpcodeImplement(0x3, SkipIfXEqualNN_3XNN);
        RegisterOpcodeImplement(0x4, SkipIfXNotEqualNN_4XNN);
        RegisterOpcodeImplement(0x5, SkipIfXEqualY_5XY0);
        RegisterOpcodeImplement(0x6, SetVX_6XNN);
        RegisterOpcodeImplement(0x7, AddVX_7XNN);
        RegisterOpcodeImplement(0x8, Arithmetic);
        RegisterOpcodeImplement(0x9, SkipIfVXNotEqualVY_9XY0);
        RegisterOpcodeImplement(0xA, ANNN_LoadI);
        RegisterOpcodeImplement(0xB, BNNN_JumpWithOffset);
        RegisterOpcodeImplement(0xC, CXNN_Random);
        RegisterOpcodeImplement(0xD, DXYN_DrawSprite);
        RegisterOpcodeImplement(0xE, SkipIfKeyDown_EX);
        RegisterOpcodeImplement(0xF, HandleOpcodeF);

        #endregion
    }

    #region Instruction Handlers

    private void SkipIfXEqualNN_3XNN(Instruction i)
    {
        if (V[i.x] == i.nn)
            MoveNextOpcode();
    }

    void SkipIfXNotEqualNN_4XNN(Instruction i)
    {
        if (V[i.x] != i.nn)
            MoveNextOpcode();
    }

    private void SkipIfXEqualY_5XY0(Instruction i)
    {
        if (V[i.x] == V[i.y])
            MoveNextOpcode();
    }

    private void HandleOpcodeF(Instruction i)
    {
        switch (i.nn)
        {
            case 0x07:
                V[i.x] = DelayTime;
                break;
            case 0x0A: // Wait for key
                var firstKey = GetLastKey();
                if (firstKey != 0xFF)
                {
                    V[i.x] = firstKey;
                }
                // freeze current opcode, until any press key
                else
                {
                    MoveBackOpcode();
                }

                break;
            case 0x15:
                DelayTime = V[i.x];
                break;
            case 0x1E:
                I += V[i.x];
                break;
            case 0x29: // read font
                I = (ushort)(V[i.x] * 5);
                break;
            case 0x33: // Binary-coded decimal
                var x = V[i.x];
                Ram[I + 0] = (byte)((x / 100) % 10);
                Ram[I + 1] = (byte)((x / 10) % 10);
                Ram[I + 2] = (byte)(x % 10);
                break;
            case 0x55:
                // set VX to I
                for (var index = 0; index <= i.x; index++)
                    Ram[I + index] = V[index];
                break;
            case 0x65:
                // set I to VX
                for (var index = 0; index <= i.x; index++)
                    V[index] = Ram[I + index];
                break;

            default:
                throw new NotImplementedException("!!Not implement opcode: " + i.opcode);
        }
    }

    // 0xEX9E, 0xEXA1
    private void SkipIfKeyDown_EX(Instruction i)
    {
        byte keyHex = V[i.x];
        bool isKeyDown = IsKeyDown(keyHex);
        bool skip = i.nn == 0x9E ? isKeyDown : !isKeyDown;
        if (skip)
            MoveNextOpcode();
    }

    // original code
    // https://github.com/DanTup/DaChip8/blob/d6bd0edefcd4e463069e8f8f91b740c40d3f1ffe/DaChip8/Chip8.cs#L326
    private void DXYN_DrawSprite(Instruction inst)
    {
        var startX = V[inst.x];
        var startY = V[inst.y];

        // Write any pending clears
        for (var x = 0; x < ScreenWidth; x++)
        {
            for (var y = 0; y < ScreenHeight; y++)
            {
                if (DisplayPendingClear[x, y])
                {
                    if (Display[x, y])
                        DisplayNeedRedraw = true;

                    DisplayPendingClear[x, y] = false;
                    Display[x, y] = false;
                }
            }
        }

        FlagCarry = 0;
        for (int lineYIndex = 0; lineYIndex < inst.n; lineYIndex++)
        {
            var spriteLine = Ram[I + lineYIndex];

            for (var lineX = 0; lineX < 8; lineX++)
            {
                int x = (startX + lineX) % ScreenWidth;
                int y = (startY + lineYIndex) % ScreenHeight;

                var spriteBit = ((spriteLine >> (7 - lineX)) & 1);
                var oldBit = Display[x, y] ? 1 : 0;

                if (oldBit != spriteBit)
                    DisplayNeedRedraw = true;

                // New bit is XOR of existing and new.
                var newBit = oldBit ^ spriteBit;

                if (newBit != 0)
                    Display[x, y] = true;
                else // Otherwise write a pending clear
                    DisplayPendingClear[x, y] = true;

                // If we wiped out a pixel, set flag for collision.
                if (oldBit != 0 && newBit == 0)
                    FlagCarry = 1;
            }
        }
    }

    private void CXNN_Random(Instruction i)
    {
        var randValue = RandomNextByte();
        V[i.x] = (byte)(randValue & i.nn);
    }

    private void BNNN_JumpWithOffset(Instruction i)
    {
        Jump(i.nnn + V0);
    }

    private void ANNN_LoadI(Instruction i)
    {
        I = i.nnn;
    }

    private void SkipIfVXNotEqualVY_9XY0(Instruction i)
    {
        if (V[i.x] != V[i.y])
            MoveNextOpcode();
    }

    void Arithmetic(Instruction i)
    {
        byte x = V[i.x];
        byte y = V[i.y];

        switch (i.n)
        {
            case 0x0: // 8XY0
                x = y;
                break;
            case 0x1: // 8XY1
                x |= y;
                break;
            case 0x2: // 8XY2
                x &= y;
                break;
            case 0x3:// 8XY2
                x ^= y;
                break;
            case 0x4: // 8XY4 Add Vx, Vy
                // overflow?
                SetFlagCarry(x + y >= 0xFF);
                x += y;
                break;
            case 0x5: // 8XY5 Sub Vx, Vy
                // underflow?
                SetFlagCarry(x > y);
                x -= y;
                break;
            case 0x6: // 8XY6 Shift right
                SetFlagCarry((x & 0x1) != 0);
                x >>= 1;
                break;
            case 0x7: // 8XY7 SubN Vx, Vy
                // underflow?
                SetFlagCarry(y > x);
                y -= x;
                break;
            case 0xE: // 8XYE Shift left
                SetFlagCarry((x & 0xF) != 0);
                x <<= 1;
                break;
            default:
                throw new NotImplementedException("not implement opcode: " + i);
        }

        V[i.x] = x;
        V[i.y] = y;
    }

    void SetVX_6XNN(Instruction i)
    {
        V[i.x] = i.nn;
    }

    void AddVX_7XNN(Instruction i)
    {
        V[i.x] += i.nn;
    }

    void RegisterOpcodeImplement(byte prefixOpcode, Action<Instruction> callback)
    {
        OpcodeExecuteMap.Add(prefixOpcode, callback);
    }

    private void Jump_1NNN(Instruction i)
    {
        PC = i.nnn;
    }

    private void CallSubroutine_2NNN(Instruction i)
    {
        PushToStack(PC);
        PC = i.nnn;
    }

    private void ReturnFromSubroutine_00EE()
    {
        PC = PopFromStack();
    }

    private void ClearScreen_00E0()
    {
        for (var y = 0; y < ScreenHeight; y++)
            for (var x = 0; x < ScreenWidth; x++)
                Display[x, y] = false;
    }

    #endregion Instruction Handlers

    public void LoadFont(byte[] fonts)
    {
        // load fonts
        const int K_FontSize = 0x50;
        if (fonts.Length != K_FontSize)
        {
            Console.WriteLine("Error: fonts length is not " + K_FontSize.ToString("X"));
            return;
        }

        const int K_LoadFontStartAddress = 0x0;
        Array.Copy(fonts, 0, Ram, K_LoadFontStartAddress, fonts.Length);
        Console.WriteLine("loaded fonts");
    }

    public Instruction? lastInstruction;
    public void Cycle()
    {
        var currentOpcode = ReadOpcode();
        // error
        if (currentOpcode == 0)
        {
            Console.WriteLine("error, current opcode is null");
            // freeze opcode
            MoveBackOpcode();
            //Console.WriteLine("last instruction opcode: " + currentInstruction?.opcode.ToString("X"));
            return;
        }

        var prevOpcodeString = lastInstruction?.ToString();
        lastInstruction = new Instruction(currentOpcode);
        if (lastInstruction.ToString() == prevOpcodeString)
        {
            //Console.WriteLine("freezing opcode...: " + currentInstruction);
        }

        if (OpcodeExecuteMap.TryGetValue(lastInstruction.firstN, out var exeCallback))
        {
            try
            {
                Console.WriteLine("execute opcode: " + lastInstruction);
                exeCallback(lastInstruction);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: try execute opcode: " + lastInstruction);
                Console.WriteLine(ex);
            }
        }
        else
        {
            MoveBackOpcode();
            Console.WriteLine("not found implement for opcode: " + lastInstruction);
        }
    }

    public void OnTick60HZ()
    {
        Cycle();

        if (DelayTime > 0)
            DelayTime--;
    }
    public ushort ReadOpcode()
    {
        // read 2 byte, PC + 1
        if (PC + 1 > K_MaxRamIndex)
        {
            Console.WriteLine("PC register overflow");
            return 0;
        }

        byte b1 = Ram[PC];
        byte b2 = Ram[PC + 1];
        MoveNextOpcode();

        return (ushort)(b1 << 8 | b2);
    }

    public bool lastRomLoaded = false;
    public bool LoadRom(string path)
    {
        Console.WriteLine("Loading rom path: " + path);
        lastRomLoaded = false;

        if (File.Exists(path) is false)
        {
            Console.WriteLine("not found rom at path: " + path);
            return false;
        }

        var bytes = File.ReadAllBytes(path);

        // load rom

        const int K_EntryPointAddress = 0x200;
        Array.Copy(bytes, 0, Ram, K_EntryPointAddress, bytes.Length);
        Console.WriteLine("copy rom into ram");

        // load fonts
        LoadFont(FontSets.FontDefault);

        // setup register
        PC = K_EntryPointAddress;
        I = 0;
        DelayTime = 0;

        Console.WriteLine("Successfully loaded & setup");

        lastRomLoaded = true;

        return true;
    }
}
