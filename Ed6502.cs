//Ed6502 - A MOS 6502 processor emulator written in C#
//2025 by Eduardo Mozart R. Oliveira
//Under MIT License
//---- Usage ----
//Instantiate this class and define it's memory (byte[] memory = new byte[65536];)
//Call Step() function to execute next instruction, for continuous excecution call Step() in a loop or coroutine emulating the desired speed (E.g. 2Mhz);
//Verbose debug can be used by uncommenting DebugInstruction(opcode); at Step();

using System;
using System.Collections;
using System.Collections.Generic;

public class Ed6502
{
   public byte[] memory; //Memory

   public byte AC;      // Accumulator
   public byte X;      // Index X
   public byte Y;      // Index Y
   public byte SP;     // Stack Pointer (offset from 0x0100) processor stack is located on memory page #1 ($0100–$01FF)
   public ushort PC;   // Program Counter
   public byte SR;      // status register [Flags]	


    //Status Register flags
    byte FLAG_C = 1 << 0; // Carry
    byte FLAG_Z = 1 << 1; // Zero AC == 0
    byte FLAG_I = 1 << 2; // Interrupt (IRQ disable)
    byte FLAG_D = 1 << 3; // Decimal
    byte FLAG_B = 1 << 4; // Break
    byte FLAG_V = 1 << 6; // Overflow
    byte FLAG_N = 1 << 7; // Negative (AC & 0x80) != 0)

    void SetFlag(byte flag, bool value)
    {
        if (value)
        {
            SR |= flag;
        }
        else
        {
            SR &= (byte)~flag;
        }
    }

   public bool GetFlag(byte flag)
    {
        return (SR & flag) != 0;
    }

    void Push(byte value)
    {
        memory[0x0100 + SP--] = value;
    }

    byte Pull()
    {
        SP++;
        return memory[0x0100 + SP];
    }

    byte ResolveIndirectX(byte operand)
    {
        byte addrL = memory[(byte)(operand + X) & 0xFF];       
        byte addrH = memory[(byte)(operand + X + 1) & 0xFF];
        ushort finalAddress = (ushort)(addrL | (addrH << 8)); 
        byte value = memory[finalAddress];
        return value;
    }
    ushort ResolveIndirectXAddress(byte operand)
    {
        byte addrL = memory[(byte)(operand + X) & 0xFF];       
        byte addrH = memory[(byte)(operand + X + 1) & 0xFF];
        ushort finalAddress = (ushort)(addrL | (addrH << 8)); 
        return finalAddress;
    }

    byte ResolveIndirectY(byte operand)
    {
        byte addrL = memory[operand];      
        byte addrH = memory[(byte)(operand+1)];
        ushort baseAddress = (ushort)(addrL | (addrH << 8)); 
        ushort finalAddress = (ushort)(baseAddress + Y);
        byte value = memory[finalAddress];
        return value;
    }
    ushort ResolveZeropageAddress(byte operand)
    {
        ushort address = operand; // address = 0x00LL
        return address;
    }
    ushort ResolveZeropageXAddress(byte operand)
    {
        ushort address = (ushort)((operand + X) & 0xFF);
        return address;
    }
    ushort ResolveZeropageYAddress(byte operand)
    {
        ushort address = (ushort)((operand + Y) & 0xFF);
        return address;
    }

    byte ResolveZeropage(byte operand)
    {
        ushort address = operand; // address = 0x00LL
        byte value = memory[address];
        return value;
    }
    byte ResolveZeropageX(byte operand)
    {
        ushort address = (ushort)((operand+X) & 0xFF); 
        byte value = memory[address];
        return value;
    }
    byte ResolveZeropageY(byte operand)
    {
        ushort address = (ushort)((operand + Y) & 0xFF);
        byte value = memory[address];
        return value;
    }

    ushort FetchAbsoluteAddress()
    {
        byte lo = memory[PC++];
        byte hi = memory[PC++];
        return (ushort)(lo | (hi << 8));
    }
    byte ResolveAbsolute()
    {
        ushort addr = FetchAbsoluteAddress();
        return memory[addr];
    }

    byte ResolveAbsoluteX()
    {
        ushort addr = FetchAbsoluteAddress();
        return memory[(ushort)(addr + X)];
    }

    byte ResolveAbsoluteY()
    {
        ushort addr = FetchAbsoluteAddress();
        return memory[(ushort)(addr + Y)];
    }

    ushort ResolveIndirectYAddress(byte operand)
    {
        byte addrL = memory[operand];       
        byte addrH = memory[(byte)(operand + 1)];
        ushort baseAddress = (ushort)(addrL | (addrH << 8)); 
        ushort finalAddress = (ushort)(baseAddress + Y);
        return finalAddress;
    }

    void ADC_BCD(byte value)
    {
        int carry = GetFlag(FLAG_C) ? 1 : 0;
        int lo = (AC & 0x0F) + (value & 0x0F) + carry;
        int hi = (AC >> 4) + (value >> 4);

        if (lo > 9)
        {
            lo -= 10;
            hi++;
        }
        if (hi > 9)
        {
            hi -= 10;
            SetFlag(FLAG_C, true);
        }
        else
        {
            SetFlag(FLAG_C, false);
        }

        AC = (byte)((hi << 4) | (lo & 0x0F));
        SetFlag(FLAG_Z, AC == 0);
        SetFlag(FLAG_N, (AC & 0x80) != 0);
    }
    void SBC_BCD(byte value)
    {
        int carry = GetFlag(FLAG_C) ? 0 : 1;
        int lo = (AC & 0x0F) - (value & 0x0F) - carry;
        int hi = (AC >> 4) - (value >> 4);

        if (lo < 0)
        {
            lo += 10;
            hi--;
        }
        if (hi < 0)
        {
            hi += 10;
            SetFlag(FLAG_C, false);
        }
        else
        {
            SetFlag(FLAG_C, true);
        }

        AC = (byte)((hi << 4) | (lo & 0x0F));
        SetFlag(FLAG_Z, AC == 0);
        SetFlag(FLAG_N, (AC & 0x80) != 0);
    }

