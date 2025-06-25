using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Chip8;

public sealed class CPU
{
    // Registry
    public ushort I, PC = 0x200;
    // 0 -> 14. 15 it's FlagCarry
    public byte[] V = new byte[16];
    public byte V0 => V[0];
    public byte V1 => V[1];

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
    public readonly HashSet<byte> KeyDownSets = new();
    public bool IsKeyDown(byte keyHex) => KeyDownSets.Contains(keyHex);

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
        RegisterOpcodeImplement(0xD, Draw);
        RegisterOpcodeImplement(0xE, SkipIfKeyDown);
        RegisterOpcodeImplement(0xF, HandleOpcodeF);

        #endregion
    }

    private void HandleOpcodeF(Instruction i)
    {
        switch (i.nn)
        {
            case 0x1E:
                AddToIndex(V[i.x]);
                break;
            case 0x55:
                SaveVX(i);
                break;
            case 0x65:
                LoadVX(i);
                break;
        }
    }

    private void LoadVX(Instruction i)
    {
        for(int vIndex = 0; vIndex < i.x; vIndex++)
            V[vIndex] = Ram[I + vIndex];
    }

    private void SaveVX(Instruction i)
    {
        for(int vIndex = 0; vIndex < i.x; vIndex++)
            Ram[I + vIndex] = V[vIndex];
    }

    void AddToIndex(byte v)
    {
        I += v;
    }

    private void SkipIfKeyDown(Instruction i)
    {
        byte keyHex = V[i.x];
        bool isKeyDown = IsKeyDown(keyHex);
        bool skip = i.nn == 0x9E ? isKeyDown : !isKeyDown;
        if (skip)
            SkipNextOpcode();
    }

    private void Draw(Instruction i)
    {
        var x = V[i.x];
        var y = V[i.y];

        // Todo...
        FlagF = 0;
        Console.WriteLine($"Draw: x: {x}, y: {y}");
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
        bool skip = V[i.x] != V[i.y];
        if (skip)
        {
            SkipNextOpcode();
        }
    }

    void Arithmetic(Instruction i)
    {
        byte x = V[i.x];
        byte y = V[i.y];

        switch (i.n)
        {
            case 0x0: // Sets VX to the value of VY.
                x = V[i.y];
                break;
            case 0x1: // Sets VX to VX or VY.
                x |= y;
                break;
            case 0x2: // Sets VX to VX and VY.
                x &= y;
                break;
            case 0x3: //Sets VX to VX xor VY.
                x ^= y;
                break;
            case 0x4: // Add VX and carry overflow
                SetFlagF(x + y >= 0xFF);
                x += y;
                break;

            // Subtract x-y, y-x
            case 0x5: // Set VX = VX - VY and underflow
                SetFlagF(x > y);
                x -= y;
                break;
            case 0x7: // Y = Y - X
                SetFlagF(y > x);
                y -= x;
                break;

            // Bit shift, set flagF last one bit
            case 0x6: // right
                FlagF = (byte)(x & 1);
                x >>= 1;
                break;
            case 0xE: // left
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
        Console.WriteLine("Return from subroutine");
        var addr = Pop();
        Jump(addr);
    }

    private void ClearScreen()
    {
        Console.WriteLine("Clear Screen");
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

    void Jump(int addr) => Jump((ushort)addr);

    void SkipNextOpcode()
    {
        Jump(PC + 2);
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
        I = PC;
    }

    Instruction currentInstruction;
    public void Tick()
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
            throw new NotImplementedException("not found opcode: " + currentInstruction.opcode);
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
}
