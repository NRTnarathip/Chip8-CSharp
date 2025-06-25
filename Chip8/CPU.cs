using System;
using System.Collections.Generic;
using System.Linq;
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
    public byte V1 => V[1];
    public byte DelayTime;

    public readonly Stack<ushort> Stack = new();
    public byte FlagF
    {
        get => V[0xF];
        set => V[0xF] = value;
    }
    public void SetFlagF(int val) => FlagF = (byte)val;
    public void SetFlagF(bool val) => FlagF = (byte)(val ? 1 : 0);

    // Opcodes
    public readonly Dictionary<byte, Action<Instruction>> OpcodeExecuteMap = new();

    // Ram
    public const int K_MaxRam = 4096;
    public const int K_MaxRamIndex = K_MaxRam - 1;
    public byte[] Ram = new byte[K_MaxRam];

    // helper
    public const int K_EntryPointOffset = 0x200;

    // Random
    public readonly Random rng = new();
    public byte RandomNextByte() => (byte)rng.Next(256);

    // Keyboard key down sets
    readonly object _keyLock = new();
    readonly HashSet<byte> KeyDownSets = new();
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
                    ReturnFromSubroutine();
                    break;
            }
        });

        RegisterOpcodeImplement(0x1, Jump);
        RegisterOpcodeImplement(0x2, CallSubroutine);
        RegisterOpcodeImplement(0x3, SkipNextIf);
        RegisterOpcodeImplement(0x4, SkipNextIf);
        RegisterOpcodeImplement(0x5, SkipNextIf);
        RegisterOpcodeImplement(0x6, SetVX);
        RegisterOpcodeImplement(0x7, AddVX);
        RegisterOpcodeImplement(0x8, Arithmetic);
        RegisterOpcodeImplement(0x9, SkipIfVXNotEqualVY);
        RegisterOpcodeImplement(0xA, SetIToNNN);
        RegisterOpcodeImplement(0xB, JumpWithOffset);
        RegisterOpcodeImplement(0xC, CXNN_Random);
        RegisterOpcodeImplement(0xD, DrawSprite);
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
            case 0x15:
                V[i.x] = DelayTime;
                break;
            //case 0x18:
            //    // sound 
            //    break;
            case 0x1E:
                I += V[i.x];
                break;
            case 0x33: // Binary-coded decimal
                Ram[I + 0] = (byte)((V[i.x] / 100) % 10);
                Ram[I + 1] = (byte)((V[i.x] / 10) % 10);
                Ram[I + 2] = (byte)(V[i.x] % 10);
                break;
            case 0x55:
                SaveVX(i);
                break;
            case 0x65:
                LoadVX(i);
                break;

            default:
                Console.WriteLine("!!Not implement opcode: " + i.opcode);
                break;
        }
    }


    private void LoadVX(Instruction i)
    {
        for (int vIndex = 0; vIndex <= i.x; vIndex++)
            V[vIndex] = Ram[I + vIndex];
    }

    private void SaveVX(Instruction i)
    {
        for (int vIndex = 0; vIndex <= i.x; vIndex++)
            Ram[I + vIndex] = V[vIndex];
    }

    private void SkipIfKeyDown(Instruction i)
    {
        byte keyHex = V[i.x];
        bool isKeyDown = IsKeyDown(keyHex);
        bool skip = i.nn == 0x9E ? isKeyDown : !isKeyDown;
        if (skip)
            SkipNextOpcode();
    }

    public const int ScreenWidth = 64;
    public const int ScreenHeight = 32;
    public const int ScreenPixelSize = ScreenWidth * ScreenHeight;
    public readonly bool[,] Display = new bool[ScreenWidth, ScreenHeight];
    readonly bool[,] DisplayPendingClear = new bool[ScreenWidth, ScreenHeight];
    public bool DisplayNeedRedraw = true;

    private void DrawSprite(Instruction i)
    {
        var startX = V[i.x];
        var startY = V[i.y];

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

        FlagF = 0;
        var spritePixelY = I;
        for (int line = 0; line < i.n; line++)
        {
            var spriteLine = Ram[spritePixelY + line];
            for (var spritePixelX = 0; spritePixelX < 8; spritePixelX++)
            {
                int x = (startX + spritePixelX) % ScreenWidth;
                int y = (startY + I) % ScreenHeight;

                var spriteBit = ((spriteLine >> (7 - spritePixelX)) & 1);
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
                    FlagF = 1;
            }
        }
    }

    private void CXNN_Random(Instruction i)
    {
        var randValue = RandomNextByte();
        V[i.x] = (byte)(randValue & i.nn);
    }

    private void JumpWithOffset(Instruction i)
    {
        PC = (ushort)(V0 + i.nnn);
    }

    private void SetIToNNN(Instruction i)
    {
        I = i.nnn;
    }

    private void SkipIfVXNotEqualVY(Instruction i)
    {
        if (V[i.x] != V[i.y])
            SkipNextOpcode();
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
            case 0x4:
                SetFlagF(x + y >= 0xFF);
                x += y;
                break;
            case 0x5:
                SetFlagF(x >= y);
                x -= y;
                break;
            case 0x7:
                SetFlagF(y >= x);
                y -= x;
                break;

            case 0x6:
                FlagF = (byte)(x & 1);
                x >>= 1;
                break;
            case 0xE:
                FlagF = (byte)(x >> 7);
                x <<= 1;
                break;
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

    private void SkipNextIf(Instruction i)
    {
        byte valueX = V[i.x];

        // skip next opcode,
        switch (i.firstN)
        {
            case 0x3: // skip if VX == NN
                if (valueX == i.nn)
                    SkipNextOpcode();
                break;
            case 0x4: // skip if VX != NN
                if (valueX != i.nn)
                    SkipNextOpcode();
                break;
            case 0x5: // skip if VX == VY
                if (valueX == V[i.y])
                    SkipNextOpcode();
                break;
        }
    }

    void RegisterOpcodeImplement(byte prefixOpcode, Action<Instruction> callback)
    {
        OpcodeExecuteMap.Add(prefixOpcode, callback);
    }

    #region Opcode Implement

    // 0x1
    private void Jump(Instruction i)
    {
        Jump(i.nnn);
    }

    // 0x2
    private void CallSubroutine(Instruction i)
    {
        Push(PC);
        Jump(i.nnn);
    }

    private void ReturnFromSubroutine()
    {
        PC = Pop();
    }

    private void ClearScreen()
    {
        for (var x = 0; x < ScreenWidth; x++)
            for (var y = 0; y < ScreenHeight; y++)
                Display[x, y] = false;
    }
    #endregion

    void Push(ushort address)
    {
        Stack.Push(address);
    }

    ushort Pop()
    {
        return Stack.Pop();
    }

    void Jump(ushort addr)
    {
        PC = addr;
    }

    void SkipNextOpcode()
    {
        PC += 2;
    }

    public void LoadRom(byte[] bytes)
    {
        // load rom, etc..
        // clear 
        Ram = new byte[4096];
        // load rom
        Array.Copy(bytes, 0, Ram, K_EntryPointOffset, bytes.Length);

        // setup
        PC = K_EntryPointOffset;
        I = 0;
        DelayTime = 0;
    }

    public Instruction? currentInstruction;
    public void Cycle()
    {
        var pcData = ReadPCAndIncress();
        // error
        if (pcData == 0)
        {
            Console.WriteLine("error, current opcode is null");
            return;
        }

        currentInstruction = new Instruction(pcData);
        if (OpcodeExecuteMap.TryGetValue(currentInstruction.firstN, out var exeCallback))
        {
            exeCallback(currentInstruction);
        }
        else
        {
            Console.WriteLine("not found opcode: " + currentInstruction.opcode);
        }
    }

    public ushort ReadPCAndIncress()
    {
        // read 2 byte, PC + 1
        if (PC > K_MaxRamIndex - 1)
        {
            Console.WriteLine("PC register overflow");
            return 0;
        }

        byte b1 = Ram[PC++];
        byte b2 = Ram[PC++];

        return (ushort)(b1 << 8 | b2);
    }

    public bool LoadRom(string path)
    {
        if (File.Exists(path) is false)
            return false;

        var bytes = File.ReadAllBytes(path);
        LoadRom(bytes);

        return true;
    }
}