    void InterpretOpcode(byte opcode)
    {
        byte operand = 0x00;
        sbyte sOperand = 0x00;
        byte value = 0x00;
        int intvalue = 0;
        int carry =0;
        int sum = 0;
        int result = 0;
        ushort returnAddress = 0;
        ushort absAddr = 0;
        byte pcLow = 0x00;
        byte pcHigh = 0x00;
        bool oldCarry = false;
        byte flags = 0x00;
        switch (opcode)
        {
            //High byte 0 ----------------------------------------------------------------------------------------------------
            case 0x00: //BRK impl
                PC++;
                Push((byte)(PC >> 8));       // Push high byte of PC
                Push((byte)(PC & 0xFF));     // Push low byte of PC

                SetFlag(FLAG_B, true);       // Set Break flag before pushing SR
                Push((byte)SR);              // Push SR
                SetFlag(FLAG_B, false);      // Clear Break flag

                SetFlag(FLAG_I, true);       // Set Interrupt Disable
                PC = (ushort)(memory[0xFFFE] | (memory[0xFFFF] << 8)); // Load IRQ vector
                break;

            case 0x10: //BPL rel
                sOperand = (sbyte)memory[PC++];
                if (!GetFlag(FLAG_N))
                {
                    PC = (ushort)(PC + sOperand);
                }
                break;
            case 0x20: //JSR abs   
                ushort addr = FetchAbsoluteAddress();
                returnAddress = (ushort)(PC - 1); // Address of last byte of JSR (the high byte of the address)
                Push((byte)(returnAddress >> 8));
                Push((byte)(returnAddress & 0xFF));
                PC = addr;
                break;
            case 0x30: //BMI rel
                sOperand = (sbyte)memory[PC++];
                if (GetFlag(FLAG_N))
                {
                    PC = (ushort)(PC + sOperand);
                }
                break;
            case 0x40: //RTI impl
                operand = memory[PC++];
                SR = memory[0x0100 + ++SP]; //pull SR
                pcLow = memory[0x0100 + ++SP];
                pcHigh = memory[0x0100 + ++SP];
                PC = (ushort)((pcHigh << 8) | pcLow);
                break;
            case 0x50: //BVC rel
                sOperand = (sbyte)memory[PC++];
                if (!GetFlag(FLAG_V))
                {
                    PC = (ushort)(PC + sOperand);
                }
                break;
            case 0x60: //RTS impl
                operand = memory[PC++];
                pcLow = memory[0x0100 + ++SP];
                pcHigh = memory[0x0100 + ++SP];
                PC = (ushort)(((pcHigh << 8) | pcLow) + 1);
                break;
            case 0x70: //BVS rel
                sOperand = (sbyte)memory[PC++];
                if (GetFlag(FLAG_V))
                {
                    PC = (ushort)(PC + sOperand);
                }
                break;
            case 0x90: //BCC rel
                sOperand = (sbyte)memory[PC++];
                if (!GetFlag(FLAG_C))
                {
                    PC = (ushort)(PC + sOperand);
                }
                break;
            case 0xA0: //LDY #
                operand = memory[PC++];
                Y = operand;
                SetFlag(FLAG_Z, Y == 0);
                SetFlag(FLAG_N, (Y & 0x80) != 0);
                break;
            case 0xB0: //BCS rel
                sOperand = (sbyte)memory[PC++];
                if (GetFlag(FLAG_C))
                {
                    PC = (ushort)(PC + sOperand);
                }
                break;
            case 0xC0: //CPY #
                operand = memory[PC++];
                result = (byte)(Y - operand);
                SetFlag(FLAG_C, Y >= operand);                     // No borrow = Carry set
                SetFlag(FLAG_Z, (byte)result == 0);               // Equal = Zero set
                SetFlag(FLAG_N, (result & 0x80) != 0);            // Bit 7 = Negative
                break;
            case 0xD0: //BNE rel
                sOperand = (sbyte)memory[PC++];
                if (!GetFlag(FLAG_Z))
                {
                    PC = (ushort)(PC + sOperand);
                }
                break;
            case 0xE0: //CPX #
                operand = memory[PC++];
                result = X - operand;
                SetFlag(FLAG_C, X >= operand);                     // No borrow = Carry set
                SetFlag(FLAG_Z, (byte)result == 0);               // Equal = Zero set
                SetFlag(FLAG_N, (result & 0x80) != 0);            // Bit 7 = Negative
                break;
            case 0xF0: //BEQ rel
              
                sOperand = (sbyte)memory[PC++];
                if (GetFlag(FLAG_Z))
                {
                    PC = (ushort)(PC + sOperand);
                }
                break;


            //High byte 1 ----------------------------------------------------------------------------------------------------
            case 0x01: //ORA X,ind
                operand = memory[PC++];
                AC |= ResolveIndirectX(operand); // Or AC with Memory
                SetFlag(FLAG_Z, AC == 0);
                SetFlag(FLAG_N, (AC & 0x80) != 0);
                break;
            case 0x11: //ORA ind,Y
                operand = memory[PC++];
                AC |= ResolveIndirectY(operand); // Or AC with Memory
                SetFlag(FLAG_Z, AC == 0);
                SetFlag(FLAG_N, (AC & 0x80) != 0);
                break;
            case 0x21: //AND X,ind
                operand = memory[PC++];
                AC &= ResolveIndirectX(operand); // AND AC with Memory
                SetFlag(FLAG_Z, AC == 0);
                SetFlag(FLAG_N, (AC & 0x80) != 0);
                break;
            case 0x31: //AND ind,Y
                operand = memory[PC++];
                AC &= ResolveIndirectY(operand); // AND AC with Memory
                SetFlag(FLAG_Z, AC == 0);
                SetFlag(FLAG_N, (AC & 0x80) != 0);
                break;
            case 0x41: //EOR X,ind
                operand = memory[PC++];
                AC ^= ResolveIndirectX(operand); // XOR AC with Memory
                SetFlag(FLAG_Z, AC == 0);
                SetFlag(FLAG_N, (AC & 0x80) != 0);
                break;
            case 0x51: //EOR ind,y
                operand = memory[PC++];
                AC ^= ResolveIndirectY(operand); // XOR AC with Memory
                SetFlag(FLAG_Z, AC == 0);
                SetFlag(FLAG_N, (AC & 0x80) != 0);
                break;
            case 0x61: //ADC X, ind
                operand = memory[PC++];
                value = ResolveIndirectX(operand);
                if (GetFlag(FLAG_D))
                {
                    ADC_BCD(value);
                }
                else
                {
                    carry = GetFlag(FLAG_C) ? 1 : 0;
                    sum = AC + value + carry;
                    SetFlag(FLAG_C, sum > 0xFF);
                    SetFlag(FLAG_Z, (byte)sum == 0);
                    SetFlag(FLAG_N, (sum & 0x80) != 0);
                    SetFlag(FLAG_V, ((AC ^ value) & 0x80) == 0 && ((AC ^ sum) & 0x80) != 0);
                    AC = (byte)(sum & 0xff);
                }
                break;
            case 0x71://ADC ind, Y
                operand = memory[PC++];
                value = ResolveIndirectY(operand);
                if (GetFlag(FLAG_D))
                {
                    ADC_BCD(value);
                }
                else
                {
                    carry = GetFlag(FLAG_C) ? 1 : 0;
                    sum = AC + value + carry;

                    SetFlag(FLAG_C, sum > 0xFF);
                    SetFlag(FLAG_Z, (byte)sum == 0);
                    SetFlag(FLAG_N, (sum & 0x80) != 0);
                    SetFlag(FLAG_V, ((AC ^ value) & 0x80) == 0 && ((AC ^ sum) & 0x80) != 0);
                    AC = (byte)(sum & 0xff);

                }
                   
                break;
            case 0x81://STA X,ind
                operand = memory[PC++];
                memory[ResolveIndirectXAddress(operand)] = AC;
                break;
            case 0x91://STA ind,Y
                operand = memory[PC++];
                memory[ResolveIndirectYAddress(operand)] = AC;
                break;
            case 0xA1://LDA X,ind
                operand = memory[PC++];
                AC = memory[ResolveIndirectXAddress(operand)];
                SetFlag(FLAG_Z, AC == 0);
                SetFlag(FLAG_N, (AC & 0x80) != 0);
                break;
            case 0xB1://LDA ind,Y
                operand = memory[PC++];
                AC = memory[ResolveIndirectYAddress(operand)];
                SetFlag(FLAG_Z, AC == 0);
                SetFlag(FLAG_N, (AC & 0x80) != 0);
                break;
            case 0xC1://CMP X,ind
                operand = memory[PC++];
                 value = ResolveIndirectX(operand);
                 result = AC - value;
                SetFlag(FLAG_C, AC >= value);                     // No borrow = Carry set
                SetFlag(FLAG_Z, (byte)result == 0);               // Equal = Zero set
                SetFlag(FLAG_N, (result & 0x80) != 0);            // Bit 7 = Negative
                break;
            case 0xD1://CMP ind,Y
                operand = memory[PC++];
                 value = ResolveIndirectY(operand);
                 result = AC - value;
                SetFlag(FLAG_C, AC >= value);                     // No borrow = Carry set
                SetFlag(FLAG_Z, (byte)result == 0);               // Equal = Zero set
                SetFlag(FLAG_N, (result & 0x80) != 0);            // Bit 7 = Negative
                break;
            case 0xE1: //SBC x,ind
                operand = memory[PC++];
                if (GetFlag(FLAG_D))
                {
                    value = ResolveIndirectX(operand);
                    SBC_BCD(value);
                }
                else
                {
                    intvalue = ResolveIndirectX(operand);
                    carry = GetFlag(FLAG_C) ? 1 : 0; 
                    sum = AC - intvalue - (1 - carry);

                    SetFlag(FLAG_C, sum >= 0); 
                    SetFlag(FLAG_Z, (sum & 0xFF) == 0);
                    SetFlag(FLAG_N, (sum & 0x80) != 0);
                    SetFlag(FLAG_V, ((AC ^ sum) & (AC ^ intvalue) & 0x80) != 0);

                    AC = (byte)(sum & 0xFF);
                }
                break;
            case 0xF1: //SBC ind,y
                operand = memory[PC++];
                if (GetFlag(FLAG_D))
                {
                    value = ResolveIndirectY(operand);
                    SBC_BCD(value);
                }
                else
                {
                    intvalue = ResolveIndirectY(operand);
                    carry = GetFlag(FLAG_C) ? 1 : 0; 
                    sum = AC - intvalue - (1 - carry);

                    SetFlag(FLAG_C, sum >= 0); 
                    SetFlag(FLAG_Z, (sum & 0xFF) == 0);
                    SetFlag(FLAG_N, (sum & 0x80) != 0);
                    SetFlag(FLAG_V, ((AC ^ sum) & (AC ^ intvalue) & 0x80) != 0);

                    AC = (byte)(sum & 0xFF);
                }
                break;

            //High byte 2 ----------------------------------------------------------------------------------------------------
            case 0xA2: //LDX #
                operand = memory[PC++];
                X = operand;
                SetFlag(FLAG_Z, X == 0);
                SetFlag(FLAG_N, (X & 0x80) != 0);
                break;

            //High byte 4 ----------------------------------------------------------------------------------------------------
            case 0x24: //BIT zpg
                operand = memory[PC++];
                value = ResolveZeropage(operand);
                SetFlag(FLAG_N, (value & 0x80) != 0);
                SetFlag(FLAG_V, (value & 0x40) != 0);
                SetFlag(FLAG_Z, (AC & value) == 0);
                break;
            case 0x84: //STY zpg
                operand = memory[PC++];
                memory[ResolveZeropageAddress(operand)] = Y;
                break;
            case 0x94: //STY zpg,x
                operand = memory[PC++];
                memory[ResolveZeropageXAddress(operand)] = Y;
                break;
            case 0xA4: //LDY zpg
                operand = memory[PC++];
                Y = memory[ResolveZeropageAddress(operand)];
                SetFlag(FLAG_Z, Y == 0);
                SetFlag(FLAG_N, (Y & 0x80) != 0);
                break;
            case 0xB4: //LDY zpg,x
                operand = memory[PC++];
                Y = memory[ResolveZeropageXAddress(operand)];
                SetFlag(FLAG_Z, Y == 0);
                SetFlag(FLAG_N, (Y & 0x80) != 0);
                break;
            case 0xC4: //CPY zpg
                operand = memory[PC++];
                value = ResolveZeropage(operand);
                result = Y - value;
                SetFlag(FLAG_C, Y >= value);                     // No borrow = Carry set
                SetFlag(FLAG_Z, (byte)result == 0);               // Equal = Zero set
                SetFlag(FLAG_N, (result & 0x80) != 0);            // Bit 7 = Negative
                break;
            case 0xE4: //CPX zpg
                operand = memory[PC++];
                value = ResolveZeropage(operand);
                result = X - value;
                SetFlag(FLAG_C, X >= value);                     // No borrow = Carry set
                SetFlag(FLAG_Z, (byte)result == 0);               // Equal = Zero set
                SetFlag(FLAG_N, (result & 0x80) != 0);            // Bit 7 = Negative
                break;


            //High byte 5 ----------------------------------------------------------------------------------------------------
            case 0x05: //ORA zpg
                operand = memory[PC++];
                AC |= ResolveZeropage(operand); // Or AC with Memory
                SetFlag(FLAG_Z, AC == 0);
                SetFlag(FLAG_N, (AC & 0x80) != 0);
                break;
            case 0x15: //ORA zpg,x
                operand = memory[PC++];
                AC |= ResolveZeropageX(operand); // Or AC with Memory
                SetFlag(FLAG_Z, AC == 0);
                SetFlag(FLAG_N, (AC & 0x80) != 0);
                break;
            case 0x25: //AND zpg
                operand = memory[PC++];
                AC &= ResolveZeropage(operand); // AND AC with Memory
                SetFlag(FLAG_Z, AC == 0);
                SetFlag(FLAG_N, (AC & 0x80) != 0);
                break;
            case 0x35: //AND zpg,x
                operand = memory[PC++];
                AC &= ResolveZeropageX(operand); // AND AC with Memory
                SetFlag(FLAG_Z, AC == 0);
                SetFlag(FLAG_N, (AC & 0x80) != 0);
                break;
            case 0x45: //EOR zpg
                operand = memory[PC++];
                AC ^= ResolveZeropage(operand); // XOR AC with Memory
                SetFlag(FLAG_Z, AC == 0);
                SetFlag(FLAG_N, (AC & 0x80) != 0);
                break;
            case 0x55: //EOR zpg,x
                operand = memory[PC++];
                AC ^= ResolveZeropageX(operand); // XOR AC with Memory
                SetFlag(FLAG_Z, AC == 0);
                SetFlag(FLAG_N, (AC & 0x80) != 0);
                break;
            case 0x65: //ADC zpg
                operand = memory[PC++];
                value = ResolveZeropage(operand);
                if (GetFlag(FLAG_D))
                {
                    ADC_BCD(value);
                }
                else
                {
                    carry = GetFlag(FLAG_C) ? 1 : 0;
                    sum = AC + value + carry;

                    SetFlag(FLAG_C, sum > 0xFF);
                    SetFlag(FLAG_Z, (byte)sum == 0);
                    SetFlag(FLAG_N, (sum & 0x80) != 0);
                    SetFlag(FLAG_V, ((AC ^ value) & 0x80) == 0 && ((AC ^ sum) & 0x80) != 0);
                    AC = (byte)(sum & 0xff);
                }
                break;
            case 0x75: //ADC zpg,x
                operand = memory[PC++];
                value = ResolveZeropageX(operand);
                if (GetFlag(FLAG_D))
                {
                    ADC_BCD(value);
                }
                else
                {
                    carry = GetFlag(FLAG_C) ? 1 : 0;
                    sum = AC + value + carry;

                    SetFlag(FLAG_C, sum > 0xFF);
                    SetFlag(FLAG_Z, (byte)sum == 0);
                    SetFlag(FLAG_N, (sum & 0x80) != 0);
                    SetFlag(FLAG_V, ((AC ^ value) & 0x80) == 0 && ((AC ^ sum) & 0x80) != 0);
                    AC = (byte)(sum & 0xff);
                }
                break;
            case 0x85://STA zpg
                operand = memory[PC++];
                memory[ResolveZeropageAddress(operand)] = AC;
                break;
            case 0x95://STA zpg,x
                operand = memory[PC++];
                memory[ResolveZeropageXAddress(operand)] = AC;
                break;
            case 0xA5://LDA zpg
                operand = memory[PC++];
                AC = memory[ResolveZeropageAddress(operand)];
                SetFlag(FLAG_Z, AC == 0);
                SetFlag(FLAG_N, (AC & 0x80) != 0);
                break;
            case 0xB5://LDA zpg,x
                operand = memory[PC++];
                AC = memory[ResolveZeropageXAddress(operand)];
                SetFlag(FLAG_Z, AC == 0);
                SetFlag(FLAG_N, (AC & 0x80) != 0);
                break;
            case 0xC5://CMP zpg
                operand = memory[PC++];
                value = ResolveZeropage(operand);
                result = AC - value;
                SetFlag(FLAG_C, AC >= value);                     // No borrow = Carry set
                SetFlag(FLAG_Z, (byte)result == 0);               // Equal = Zero set
                SetFlag(FLAG_N, (result & 0x80) != 0);            // Bit 7 = Negative
                break;
            case 0xD5://CMP zpg
                operand = memory[PC++];
                value = ResolveZeropageX(operand);
                result = AC - value;
                SetFlag(FLAG_C, AC >= value);                     // No borrow = Carry set
                SetFlag(FLAG_Z, (byte)result == 0);               // Equal = Zero set
                SetFlag(FLAG_N, (result & 0x80) != 0);            // Bit 7 = Negative
                break;
            case 0xE5: //SBC zpg
                operand = memory[PC++];
                if (GetFlag(FLAG_D))
                {
                    value = ResolveZeropage(operand);
                    SBC_BCD(value);
                }
                else
                {
                    intvalue = ResolveZeropage(operand);
                    carry = GetFlag(FLAG_C) ? 1 : 0; 
                    sum = AC - intvalue - (1 - carry);

                    SetFlag(FLAG_C, sum >= 0); 
                    SetFlag(FLAG_Z, (sum & 0xFF) == 0);
                    SetFlag(FLAG_N, (sum & 0x80) != 0);
                    SetFlag(FLAG_V, ((AC ^ sum) & (AC ^ intvalue) & 0x80) != 0);

                    AC = (byte)(sum & 0xFF);
                }
                break;
            case 0xF5: //SBC zpg,x
                operand = memory[PC++];
                if (GetFlag(FLAG_D))
                {
                    value = ResolveZeropageX(operand);
                    SBC_BCD(value);
                }
                else
                {
                    intvalue = ResolveZeropageX(operand);
                    carry = GetFlag(FLAG_C) ? 1 : 0; 
                    sum = AC - intvalue - (1 - carry);

                    SetFlag(FLAG_C, sum >= 0); 
                    SetFlag(FLAG_Z, (sum & 0xFF) == 0);
                    SetFlag(FLAG_N, (sum & 0x80) != 0);
                    SetFlag(FLAG_V, ((AC ^ sum) & (AC ^ intvalue) & 0x80) != 0);

                    AC = (byte)(sum & 0xFF);
                }
                break;

            //High byte 6 ----------------------------------------------------------------------------------------------------
            case 0x06: // ASL zpg
                operand = memory[PC++];
                value = memory[operand];
                SetFlag(FLAG_C, (value & 0x80) != 0);
                value <<= 1;
                memory[operand] = value;
                SetFlag(FLAG_Z, value == 0);
                SetFlag(FLAG_N, (value & 0x80) != 0);
                break;
            case 0x16: // ASL zpg,x
                operand = memory[PC++];
                value = ResolveZeropageX(operand); 
                SetFlag(FLAG_C, (value & 0x80) != 0);
                value <<= 1;
                memory[ResolveZeropageXAddress(operand)] = value;
                SetFlag(FLAG_Z, value == 0);
                SetFlag(FLAG_N, (value & 0x80) != 0);
                break;
            case 0x26: // ROL zpg
               operand = memory[PC++];
               value = memory[operand];
               oldCarry = GetFlag(FLAG_C);
               SetFlag(FLAG_C, (value & 0x80) != 0);
               value = (byte)((value << 1) | (oldCarry ? 1 : 0));
                memory[operand] = value;
                SetFlag(FLAG_Z, value == 0);
               SetFlag(FLAG_N, (value & 0x80) != 0);
               break;
            case 0x36: // ROL zpg,x
                operand = memory[PC++];
                value = ResolveZeropageX(operand);
                oldCarry = GetFlag(FLAG_C);
                SetFlag(FLAG_C, (value & 0x80) != 0);
                value = (byte)((value << 1) | (oldCarry ? 1 : 0));
                memory[ResolveZeropageXAddress(operand)] = value;
                SetFlag(FLAG_Z, value == 0);
                SetFlag(FLAG_N, (value & 0x80) != 0);
                break;
            case 0x46: // LSR zpg
                operand = memory[PC++];
                value = memory[operand];
                SetFlag(FLAG_C, (value & 0x01) != 0);
                value >>= 1;
                memory[operand] = value;
                SetFlag(FLAG_Z, value == 0);
                SetFlag(FLAG_N, false);
                break;
            case 0x56: //LSR zpg,x
                operand = memory[PC++];
                value = ResolveZeropageX(operand);
                SetFlag(FLAG_C, (value & 0x01) != 0);
                value >>= 1;
                memory[ResolveZeropageXAddress(operand)] = value;
                SetFlag(FLAG_Z, value == 0);
                SetFlag(FLAG_N, false);
                break;
            case 0x66: // ROR zpg
              operand = memory[PC++];
              value = memory[operand];
              oldCarry = GetFlag(FLAG_C);
              SetFlag(FLAG_C, (value & 0x01) != 0);
              value = (byte)((value >> 1) | (oldCarry ? 0x80 : 0));
                memory[operand] = value;
                SetFlag(FLAG_Z, value == 0);
              SetFlag(FLAG_N, (value & 0x80) != 0);
              break;
            case 0x76: // ROR zpg,x
              operand = memory[PC++];
              value = ResolveZeropageX(operand);
              oldCarry = GetFlag(FLAG_C);
              SetFlag(FLAG_C, (value & 0x01) != 0);
              value = (byte)((value >> 1) | (oldCarry ? 0x80 : 0));
                memory[ResolveZeropageXAddress(operand)] = value;
                SetFlag(FLAG_Z, value == 0);
              SetFlag(FLAG_N, (value & 0x80) != 0);
                break;
            case 0x86: //STX zpg
                operand = memory[PC++];
                memory[operand] = X;
                break;
            case 0x96: //STX zpg,y
                operand = memory[PC++];
                memory[ResolveZeropageYAddress(operand)] = X;
                break;
            case 0xA6: //LDX zpg
                operand = memory[PC++];
                value = memory[operand];
                X = value;
                SetFlag(FLAG_Z, value == 0);
                SetFlag(FLAG_N, (value & 0x80) != 0);
                break;
            case 0xB6: //LDX zpg,y
                operand = memory[PC++];
                value = ResolveZeropageY(operand);
                X = value;
                SetFlag(FLAG_Z, value == 0);
                SetFlag(FLAG_N, (value & 0x80) != 0);
                break;
            case 0xC6: // DEC zpg
                operand = memory[PC++];
                value = memory[operand];
                value--;
                memory[operand] = value;
                SetFlag(FLAG_Z, value == 0);
                SetFlag(FLAG_N, (value & 0x80) != 0);
                break;
            case 0xD6: // DEC zpg,x
                operand = memory[PC++];
                value = ResolveZeropageX(operand);
                value--;
                memory[ResolveZeropageXAddress(operand)] = value;
                SetFlag(FLAG_Z, value == 0);
                SetFlag(FLAG_N, (value & 0x80) != 0);
                break;
            case 0xE6: // INC zpg
                operand = memory[PC++];
                value = memory[operand];
                value++;
                memory[operand] = value;
                SetFlag(FLAG_Z, value == 0);
                SetFlag(FLAG_N, (value & 0x80) != 0);
                break;
            case 0xF6: // INC zpg,x
                operand = memory[PC++];
                value = ResolveZeropageX(operand);
                value++;
                memory[ResolveZeropageXAddress(operand)] = value;
                SetFlag(FLAG_Z, value == 0);
                SetFlag(FLAG_N, (value & 0x80) != 0);
                break;

            //High byte 8 ----------------------------------------------------------------------------------------------------
            case 0x08: //PHP impl
                flags = (byte)(SR | 0x30); //Set bits 4 (Break)  and 5
                Push(flags);
                break;
            case 0x18: //CLC impl
                SetFlag(FLAG_C, false);
                break;
            case 0x28: //PLP impl
                flags = Pull();
                SR = (byte)((flags & 0xCF) | 0x20); // Clear bits 4 & 5, then set bit 5
                break;
            case 0x38: //SEC impl
                SetFlag(FLAG_C, true);
                break;
            case 0x48: //PHA impl
                Push(AC);
                break;
            case 0x58: // CLI impl
                SetFlag(FLAG_I, false);
                break;
            case 0x68: //PLA impl
                AC = Pull();
                SetFlag(FLAG_Z, AC == 0);
                SetFlag(FLAG_N, (AC & 0x80) != 0);
                break;
            case 0x78: // SEI impl
                SetFlag(FLAG_I, true);
                break;
            case 0x88: //DEY impl
                Y -= 1;
                SetFlag(FLAG_Z, Y == 0);
                SetFlag(FLAG_N, (Y & 0x80) != 0);
                break;
            case 0x98: //TYA impl
                AC = Y;
                SetFlag(FLAG_Z, AC == 0);
                SetFlag(FLAG_N, (AC & 0x80) != 0);
                break;
            case 0xA8: //TAY impl
                Y = AC;
                SetFlag(FLAG_Z, Y == 0);
                SetFlag(FLAG_N, (Y & 0x80) != 0);
                break;
            case 0xB8: // CLV impl
                SetFlag(FLAG_V, false);
                break;
            case 0xC8: // INY impl
                Y += 1;
                SetFlag(FLAG_Z, Y == 0);
                SetFlag(FLAG_N, (Y & 0x80) != 0);
                break;
            case 0xD8: // CLD impl
                SetFlag(FLAG_D, false);
                break;
            case 0xE8: // INX impl
                X += 1;
                SetFlag(FLAG_Z, X == 0);
                SetFlag(FLAG_N, (X & 0x80) != 0);
                break;
            case 0xF8: //SED impl
                SetFlag(FLAG_D, true);
                break;
            //High byte 9
            case 0x09: //ORA #
                operand = memory[PC++];
                AC |= operand; // Or AC with Memory
                SetFlag(FLAG_Z, AC == 0);
                SetFlag(FLAG_N, (AC & 0x80) != 0);
                break;
            case 0x19: //ORA abs,y
                value = ResolveAbsoluteY();
                AC |= value; // Or AC with Memory
                SetFlag(FLAG_Z, AC == 0);
                SetFlag(FLAG_N, (AC & 0x80) != 0);
                break;
            case 0x29: //AND #
                operand = memory[PC++];
                AC &= operand; // AND AC with Memory
                SetFlag(FLAG_Z, AC == 0);
                SetFlag(FLAG_N, (AC & 0x80) != 0);
                break;
            case 0x39: //AND abs,y
                value = ResolveAbsoluteY();
                AC &= value; // AND AC with Memory
                SetFlag(FLAG_Z, AC == 0);
                SetFlag(FLAG_N, (AC & 0x80) != 0);
                break;
            case 0x49: //EOR #
                operand = memory[PC++];
                AC ^= operand; // XOR AC with Memory
                SetFlag(FLAG_Z, AC == 0);
                SetFlag(FLAG_N, (AC & 0x80) != 0);
                break;
            case 0x59: //EOR abs,y
                value = ResolveAbsoluteY();
                AC ^= value; // XOR AC with Memory
                SetFlag(FLAG_Z, AC == 0);
                SetFlag(FLAG_N, (AC & 0x80) != 0);
                break;
            case 0x69: //ADC #
                operand = memory[PC++];
                if (GetFlag(FLAG_D))
                {
                    ADC_BCD(operand);
                }
                else
                {
                    carry = GetFlag(FLAG_C) ? 1 : 0;
                    sum = AC + operand + carry;

                    SetFlag(FLAG_C, sum > 0xFF);
                    SetFlag(FLAG_Z, (byte)sum == 0);
                    SetFlag(FLAG_N, (sum & 0x80) != 0);
                    SetFlag(FLAG_V, ((AC ^ operand) & 0x80) == 0 && ((AC ^ sum) & 0x80) != 0);
                    AC = (byte)sum;
                }
                break;
            case 0x79: //ADC abs,y
                value = ResolveAbsoluteY();
                if (GetFlag(FLAG_D))
                {
                    ADC_BCD(value);
                }
                else
                {
                    carry = GetFlag(FLAG_C) ? 1 : 0;
                    sum = AC + value + carry;
                    SetFlag(FLAG_C, sum > 0xFF);
                    SetFlag(FLAG_Z, (byte)sum == 0);
                    SetFlag(FLAG_N, (sum & 0x80) != 0);
                    SetFlag(FLAG_V, ((AC ^ value) & 0x80) == 0 && ((AC ^ sum) & 0x80) != 0);
                    AC = (byte)(sum & 0xff);
                }
                break;
            case 0x99://STA abs,y
                absAddr = FetchAbsoluteAddress();
                memory[(ushort)(absAddr + Y)] = AC;
                break;
            case 0xA9://LDA #
                //operand = memory[PC++];
                //AC = memory[operand];
                AC = memory[PC++];
                SetFlag(FLAG_Z, AC == 0);
                SetFlag(FLAG_N, (AC & 0x80) != 0);
                break;
            case 0xB9://LDA abs,y
                AC = ResolveAbsoluteY();
                SetFlag(FLAG_Z, AC == 0);
                SetFlag(FLAG_N, (AC & 0x80) != 0);
                break;
            case 0xC9://CMP #
                operand = memory[PC++];
                result = AC - operand;
                SetFlag(FLAG_C, AC >= operand);                     // No borrow = Carry set
                SetFlag(FLAG_Z, (byte)result == 0);               // Equal = Zero set
                SetFlag(FLAG_N, (result & 0x80) != 0);            // Bit 7 = Negative
                break;
            case 0xD9://CMP abs,y
                value = ResolveAbsoluteY();
                result = (byte)(AC - value);
                SetFlag(FLAG_C, AC >= value);                     // No borrow = Carry set
                SetFlag(FLAG_Z, (byte)result == 0);               // Equal = Zero set
                SetFlag(FLAG_N, (result & 0x80) != 0);            // Bit 7 = Negative
                break;
            case 0xE9: //SBC #
                operand = memory[PC++];
                if (GetFlag(FLAG_D))
                {
                    value = operand;
                    SBC_BCD(value);
                }
                else
                {
                    intvalue = operand;
                    carry = GetFlag(FLAG_C) ? 1 : 0; 
                    sum = AC - intvalue - (1 - carry);

                    SetFlag(FLAG_C, sum >= 0); 
                    SetFlag(FLAG_Z, (sum & 0xFF) == 0);
                    SetFlag(FLAG_N, (sum & 0x80) != 0);
                    SetFlag(FLAG_V, ((AC ^ sum) & (AC ^ intvalue) & 0x80) != 0);

                    AC = (byte)(sum & 0xFF);
                }
                break;
            case 0xF9: //SBC abs,Y
                if (GetFlag(FLAG_D))
                {
                    value = ResolveAbsoluteY();
                    SBC_BCD(value);
                }
                else
                {
                    intvalue = ResolveAbsoluteY();
                    carry = GetFlag(FLAG_C) ? 1 : 0; 
                    sum = AC - intvalue - (1 - carry);

                    SetFlag(FLAG_C, sum >= 0); 
                    SetFlag(FLAG_Z, (sum & 0xFF) == 0);
                    SetFlag(FLAG_N, (sum & 0x80) != 0);
                    SetFlag(FLAG_V, ((AC ^ sum) & (AC ^ intvalue) & 0x80) != 0);

                    AC = (byte)(sum & 0xFF);
                }
                break;
            //High byte A ----------------------------------------------------------------------------------------------------

            case 0x0A: // ASL A
                SetFlag(FLAG_C, (AC & 0x80) != 0);  // bit 7 → Carry
                AC <<= 1;
                AC &= 0xFF; // ensure byte
                SetFlag(FLAG_Z, AC == 0);
                SetFlag(FLAG_N, (AC & 0x80) != 0);
                break;
            case 0x2A: // ROL A
                oldCarry = GetFlag(FLAG_C);
                SetFlag(FLAG_C, (AC & 0x80) != 0);
                AC = (byte)((AC << 1) | (oldCarry ? 1 : 0));
                SetFlag(FLAG_Z, AC == 0);
                SetFlag(FLAG_N, (AC & 0x80) != 0);
                break;
            case 0x4A: // LSR A
                SetFlag(FLAG_C, (AC & 0x01) != 0);
                AC >>= 1;
                SetFlag(FLAG_Z, AC == 0);
                SetFlag(FLAG_N, false);
                break;
            case 0x6A: // ROR A
                oldCarry = GetFlag(FLAG_C);
                SetFlag(FLAG_C, (AC & 0x01) != 0);
                AC = (byte)((AC >> 1) | (oldCarry ? 0x80 : 0));
                SetFlag(FLAG_Z, AC == 0);
                SetFlag(FLAG_N, (AC & 0x80) != 0);
                break;
            case 0x8A: //TXA impl
                AC = X;
                SetFlag(FLAG_Z, AC == 0);
                SetFlag(FLAG_N, (AC & 0x80) != 0);
                break;
            case 0x9A: //TXS impl
                SP = X;
                break;
            case 0xAA: //TAX impl
                X = AC;
                SetFlag(FLAG_Z, X == 0);
                SetFlag(FLAG_N, (X & 0x80) != 0);
                break;
            case 0xBA: //TSX impl
                X = SP;
                SetFlag(FLAG_Z, X == 0);
                SetFlag(FLAG_N, (X & 0x80) != 0);
                break;
            case 0xCA: //DEX impl
                X = (byte)(X-1);
                SetFlag(FLAG_Z, X == 0);
                SetFlag(FLAG_N, (X & 0x80) != 0);
                break;
            case 0xEA: // NOP
                       //No operation, Do nothing
             //PC++;
            break;
            //High byte C ----------------------------------------------------------------------------------------------------

            case 0x2C: //BIT Abs
                value = ResolveAbsolute();
                SetFlag(FLAG_N, (value & 0x80) != 0);
                SetFlag(FLAG_V, (value & 0x40) != 0);
                SetFlag(FLAG_Z, (AC & value) == 0);
                break;
            case 0x4C: // JMP abs
                byte lo = memory[PC++];
                byte hi = memory[PC++];
                PC = (ushort)(lo | (hi << 8));
                break;
            case 0x6C: //JMP ind
                byte ptrLo = memory[PC++];
                byte ptrHi = memory[PC++];
                ushort ptr = (ushort)(ptrLo | (ptrHi << 8));

                // 6502 bug workaround
                byte low = memory[ptr];
                byte high = memory[(ushort)((ptr & 0xFF00) | ((ptr + 1) & 0x00FF))];

                PC = (ushort)(low | (high << 8));
                break;
            case 0x8C: //STY abs
                memory[FetchAbsoluteAddress()] = Y;
                break;
            case 0xAC: //LDY abs
                Y = ResolveAbsolute();
                SetFlag(FLAG_Z, Y == 0);
                SetFlag(FLAG_N, (Y & 0x80) != 0);
                break;
            case 0xBC: //LDY abs,x
                Y = ResolveAbsoluteX();
                SetFlag(FLAG_Z, Y == 0);
                SetFlag(FLAG_N, (Y & 0x80) != 0);
                break;
            case 0xCC: //CPY abs
                value = ResolveAbsolute();
                result = Y - value;
                SetFlag(FLAG_C, Y >= value);                     // No borrow = Carry set
                SetFlag(FLAG_Z, (byte)result == 0);               // Equal = Zero set
                SetFlag(FLAG_N, (result & 0x80) != 0);            // Bit 7 = Negative
                break;
            case 0xEC: //CPX abs
                value = ResolveAbsolute(); 
                result = X - value;
                SetFlag(FLAG_C, X >= value);                     // No borrow = Carry set
                SetFlag(FLAG_Z, (byte)result == 0);               // Equal = Zero set
                SetFlag(FLAG_N, (result & 0x80) != 0);            // Bit 7 = Negative
                break;

            //High byte D ----------------------------------------------------------------------------------------------------
            case 0x0D: //ORA abs
                value = ResolveAbsolute();
                AC |= value; // Or AC with Memory
                SetFlag(FLAG_Z, AC == 0);
                SetFlag(FLAG_N, (AC & 0x80) != 0);
                break;
            case 0x1D: //ORA abs,x
                value = ResolveAbsoluteX();
                AC |= value; // Or AC with Memory
                SetFlag(FLAG_Z, AC == 0);
                SetFlag(FLAG_N, (AC & 0x80) != 0);
                break;
            case 0x2D: //AND abs
                value = ResolveAbsolute();
                AC &= value; // AND AC with Memory
                SetFlag(FLAG_Z, AC == 0);
                SetFlag(FLAG_N, (AC & 0x80) != 0);
                break;
            case 0x3D: //AND abs,x
                value = ResolveAbsoluteX();
                AC &= value; // AND AC with Memory
                SetFlag(FLAG_Z, AC == 0);
                SetFlag(FLAG_N, (AC & 0x80) != 0);
                break;
            case 0x4D: //EOR abs
                value = ResolveAbsolute();
                AC ^= value; // XOR AC with Memory
                SetFlag(FLAG_Z, AC == 0);
                SetFlag(FLAG_N, (AC & 0x80) != 0);
                break;
            case 0x5D: //EOR abs,x
                value = ResolveAbsoluteX();
                AC ^= value; // XOR AC with Memory
                SetFlag(FLAG_Z, AC == 0);
                SetFlag(FLAG_N, (AC & 0x80) != 0);
                break;
            case 0x6D: //ADC abs
                value = ResolveAbsolute();
                if (GetFlag(FLAG_D))
                {
                    ADC_BCD(value);
                }
                else
                {
                    carry = GetFlag(FLAG_C) ? 1 : 0;
                    sum = AC + value + carry;
                    SetFlag(FLAG_C, sum > 0xFF);
                    SetFlag(FLAG_Z, (byte)sum == 0);
                    SetFlag(FLAG_N, (sum & 0x80) != 0);
                    SetFlag(FLAG_V, ((AC ^ value) & 0x80) == 0 && ((AC ^ sum) & 0x80) != 0);
                    AC = (byte)(sum & 0xff);
                }
                break;
            case 0x7D: //ADC abs,x
                value = ResolveAbsoluteX();
                if (GetFlag(FLAG_D))
                {
                    ADC_BCD(value);
                }
                else
                {
                    carry = GetFlag(FLAG_C) ? 1 : 0;
                    sum = AC + value + carry;
                    SetFlag(FLAG_C, sum > 0xFF);
                    SetFlag(FLAG_Z, (byte)sum == 0);
                    SetFlag(FLAG_N, (sum & 0x80) != 0);
                    SetFlag(FLAG_V, ((AC ^ value) & 0x80) == 0 && ((AC ^ sum) & 0x80) != 0);
                    AC = (byte)(sum & 0xff);
                }
                break;
            case 0x8D://STA abs
                absAddr = FetchAbsoluteAddress();
                memory[absAddr] = AC;
                break;
            case 0x9D://STA abs,x
                absAddr = FetchAbsoluteAddress();
                memory[(ushort)(absAddr + X)] = AC;
                break;
            case 0xAD://LDA abs
                AC = ResolveAbsolute();
                SetFlag(FLAG_Z, AC == 0);
                SetFlag(FLAG_N, (AC & 0x80) != 0);
                break;
            case 0xBD://LDA abs,x
                AC = ResolveAbsoluteX();
                SetFlag(FLAG_Z, AC == 0);
                SetFlag(FLAG_N, (AC & 0x80) != 0);
                break;
            case 0xCD://CMP abs
                value = ResolveAbsolute();
                result = AC - value;
                SetFlag(FLAG_C, AC >= value);                     // No borrow = Carry set
                SetFlag(FLAG_Z, (byte)result == 0);               // Equal = Zero set
                SetFlag(FLAG_N, (result & 0x80) != 0);            // Bit 7 = Negative
                break;
            case 0xDD://CMP abs,x
                value = ResolveAbsoluteX();
                result = AC - value;
                SetFlag(FLAG_C, AC >= value);                     // No borrow = Carry set
                SetFlag(FLAG_Z, (byte)result == 0);               // Equal = Zero set
                SetFlag(FLAG_N, (result & 0x80) != 0);            // Bit 7 = Negative
                break;
            case 0xED: //SBC abs
                if (GetFlag(FLAG_D))
                {
                    value = ResolveAbsolute();
                    SBC_BCD(value);
                }
                else
                {
                    intvalue = ResolveAbsolute();
                    carry = GetFlag(FLAG_C) ? 1 : 0; 
                    sum = AC - intvalue - (1 - carry);

                    SetFlag(FLAG_C, sum >= 0); 
                    SetFlag(FLAG_Z, (sum & 0xFF) == 0);
                    SetFlag(FLAG_N, (sum & 0x80) != 0);
                    SetFlag(FLAG_V, ((AC ^ sum) & (AC ^ intvalue) & 0x80) != 0);

                    AC = (byte)(sum & 0xFF);
                }
                break;
            case 0xFD: //SBC abs,x
                if (GetFlag(FLAG_D))
                {
                    value = ResolveAbsoluteX();
                    SBC_BCD(value);
                }
                else
                {
                    intvalue = ResolveAbsoluteX();
                    carry = GetFlag(FLAG_C) ? 1 : 0; 
                    sum = AC - intvalue - (1 - carry);

                    SetFlag(FLAG_C, sum >= 0); 
                    SetFlag(FLAG_Z, (sum & 0xFF) == 0);
                    SetFlag(FLAG_N, (sum & 0x80) != 0);
                    SetFlag(FLAG_V, ((AC ^ sum) & (AC ^ intvalue) & 0x80) != 0);

                    AC = (byte)(sum & 0xFF);
                }

                break;

            //High byte E ----------------------------------------------------------------------------------------------------
            case 0x0E: // ASL abs
                absAddr = FetchAbsoluteAddress(); 
                value = memory[absAddr];
                SetFlag(FLAG_C, (value & 0x80) != 0);
                value <<= 1;
                memory[absAddr] = value;
                SetFlag(FLAG_Z, value == 0);
                SetFlag(FLAG_N, (value & 0x80) != 0);
                break;
            case 0x1E: // ASL abs,x
                absAddr = FetchAbsoluteAddress();
                value = memory[(ushort)(absAddr + X)];
                SetFlag(FLAG_C, (value & 0x80) != 0);
                value <<= 1;
                memory[(ushort)(absAddr + X)] = value;
                SetFlag(FLAG_Z, value == 0);
                SetFlag(FLAG_N, (value & 0x80) != 0);
                break;
            case 0x2E: // ROL abs
                absAddr = FetchAbsoluteAddress(); 
                value = memory[absAddr];
                oldCarry = GetFlag(FLAG_C);
                SetFlag(FLAG_C, (value & 0x80) != 0);
                value = (byte)((value << 1) | (oldCarry ? 1 : 0));
                memory[absAddr] = value;
                SetFlag(FLAG_Z, value == 0);
                SetFlag(FLAG_N, (value & 0x80) != 0);
                break;
            case 0x3E: // ROL abs,x
                absAddr = FetchAbsoluteAddress();
                value = memory[(ushort)(absAddr + X)];
                oldCarry = GetFlag(FLAG_C);
                SetFlag(FLAG_C, (value & 0x80) != 0);
                value = (byte)((value << 1) | (oldCarry ? 1 : 0));
                memory[(ushort)(absAddr + X)] = value;
                SetFlag(FLAG_Z, value == 0);
                SetFlag(FLAG_N, (value & 0x80) != 0);
                break;
            case 0x4E: // LSR abs
                absAddr = FetchAbsoluteAddress(); 
                value = memory[absAddr];
                SetFlag(FLAG_C, (value & 0x01) != 0);
                value >>= 1;
                memory[absAddr] = value;
                SetFlag(FLAG_Z, value == 0);
                SetFlag(FLAG_N, false);
                break;
            case 0x5E: //LSR abs,x
                absAddr = FetchAbsoluteAddress();
                value = memory[(ushort)(absAddr + X)];
                SetFlag(FLAG_C, (value & 0x01) != 0);
                value >>= 1;
                memory[(ushort)(absAddr + X)] = value;
                SetFlag(FLAG_Z, value == 0);
                SetFlag(FLAG_N, false);
                break;
            case 0x6E: // ROR abs
                absAddr = FetchAbsoluteAddress(); 
                value = memory[absAddr];
                oldCarry = GetFlag(FLAG_C);
                SetFlag(FLAG_C, (value & 0x01) != 0);
                value = (byte)((value >> 1) | (oldCarry ? 0x80 : 0));
                memory[absAddr] = value;
                SetFlag(FLAG_Z, value == 0);
                SetFlag(FLAG_N, (value & 0x80) != 0);
                break;
            case 0x7E: // ROR abs,x
                absAddr = FetchAbsoluteAddress();
                value = memory[(ushort)(absAddr + X)];
                oldCarry = GetFlag(FLAG_C);
                SetFlag(FLAG_C, (value & 0x01) != 0);
                value = (byte)((value >> 1) | (oldCarry ? 0x80 : 0));
                memory[(ushort)(absAddr + X)] = value;
                SetFlag(FLAG_Z, value == 0);
                SetFlag(FLAG_N, (value & 0x80) != 0);
                break;
            case 0x8E: //STX abs
                absAddr = FetchAbsoluteAddress();
                memory[absAddr] = X;
                break;
            case 0xAE: //LDX abs
                value = ResolveAbsolute();
                X = value;
                SetFlag(FLAG_Z, value == 0);
                SetFlag(FLAG_N, (value & 0x80) != 0);
                break;
            case 0xBE: //LDX abs,y
                value = ResolveAbsoluteY();
                X = value;
                SetFlag(FLAG_Z, value == 0);
                SetFlag(FLAG_N, (value & 0x80) != 0);
                break;
            case 0xCE: // DEC abs
                absAddr = FetchAbsoluteAddress();
                value = memory[absAddr];
                value--;
                memory[absAddr] = value;
                SetFlag(FLAG_Z, value == 0);
                SetFlag(FLAG_N, (value & 0x80) != 0);
                break;
            case 0xDE: // DEC abs,x
                absAddr = FetchAbsoluteAddress();
                value = memory[(ushort)(absAddr + X)];
                value--;
                memory[(ushort)(absAddr + X)] = value;
                SetFlag(FLAG_Z, value == 0);
                SetFlag(FLAG_N, (value & 0x80) != 0);
                break;
            case 0xEE: // INC abs
                absAddr = FetchAbsoluteAddress();
                value = memory[absAddr];
                value++;
                memory[absAddr] = value;
                SetFlag(FLAG_Z, value == 0);
                SetFlag(FLAG_N, (value & 0x80) != 0);
                break;
            case 0xFE: // INC abs,x
                absAddr = FetchAbsoluteAddress();
                value = memory[(ushort)(absAddr + X)];
                value++;
                memory[(ushort)(absAddr + X)] = value;
                SetFlag(FLAG_Z, value == 0);
                SetFlag(FLAG_N, (value & 0x80) != 0);
                break;

        }

    }

