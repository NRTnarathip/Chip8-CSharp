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
    // Registry
    public ushort I, PC;
    // 0 -> 14. 15 it's FlagCarry
    public byte[] V = new byte[16];
    public byte V0 => V[0];
    public byte DelayTime;

    public readonly Stack<ushort> Stack = new();
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

    // helper
    public const int K_EntryPointAddress = 0x200;

    // Random
    public readonly Random rng = new();
    public byte RandomNextByte() => (byte)rng.Next(256);

    // Keyboard key down sets
    readonly object _keyLock = new();
    readonly HashSet<byte> KeyDownSets = new();
    public byte GetLastKey()
    {
        lock (_keyLock)
        {
            if (KeyDownSets.Count > 0)
                return KeyDownSets.Last();
            return 0xFF;
        }
    }
    public bool UpdateKeyState(byte keyHex, bool isKeyDown)
    {
        if (isKeyDown)
        {
            lock (_keyLock)
            {
                if (IsKeyDown(keyHex) is false)
                {
                    KeyDownSets.Add(keyHex);
                    return true;
                }
            }
        }
        else
        {
            lock (_keyLock)
            {
                if (IsKeyDown(keyHex))
                {
                    KeyDownSets.Remove(keyHex);
                    return true;
                }
            }
        }

        return false;
    }

    public bool IsKeyDown(byte keyHex)
    {
        bool down;
        lock (_keyLock)
            down = KeyDownSets.Contains(keyHex);
        return down;
    }

    public CPU()
    {
        #region Register Opcodes

        RegisterOpcodeImplement(0x0, (ins) =>
        {
            switch (ins.nn)
            {
                // clear screen
                case 0xE0:
                    ClearScreen();
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
        RegisterOpcodeImplement(0x3, SkipIf);
        RegisterOpcodeImplement(0x4, SkipIf);
        RegisterOpcodeImplement(0x5, SkipIf);
        RegisterOpcodeImplement(0x6, SetVX);
        RegisterOpcodeImplement(0x7, AddVX);
        RegisterOpcodeImplement(0x8, Arithmetic);
        RegisterOpcodeImplement(0x9, SkipIfVXNotEqualVY);
        RegisterOpcodeImplement(0xA, ANNN_LoadI);
        RegisterOpcodeImplement(0xB, BNNN_JumpWithOffset);
        RegisterOpcodeImplement(0xC, CXNN_Random);
        RegisterOpcodeImplement(0xD, DXYN_DrawSprite);
        RegisterOpcodeImplement(0xE, SkipIfKeyDown);
        RegisterOpcodeImplement(0xF, HandleOpcodeF);

        #endregion

    }

    private void HandleOpcodeF(Instruction i)
    {
        switch (i.nn)
        {
            case 0x07:
                DelayTime = V[i.x];
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
                V[i.x] = DelayTime;
                break;
            //case 0x18:
            //    // sound 
            //    break;
            case 0x1E:
                I += V[i.x];
                break;
            case 0x29:
                I = (ushort)(V[i.x] * 5); // 0 is at 0x0, 1 is at 0x5, ...
                break;
            case 0x33: // Binary-coded decimal
                Ram[I + 0] = (byte)((V[i.x] / 100) % 10);
                Ram[I + 1] = (byte)((V[i.x] / 10) % 10);
                Ram[I + 2] = (byte)(V[i.x] % 10);
                break;
            case 0x55:
                // set VX to I
                for (var index = 0; index <= i.x; index++)
                    Ram[I + index] = V[index];

                break;
            case 0x65:
                for (var index = 0; index <= i.x; index++)
                    V[index] = Ram[I + index];
                // set I to VX
                break;

            default:
                throw new NotImplementedException("!!Not implement opcode: " + i.opcode);
        }
    }

    // 0xEX9E, 0xEXA1
    private void SkipIfKeyDown(Instruction i)
    {
        byte keyHex = V[i.x];
        bool isKeyDown = IsKeyDown(keyHex);
        bool skip = i.nn == 0x9E ? isKeyDown : !isKeyDown;
        if (skip)
            MoveNextOpcode();
    }

    public const int ScreenWidth = 64;
    public const int ScreenHeight = 32;
    public const int ScreenPixelSize = ScreenWidth * ScreenHeight;
    public readonly bool[,] Display = new bool[ScreenWidth, ScreenHeight];
    readonly bool[,] DisplayPendingClear = new bool[ScreenWidth, ScreenHeight];
    public bool DisplayNeedRedraw = true;

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
        for (int i = 0; i < inst.n; i++)
        {
            var spriteLine = Ram[I + i];

            for (var bit = 0; bit < 8; bit++)
            {
                int x = (startX + bit) % ScreenWidth;
                int y = (startY + I) % ScreenHeight;

                var spriteBit = ((spriteLine >> (7 - bit)) & 1);
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
        Jump(V0 + i.nnn);
    }

    private void Jump(int addr) => PC = (ushort)addr;

    private void ANNN_LoadI(Instruction i)
    {
        I = i.nnn;
    }

    private void SkipIfVXNotEqualVY(Instruction i)
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
            case 0x0:
                x = y;
                break;
            case 0x1:
                x |= y;
                break;
            case 0x2:
                x &= y;
                break;
            case 0x3:
                x ^= y;
                break;
            case 0x4: // Add Vx, Vy
                // overflow?
                SetFlagCarry(x + y >= 0xFF);
                x += y;
                break;
            case 0x5: // Sub Vx, Vy
                // underflow?
                SetFlagCarry(x > y);
                x -= y;
                break;
            case 0x6: // Shift right
                SetFlagCarry((x & 0x1) != 0);
                x >>= 1;
                break;
            case 0x7: // SubN Vx, Vy
                // underflow?
                SetFlagCarry(y > x);
                y -= x;
                break;
            case 0xE: // Shift left
                SetFlagCarry((x & 0xF) != 0);
                x <<= 1;
                break;
            default:
                throw new NotImplementedException("not implement opcode: " + i);
        }

        V[i.x] = x;
        V[i.y] = y;
    }

    void SetVX(Instruction i)
    {
        V[i.x] = i.nn;
    }

    void AddVX(Instruction i)
    {
        V[i.x] += i.nn;
    }

    private void SkipIf(Instruction i)
    {
        byte x = V[i.x];

        // skip next opcode,
        switch (i.firstN)
        {
            case 0x3: // skip if VX == NN
                if (x == i.nn)
                    MoveNextOpcode();
                break;
            case 0x4: // skip if VX != NN
                if (x != i.nn)
                    MoveNextOpcode();
                break;
            case 0x5: // skip if VX == VY
                if (x == V[i.y])
                    MoveNextOpcode();
                break;
            default:
                throw new NotImplementedException();
        }
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

    private void MoveNextOpcode() => PC += 2;
    public void MoveBackOpcode() => PC -= 2;

    private void ClearScreen()
    {
        for (var x = 0; x < ScreenWidth; x++)
            for (var y = 0; y < ScreenHeight; y++)
                Display[x, y] = false;
    }

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

    public void LoadRom(byte[] bytes)
    {
        // load rom, etc..
        // load rom
        Array.Copy(bytes, 0, Ram, K_EntryPointAddress, bytes.Length);
        LoadFont(FontSets.FontDefault);

        // setup
        PC = K_EntryPointAddress;
        I = 0;
        DelayTime = 0;
    }
    public void LoadFont(byte[] fonts)
    {
        // load fonts
        if (fonts.Length >= 0x80)
        {
            Console.WriteLine("Error: font bytes is more than 0x50");
            return;
        }

        const int K_LoadFontStartAddress = 0x50;
        Array.Copy(fonts, 0, Ram, K_LoadFontStartAddress, fonts.Length);
    }

    public Instruction? currentInstruction;
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

        var prevOpcodeString = currentInstruction?.ToString();
        currentInstruction = new Instruction(currentOpcode);
        if (currentInstruction.ToString() == prevOpcodeString)
        {
            //Console.WriteLine("freezing opcode...: " + currentInstruction);
        }

        if (OpcodeExecuteMap.TryGetValue(currentInstruction.firstN, out var exeCallback))
        {
            try
            {
                //Console.WriteLine("execute opcode: " + currentInstruction);
                exeCallback(currentInstruction);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: try execute opcode: " + currentInstruction);
                Console.WriteLine(ex);
            }
        }
        else
        {
            MoveBackOpcode();
            Console.WriteLine("not found opcode: " + currentInstruction);
        }
    }

    public void OnTick60HZ()
    {
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

    public bool LoadRom(string path)
    {
        if (File.Exists(path) is false)
        {
            Console.WriteLine("not found rom at path: " + path);
            return false;
        }

        var bytes = File.ReadAllBytes(path);
        LoadRom(bytes);

        return true;
    }
}