    public void Step()
    {
        byte opcode = memory[PC++];
       // DebugInstruction(opcode);
        InterpretOpcode(opcode);
    }


   public void DebugInstruction(byte opcode)
    {
        string mnemonic = opcode switch
        {
            0x00 => "BRK",
            0x01 => "ORA (Indirect,X)",
            0x05 => "ORA ZeroPage",
            0x06 => "ASL ZeroPage",
            0x08 => "PHP",
            0x09 => "ORA #Immediate",
            0x0A => "ASL A",
            0x0D => "ORA Absolute",
            0x0E => "ASL Absolute",
            0x10 => "BPL",
            0x11 => "ORA (Indirect),Y",
            0x15 => "ORA ZeroPage,X",
            0x16 => "ASL ZeroPage,X",
            0x18 => "CLC",
            0x19 => "ORA Absolute,Y",
            0x1D => "ORA Absolute,X",
            0x1E => "ASL Absolute,X",

            0x20 => "JSR",
            0x21 => "AND (Indirect,X)",
            0x24 => "BIT ZeroPage",
            0x25 => "AND ZeroPage",
            0x26 => "ROL ZeroPage",
            0x28 => "PLP",
            0x29 => "AND #Immediate",
            0x2A => "ROL A",
            0x2C => "BIT Absolute",
            0x2D => "AND Absolute",
            0x2E => "ROL Absolute",

            0x30 => "BMI",
            0x31 => "AND (Indirect),Y",
            0x35 => "AND ZeroPage,X",
            0x36 => "ROL ZeroPage,X",
            0x38 => "SEC",
            0x39 => "AND Absolute,Y",
            0x3D => "AND Absolute,X",
            0x3E => "ROL Absolute,X",

            0x40 => "RTI",
            0x41 => "EOR (Indirect,X)",
            0x45 => "EOR ZeroPage",
            0x46 => "LSR ZeroPage",
            0x48 => "PHA",
            0x49 => "EOR #Immediate",
            0x4A => "LSR A",
            0x4C => "JMP Absolute",
            0x4D => "EOR Absolute",
            0x4E => "LSR Absolute",

            0x50 => "BVC",
            0x51 => "EOR (Indirect),Y",
            0x55 => "EOR ZeroPage,X",
            0x56 => "LSR ZeroPage,X",
            0x58 => "CLI",
            0x59 => "EOR Absolute,Y",
            0x5D => "EOR Absolute,X",
            0x5E => "LSR Absolute,X",

            0x60 => "RTS",
            0x61 => "ADC (Indirect,X)",
            0x65 => "ADC ZeroPage",
            0x66 => "ROR ZeroPage",
            0x68 => "PLA",
            0x69 => "ADC #Immediate",
            0x6A => "ROR A",
            0x6C => "JMP Indirect",
            0x6D => "ADC Absolute",
            0x6E => "ROR Absolute",

            0x70 => "BVS",
            0x71 => "ADC (Indirect),Y",
            0x75 => "ADC ZeroPage,X",
            0x76 => "ROR ZeroPage,X",
            0x78 => "SEI",
            0x79 => "ADC Absolute,Y",
            0x7D => "ADC Absolute,X",
            0x7E => "ROR Absolute,X",

            0x81 => "STA (Indirect,X)",
            0x84 => "STY ZeroPage",
            0x85 => "STA ZeroPage",
            0x86 => "STX ZeroPage",
            0x88 => "DEY",
            0x8A => "TXA",
            0x8C => "STY Absolute",
            0x8D => "STA Absolute",
            0x8E => "STX Absolute",

            0x90 => "BCC",
            0x91 => "STA (Indirect),Y",
            0x94 => "STY ZeroPage,X",
            0x95 => "STA ZeroPage,X",
            0x96 => "STX ZeroPage,Y",
            0x98 => "TYA",
            0x99 => "STA Absolute,Y",
            0x9A => "TXS",
            0x9D => "STA Absolute,X",

            0xA0 => "LDY #Immediate",
            0xA1 => "LDA (Indirect,X)",
            0xA2 => "LDX #Immediate",
            0xA4 => "LDY ZeroPage",
            0xA5 => "LDA ZeroPage",
            0xA6 => "LDX ZeroPage",
            0xA8 => "TAY",
            0xA9 => "LDA #Immediate",
            0xAA => "TAX",
            0xAC => "LDY Absolute",
            0xAD => "LDA Absolute",
            0xAE => "LDX Absolute",

            0xB0 => "BCS",
            0xB1 => "LDA (Indirect),Y",
            0xB4 => "LDY ZeroPage,X",
            0xB5 => "LDA ZeroPage,X",
            0xB6 => "LDX ZeroPage,Y",
            0xB8 => "CLV",
            0xB9 => "LDA Absolute,Y",
            0xBA => "TSX",
            0xBC => "LDY Absolute,X",
            0xBD => "LDA Absolute,X",
            0xBE => "LDX Absolute,Y",

            0xC0 => "CPY #Immediate",
            0xC1 => "CMP (Indirect,X)",
            0xC4 => "CPY ZeroPage",
            0xC5 => "CMP ZeroPage",
            0xC6 => "DEC ZeroPage",
            0xC8 => "INY",
            0xC9 => "CMP #Immediate",
            0xCA => "DEX",
            0xCC => "CPY Absolute",
            0xCD => "CMP Absolute",
            0xCE => "DEC Absolute",

            0xD0 => "BNE",
            0xD1 => "CMP (Indirect),Y",
            0xD5 => "CMP ZeroPage,X",
            0xD6 => "DEC ZeroPage,X",
            0xD8 => "CLD",
            0xD9 => "CMP Absolute,Y",
            0xDD => "CMP Absolute,X",
            0xDE => "DEC Absolute,X",

            0xE0 => "CPX #Immediate",
            0xE1 => "SBC (Indirect,X)",
            0xE4 => "CPX ZeroPage",
            0xE5 => "SBC ZeroPage",
            0xE6 => "INC ZeroPage",
            0xE8 => "INX",
            0xE9 => "SBC #Immediate",
            0xEA => "NOP",
            0xEC => "CPX Absolute",
            0xED => "SBC Absolute",
            0xEE => "INC Absolute",

            0xF0 => "BEQ",
            0xF1 => "SBC (Indirect),Y",
            0xF5 => "SBC ZeroPage,X",
            0xF6 => "INC ZeroPage,X",
            0xF8 => "SED",
            0xF9 => "SBC Absolute,Y",
            0xFD => "SBC Absolute,X",
            0xFE => "INC Absolute,X",

            _ => "???"
        };

        Console.Write($"PC: {PC:X4}, Opcode: {opcode:X2}, Mnemonic: {mnemonic}, A:{AC:X2} X:{X:X2} Y:{Y:X2} SR:{SR:X2} SP:{SP:X2}");
    }


}
