// netos kernel - GCInfo Decoder
// Parses NativeAOT GCInfo to enumerate GC references on the stack.
//
// GCInfo is located in .xdata after UNWIND_INFO and NativeAOT-specific data.
// It describes which stack slots and registers contain live GC references
// at each safe point (call sites, loop back-edges).
//
// Reference: https://github.com/dotnet/runtime/blob/main/src/coreclr/vm/gcinfodecoder.cpp

using System;
using System.Runtime.InteropServices;
using Kernel.Platform;
using Kernel.X64;

namespace Kernel.Runtime;

/// <summary>
/// Flags for GCInfo header (fat header format).
/// </summary>
public enum GCInfoHeaderFlags : uint
{
    None = 0,
    IsFatHeader = 0x01,           // Bit 0: 1 = fat header, 0 = slim header
    HasSecurityObject = 0x02,     // Bit 1: (unused in modern runtime)
    HasGSCookie = 0x04,           // Bit 2: GS cookie for stack protection
    HasPSPSym = 0x08,             // Bit 3: (unused)
    HasGenericsInstContext = 0x10, // Bit 4: Generic instantiation context
    HasStackBaseRegister = 0x20,  // Bit 5: Has frame pointer
    WantsReportOnlyLeaf = 0x40,   // Bit 6: (AMD64 specific)
    HasEditAndContinue = 0x80,    // Bit 7: Edit and continue info
    HasReversePInvokeFrame = 0x100, // Bit 8: Reverse P/Invoke frame
}

/// <summary>
/// Slot types for tracked references.
/// </summary>
public enum GCSlotBase : byte
{
    Stack = 0,        // Relative to RSP
    CallerSP = 1,     // Relative to caller's RSP
    FramePointer = 2, // Relative to frame pointer (RBP)
}

/// <summary>
/// Represents a tracked GC slot (register or stack location).
/// </summary>
public struct GCSlot
{
    public bool IsRegister;       // True if register, false if stack
    public byte RegisterNumber;   // Register index (0=RAX, 1=RCX, etc.)
    public GCSlotBase StackBase;  // Base for stack offset
    public int StackOffset;       // Offset from base
    public bool IsInterior;       // Interior pointer (points within object)
    public bool IsPinned;         // Pinned (cannot be relocated)
}

/// <summary>
/// Decodes GCInfo to enumerate GC references.
/// </summary>
public unsafe struct GCInfoDecoder
{
    // Pointer to GCInfo data
    private byte* _ptr;
    private byte* _start;

    // Bit position for reading
    private int _bitPosition;

    // Decoded header values
    public uint CodeLength;
    public uint PrologSize;
    public uint EpilogSize;
    public int GsCookieStackSlot;
    public int GenericsInstContextStackSlot;
    public int StackBaseRegister;
    public int ReversePInvokeFrameSlot;
    public uint NumSafePoints;
    public uint NumInterruptibleRanges;

    // Slot table
    public uint NumRegisters;
    public uint NumStackSlots;
    public uint NumUntrackedSlots;

    // Total tracked slots (registers + stack)
    public uint NumTrackedSlots => NumRegisters + NumStackSlots;

    // Header flags
    public bool IsFatHeader;
    public bool HasGSCookie;
    public bool HasGenericsInstContext;
    public bool HasStackBaseRegister;
    public bool WantsReportOnlyLeaf;
    public bool HasEditAndContinue;
    public bool HasReversePInvokeFrame;

    // AMD64-specific encoding bases (from gcinfotypes.h)
    private const int CODE_LENGTH_ENCBASE = 8;
    private const int NORM_PROLOG_SIZE_ENCBASE = 5;
    private const int NORM_EPILOG_SIZE_ENCBASE = 3;
    private const int STACK_BASE_REGISTER_ENCBASE = 3;
    private const int GS_COOKIE_STACK_SLOT_ENCBASE = 6;
    private const int GENERICS_INST_CONTEXT_STACK_SLOT_ENCBASE = 6;
    private const int REVERSE_PINVOKE_FRAME_ENCBASE = 6;
    private const int NUM_SAFE_POINTS_ENCBASE = 2;
    private const int NUM_INTERRUPTIBLE_RANGES_ENCBASE = 1;
    private const int NUM_REGISTERS_ENCBASE = 2;
    private const int NUM_STACK_SLOTS_ENCBASE = 2;
    private const int NUM_UNTRACKED_SLOTS_ENCBASE = 1;
    private const int REGISTER_ENCBASE = 3;
    private const int REGISTER_DELTA_ENCBASE = 2;
    private const int STACK_SLOT_ENCBASE = 6;
    private const int STACK_SLOT_DELTA_ENCBASE = 4;
    private const int SIZE_OF_EDIT_AND_CONTINUE_PRESERVED_AREA_ENCBASE = 4;
    private const int SIZE_OF_STACK_AREA_ENCBASE = 3; // AMD64-specific: SizeOfStackOutgoingAndScratchArea

    // Safe point and liveness encoding bases
    private const int NORM_CODE_OFFSET_DELTA_ENCBASE = 3;
    private const int INTERRUPTIBLE_RANGE_DELTA1_ENCBASE = 6;
    private const int INTERRUPTIBLE_RANGE_DELTA2_ENCBASE = 6;
    private const int LIVESTATE_RLE_RUN_ENCBASE = 2;
    private const int LIVESTATE_RLE_SKIP_ENCBASE = 4;
    private const int POINTER_SIZE_ENCBASE = 3;

    // Maximum slots we can track (fixed array size for kernel - no heap allocation)
    private const int MAX_SLOTS = 64;
    private const int MAX_SAFE_POINTS = 256;

    // Decoded slot table (fixed-size array to avoid heap allocation)
    private fixed byte _slotData[MAX_SLOTS * 8]; // GCSlot is ~8 bytes
    private int _numDecodedSlots;

    // Safe point offsets (normalized code offsets where GC can happen)
    private fixed uint _safePointOffsets[MAX_SAFE_POINTS];

    // Bit position where liveness data starts (after safe point offsets)
    private int _livenessDataBitPosition;

    // Number of bits needed to represent a code offset
    private int _numBitsPerOffset;

    // Indirect live slot table info
    private bool _hasIndirectLiveSlotTable;
    private int _liveSlotPointerBits;
    private int _pointerTableBitPosition;
    private int _liveStateDataBitPosition;

    /// <summary>
    /// Initialize decoder with GCInfo data pointer.
    /// </summary>
    public GCInfoDecoder(byte* gcInfo)
    {
        _start = gcInfo;
        _ptr = gcInfo;
        _bitPosition = 0;

        // Zero all fields
        CodeLength = 0;
        PrologSize = 0;
        EpilogSize = 0;
        GsCookieStackSlot = 0;
        GenericsInstContextStackSlot = 0;
        StackBaseRegister = -1;
        ReversePInvokeFrameSlot = 0;
        NumSafePoints = 0;
        NumInterruptibleRanges = 0;
        NumRegisters = 0;
        NumStackSlots = 0;
        NumUntrackedSlots = 0;

        IsFatHeader = false;
        HasGSCookie = false;
        HasGenericsInstContext = false;
        HasStackBaseRegister = false;
        WantsReportOnlyLeaf = false;
        HasEditAndContinue = false;
        HasReversePInvokeFrame = false;

        _numDecodedSlots = 0;
        _livenessDataBitPosition = 0;
        _numBitsPerOffset = 0;

        _hasIndirectLiveSlotTable = false;
        _liveSlotPointerBits = 0;
        _pointerTableBitPosition = 0;
        _liveStateDataBitPosition = 0;
    }

    /// <summary>
    /// Decode the GCInfo header.
    /// </summary>
    public bool DecodeHeader(bool debug = false)
    {
        if (_ptr == null) return false;

        if (debug)
        {
            // Dump first 32 bytes of GCInfo
            DebugConsole.Write("      [GCInfo bytes 0-7] ");
            for (int b = 0; b < 8; b++)
            {
                DebugConsole.WriteHex(_ptr[b]);
                DebugConsole.Write(" ");
            }
            DebugConsole.WriteLine();
            DebugConsole.Write("      [GCInfo bytes 8-15] ");
            for (int b = 8; b < 16; b++)
            {
                DebugConsole.WriteHex(_ptr[b]);
                DebugConsole.Write(" ");
            }
            DebugConsole.WriteLine();
            DebugConsole.Write("      [GCInfo bytes 16-23] ");
            for (int b = 16; b < 24; b++)
            {
                DebugConsole.WriteHex(_ptr[b]);
                DebugConsole.Write(" ");
            }
            DebugConsole.WriteLine();
            DebugConsole.Write("      [GCInfo bytes 24-31] ");
            for (int b = 24; b < 32; b++)
            {
                DebugConsole.WriteHex(_ptr[b]);
                DebugConsole.Write(" ");
            }
            DebugConsole.WriteLine();
        }

        // First bit indicates slim (0) or fat (1) header
        IsFatHeader = ReadBits(1) != 0;

        if (debug)
        {
            DebugConsole.Write("      [Header] IsFat=");
            DebugConsole.Write(IsFatHeader ? "Y" : "N");
        }

        if (IsFatHeader)
        {
            // Fat header: read flags
            // Bits 1-10 contain header flags
            /*
             * Bit 1: IsVarArg (unused)
             * Bit 2: HasSecurityObject (unused)
             * Bit 3: HasGSCookie
             * Bit 4: HasPSPSym (unused)
             * Bits 5-6: ContextParamType (2 bits)
             * Bit 7: HasStackBaseRegister
             * Bit 8: WantsReportOnlyLeaf (AMD64)
             * Bit 9: HasEditAndContinue
             * Bit 10: HasReversePInvokeFrame
             */
            ReadBits(1); // IsVarArg - skip
            ReadBits(1); // HasSecurityObject - skip
            HasGSCookie = ReadBits(1) != 0;
            ReadBits(1); // HasPSPSym - skip
            uint contextParamType = ReadBits(2);
            HasGenericsInstContext = contextParamType != 0;
            HasStackBaseRegister = ReadBits(1) != 0;
            WantsReportOnlyLeaf = ReadBits(1) != 0;
            HasEditAndContinue = ReadBits(1) != 0;
            HasReversePInvokeFrame = ReadBits(1) != 0;
        }
        else
        {
            // Slim header: just stack base register flag
            HasStackBaseRegister = ReadBits(1) != 0;
        }

        // Code length - AMD64 has NO normalization (identity function)
        // DENORMALIZE_CODE_LENGTH(x) = x for AMD64
        int bitPosBefore = _bitPosition;
        CodeLength = DecodeVarLengthUnsigned(CODE_LENGTH_ENCBASE);
        if (debug)
        {
            DebugConsole.Write(" bitsForCode=");
            DebugConsole.WriteDecimal((uint)(bitPosBefore));
            DebugConsole.Write("-");
            DebugConsole.WriteDecimal((uint)_bitPosition);
        }

        // GS cookie info (if present)
        if (HasGSCookie)
        {
            PrologSize = DecodeVarLengthUnsigned(NORM_PROLOG_SIZE_ENCBASE) + 1;
            EpilogSize = DecodeVarLengthUnsigned(NORM_EPILOG_SIZE_ENCBASE);
            GsCookieStackSlot = DecodeVarLengthSigned(GS_COOKIE_STACK_SLOT_ENCBASE);
        }

        // Generics context
        if (HasGenericsInstContext)
        {
            GenericsInstContextStackSlot = DecodeVarLengthSigned(GENERICS_INST_CONTEXT_STACK_SLOT_ENCBASE);
        }

        // Stack base register
        // For slim headers, HasStackBaseRegister=true means use RBP (value 0, denormalizes to 5)
        // For fat headers, the value is explicitly encoded
        if (HasStackBaseRegister)
        {
            if (IsFatHeader)
            {
                StackBaseRegister = (int)DecodeVarLengthUnsigned(STACK_BASE_REGISTER_ENCBASE);
                // Denormalize: actual = encoded ^ 5
                StackBaseRegister ^= 5;
            }
            else
            {
                // Slim header: implicit value of 0 denormalizes to RBP (5)
                StackBaseRegister = 5; // RBP
            }
        }

        // Edit and continue
        if (HasEditAndContinue)
        {
            // Skip preserved area size
            DecodeVarLengthUnsigned(SIZE_OF_EDIT_AND_CONTINUE_PRESERVED_AREA_ENCBASE);
        }

        // Reverse P/Invoke frame
        if (HasReversePInvokeFrame)
        {
            ReversePInvokeFrameSlot = DecodeVarLengthSigned(REVERSE_PINVOKE_FRAME_ENCBASE);
        }

        // AMD64-specific: SizeOfStackOutgoingAndScratchArea (for fat headers only)
        // This field is ALWAYS present for AMD64 fat headers, not conditional on any flag
        if (IsFatHeader)
        {
            // Skip this field - we don't need the value, just need to advance past it
            DecodeVarLengthUnsigned(SIZE_OF_STACK_AREA_ENCBASE);
        }

        // Number of safe points (always encoded)
        NumSafePoints = DecodeVarLengthUnsigned(NUM_SAFE_POINTS_ENCBASE);

        // Number of interruptible ranges - ONLY for fat headers!
        // Slim headers always have 0 interruptible ranges (not encoded)
        if (IsFatHeader)
        {
            NumInterruptibleRanges = DecodeVarLengthUnsigned(NUM_INTERRUPTIBLE_RANGES_ENCBASE);
        }
        else
        {
            NumInterruptibleRanges = 0;
        }

        if (debug)
        {
            DebugConsole.Write(" Code=");
            DebugConsole.WriteDecimal(CodeLength);
            DebugConsole.Write(" SP=");
            DebugConsole.WriteDecimal(NumSafePoints);
            DebugConsole.Write(" IR=");
            DebugConsole.WriteDecimal(NumInterruptibleRanges);
            DebugConsole.Write(" bitPos=");
            DebugConsole.WriteDecimal((uint)_bitPosition);
            DebugConsole.WriteLine();
        }

        return true;
    }

    /// <summary>
    /// Decode safe point offsets, interruptible ranges, and slot table counts.
    /// Call after DecodeHeader().
    ///
    /// NativeAOT GCInfo order after header:
    /// 1. Safe point offsets (NumSafePoints * ceil(log2(CodeLength)) bits each)
    /// 2. Interruptible ranges (if any)
    /// 3. Slot table counts (register count, stack count, untracked count)
    /// </summary>
    public bool DecodeSlotTable(bool debug = false)
    {
        // ========== SAFE POINT OFFSETS ==========
        // These come FIRST after the header
        _numBitsPerOffset = CeilLog2(CodeLength);
        if (_numBitsPerOffset == 0)
            _numBitsPerOffset = 1; // Minimum 1 bit

        uint numSafePoints = NumSafePoints;
        if (numSafePoints > MAX_SAFE_POINTS)
            numSafePoints = MAX_SAFE_POINTS;

        int safePointStartPos = _bitPosition;
        fixed (uint* spPtr = _safePointOffsets)
        {
            for (uint i = 0; i < numSafePoints; i++)
            {
                spPtr[i] = ReadBits(_numBitsPerOffset);
            }
        }

        // Skip any remaining safe points if we hit our max
        if (NumSafePoints > MAX_SAFE_POINTS)
        {
            int remaining = (int)(NumSafePoints - MAX_SAFE_POINTS);
            _bitPosition += remaining * _numBitsPerOffset;
        }

        // ========== INTERRUPTIBLE RANGES ==========
        // Skip interruptible ranges if present (we only support safe points for now)
        int irStartPos = _bitPosition;
        for (uint i = 0; i < NumInterruptibleRanges; i++)
        {
            DecodeVarLengthUnsigned(INTERRUPTIBLE_RANGE_DELTA1_ENCBASE); // start delta
            DecodeVarLengthUnsigned(INTERRUPTIBLE_RANGE_DELTA2_ENCBASE); // stop delta
        }
        int irEndPos = _bitPosition;

        // ========== SLOT TABLE COUNTS ==========
        int slotCountStartPos = _bitPosition;

        // Number of register slots (preceded by presence bit)
        if (ReadBits(1) != 0)
        {
            NumRegisters = DecodeVarLengthUnsigned(NUM_REGISTERS_ENCBASE);
        }
        else
        {
            NumRegisters = 0;
        }

        // Stack slots and untracked slots share ONE presence bit
        if (ReadBits(1) != 0)
        {
            NumStackSlots = DecodeVarLengthUnsigned(NUM_STACK_SLOTS_ENCBASE);
            NumUntrackedSlots = DecodeVarLengthUnsigned(NUM_UNTRACKED_SLOTS_ENCBASE);
        }
        else
        {
            NumStackSlots = 0;
            NumUntrackedSlots = 0;
        }

        if (debug)
        {
            DebugConsole.Write("      [SafePoints] bits=");
            DebugConsole.WriteDecimal((uint)safePointStartPos);
            DebugConsole.Write(" bitsPerOff=");
            DebugConsole.WriteDecimal((uint)_numBitsPerOffset);
            DebugConsole.Write(" count=");
            DebugConsole.WriteDecimal(NumSafePoints);
            if (numSafePoints > 0)
            {
                DebugConsole.Write(" first=");
                fixed (uint* spPtr = _safePointOffsets)
                {
                    DebugConsole.WriteDecimal(spPtr[0]);
                }
            }
            DebugConsole.WriteLine();

            DebugConsole.Write("      [IntRanges] bits=");
            DebugConsole.WriteDecimal((uint)irStartPos);
            DebugConsole.Write("-");
            DebugConsole.WriteDecimal((uint)irEndPos);
            DebugConsole.Write(" count=");
            DebugConsole.WriteDecimal(NumInterruptibleRanges);
            DebugConsole.WriteLine();

            DebugConsole.Write("      [SlotCounts] bits=");
            DebugConsole.WriteDecimal((uint)slotCountStartPos);
            DebugConsole.Write("-");
            DebugConsole.WriteDecimal((uint)_bitPosition);
            DebugConsole.Write(" regs=");
            DebugConsole.WriteDecimal(NumRegisters);
            DebugConsole.Write(" stk=");
            DebugConsole.WriteDecimal(NumStackSlots);
            DebugConsole.Write(" untrk=");
            DebugConsole.WriteDecimal(NumUntrackedSlots);
            DebugConsole.WriteLine();
        }

        return true;
    }

    /// <summary>
    /// Read a specific slot from the slot table.
    /// Must be called in order (0, 1, 2, ...) as encoding is delta-based.
    /// </summary>
    public GCSlot ReadSlot(uint slotIndex, ref int prevRegister, ref int prevStackOffset)
    {
        GCSlot slot = default;

        if (slotIndex < NumRegisters)
        {
            // Register slot
            slot.IsRegister = true;

            if (slotIndex == 0)
            {
                // First register - absolute encoding
                slot.RegisterNumber = (byte)DecodeVarLengthUnsigned(REGISTER_ENCBASE);
            }
            else
            {
                // Subsequent registers - delta encoding
                uint delta = DecodeVarLengthUnsigned(REGISTER_DELTA_ENCBASE) + 1;
                slot.RegisterNumber = (byte)(prevRegister + delta);
            }
            prevRegister = slot.RegisterNumber;

            // Flags
            uint flags = ReadBits(2);
            slot.IsInterior = (flags & 1) != 0;
            slot.IsPinned = (flags & 2) != 0;
        }
        else
        {
            // Stack slot
            slot.IsRegister = false;
            uint stackSlotIndex = slotIndex - NumRegisters;

            if (stackSlotIndex == 0)
            {
                // First stack slot - absolute encoding
                slot.StackBase = (GCSlotBase)ReadBits(2);
                slot.StackOffset = (int)DecodeVarLengthUnsigned(STACK_SLOT_ENCBASE);
            }
            else
            {
                // Subsequent stack slots - delta encoding (same base as previous)
                slot.StackBase = (GCSlotBase)0; // Uses previous base
                uint delta = DecodeVarLengthUnsigned(STACK_SLOT_DELTA_ENCBASE);
                slot.StackOffset = prevStackOffset + (int)delta;
            }
            prevStackOffset = slot.StackOffset;

            // Flags
            uint flags = ReadBits(2);
            slot.IsInterior = (flags & 1) != 0;
            slot.IsPinned = (flags & 2) != 0;
        }

        return slot;
    }

    /// <summary>
    /// Decode slot definitions and safe point data after DecodeSlotTable().
    /// This reads the actual slot encodings and safe point offsets.
    ///
    /// Matches NativeAOT's DecodeSlotTable exactly:
    /// - Registers, stack slots, and untracked slots are decoded in separate sections
    /// - The FIRST slot of each section is ALWAYS absolute
    /// - Subsequent slots within a section use delta if prevFlags==0, absolute if prevFlags!=0
    /// </summary>
    public bool DecodeSlotDefinitionsAndSafePoints(bool debug = false)
    {
        if (debug)
        {
            DebugConsole.Write("      [Decode] bitPos=");
            DebugConsole.WriteDecimal((uint)_bitPosition);
            DebugConsole.Write(" regs=");
            DebugConsole.WriteDecimal(NumRegisters);
            DebugConsole.Write(" stk=");
            DebugConsole.WriteDecimal(NumStackSlots);
            DebugConsole.Write(" untrk=");
            DebugConsole.WriteDecimal(NumUntrackedSlots);
            DebugConsole.WriteLine();
        }

        fixed (byte* slotPtr = _slotData)
        {
            var slots = (GCSlot*)slotPtr;
            uint i = 0;

            // ==================== REGISTER SLOTS ====================
            if (NumRegisters > 0)
            {
                if (debug)
                {
                    // Dump raw bits at current position for debugging
                    int byteOff = _bitPosition / 8;
                    int bitOff = _bitPosition % 8;
                    DebugConsole.Write("      [RawBits at ");
                    DebugConsole.WriteDecimal((uint)_bitPosition);
                    DebugConsole.Write("] byte[");
                    DebugConsole.WriteDecimal((uint)byteOff);
                    DebugConsole.Write("]=0x");
                    DebugConsole.WriteHex(_ptr[byteOff]);
                    DebugConsole.Write(" byte[");
                    DebugConsole.WriteDecimal((uint)(byteOff + 1));
                    DebugConsole.Write("]=0x");
                    DebugConsole.WriteHex(_ptr[byteOff + 1]);
                    DebugConsole.Write(" bitOff=");
                    DebugConsole.WriteDecimal((uint)bitOff);
                    DebugConsole.WriteLine();
                }

                // First register - always absolute encoding
                uint regNum = DecodeVarLengthUnsigned(REGISTER_ENCBASE);
                uint flags = ReadBits(2);

                if (i < MAX_SLOTS)
                {
                    slots[i].IsRegister = true;
                    slots[i].RegisterNumber = (byte)regNum;
                    slots[i].IsInterior = (flags & 1) != 0;
                    slots[i].IsPinned = (flags & 2) != 0;
                }
                if (debug)
                {
                    DebugConsole.Write("      [Reg ");
                    DebugConsole.WriteDecimal(i);
                    DebugConsole.Write("] reg=");
                    DebugConsole.WriteDecimal(regNum);
                    DebugConsole.Write(" flags=");
                    DebugConsole.WriteDecimal(flags);
                    DebugConsole.WriteLine();
                }
                i++;

                // Subsequent registers
                uint loopEnd = NumRegisters;
                if (loopEnd > MAX_SLOTS) loopEnd = MAX_SLOTS;

                for (; i < loopEnd; i++)
                {
                    if (flags != 0)
                    {
                        // Absolute encoding
                        regNum = DecodeVarLengthUnsigned(REGISTER_ENCBASE);
                        flags = ReadBits(2);
                    }
                    else
                    {
                        // Delta encoding - decode delta and add to previous register
                        uint delta = DecodeVarLengthUnsigned(REGISTER_DELTA_ENCBASE) + 1;
                        regNum += delta;
                        // flags stays 0
                    }

                    slots[i].IsRegister = true;
                    slots[i].RegisterNumber = (byte)regNum;
                    slots[i].IsInterior = (flags & 1) != 0;
                    slots[i].IsPinned = (flags & 2) != 0;

                    if (debug)
                    {
                        DebugConsole.Write("      [Reg ");
                        DebugConsole.WriteDecimal(i);
                        DebugConsole.Write("] reg=");
                        DebugConsole.WriteDecimal(regNum);
                        DebugConsole.Write(" flags=");
                        DebugConsole.WriteDecimal(flags);
                        DebugConsole.WriteLine();
                    }
                }
            }

            // ==================== STACK SLOTS ====================
            if (NumStackSlots > 0 && i < MAX_SLOTS)
            {
                // First stack slot - always absolute encoding
                GCSlotBase spBase = (GCSlotBase)ReadBits(2);
                int normSpOffset = DecodeVarLengthSigned(STACK_SLOT_ENCBASE);
                int spOffset = normSpOffset << 3; // DENORMALIZE_STACK_SLOT(x) = x << 3
                uint flags = ReadBits(2);

                slots[i].IsRegister = false;
                slots[i].StackBase = spBase;
                slots[i].StackOffset = spOffset;
                slots[i].IsInterior = (flags & 1) != 0;
                slots[i].IsPinned = (flags & 2) != 0;

                if (debug)
                {
                    DebugConsole.Write("      [Stk ");
                    DebugConsole.WriteDecimal(i);
                    DebugConsole.Write("] base=");
                    DebugConsole.WriteDecimal((uint)spBase);
                    DebugConsole.Write(" off=");
                    DebugConsole.WriteDecimal((uint)spOffset);
                    DebugConsole.Write(" flags=");
                    DebugConsole.WriteDecimal(flags);
                    DebugConsole.WriteLine();
                }
                i++;

                // Subsequent stack slots
                uint stackEnd = NumRegisters + NumStackSlots;
                if (stackEnd > MAX_SLOTS) stackEnd = MAX_SLOTS;

                for (; i < stackEnd; i++)
                {
                    // ALWAYS read spBase first (2 bits) for every subsequent slot
                    spBase = (GCSlotBase)ReadBits(2);

                    if (flags != 0)
                    {
                        // Absolute encoding - read offset and flags
                        normSpOffset = DecodeVarLengthSigned(STACK_SLOT_ENCBASE);
                        spOffset = normSpOffset << 3; // DENORMALIZE_STACK_SLOT
                        flags = ReadBits(2);
                    }
                    else
                    {
                        // Delta encoding - add delta to previous offset
                        int delta = (int)DecodeVarLengthUnsigned(STACK_SLOT_DELTA_ENCBASE);
                        normSpOffset += delta;
                        spOffset = normSpOffset << 3; // DENORMALIZE_STACK_SLOT
                        // flags stays 0 (NOT read)
                    }

                    slots[i].IsRegister = false;
                    slots[i].StackBase = spBase;
                    slots[i].StackOffset = spOffset;
                    slots[i].IsInterior = (flags & 1) != 0;
                    slots[i].IsPinned = (flags & 2) != 0;

                    if (debug)
                    {
                        DebugConsole.Write("      [Stk ");
                        DebugConsole.WriteDecimal(i);
                        DebugConsole.Write("] base=");
                        DebugConsole.WriteDecimal((uint)spBase);
                        DebugConsole.Write(" off=");
                        DebugConsole.WriteDecimal((uint)spOffset);
                        DebugConsole.Write(" flags=");
                        DebugConsole.WriteDecimal(flags);
                        DebugConsole.WriteLine();
                    }
                }
            }

            // ==================== UNTRACKED SLOTS ====================
            // (We skip untracked slots for now as they're not GC roots we report)

            _numDecodedSlots = (int)i;
        }

        // Safe points, interruptible ranges, and slot counts were already decoded in DecodeSlotTable
        // The bit position is now at the liveness data

        // Remember where liveness data starts
        _livenessDataBitPosition = _bitPosition;

        uint numSafePoints = NumSafePoints;
        if (numSafePoints > MAX_SAFE_POINTS)
            numSafePoints = MAX_SAFE_POINTS;

        // Read the indirect live slot table flag
        _hasIndirectLiveSlotTable = (numSafePoints > 0) && ReadBits(1) != 0;

        if (debug)
        {
            DebugConsole.Write("      [Decode] livenessDataBitPos=");
            DebugConsole.WriteDecimal((uint)_livenessDataBitPosition);
            DebugConsole.Write(" hasIndirect=");
            DebugConsole.Write(_hasIndirectLiveSlotTable ? "Y" : "N");
            DebugConsole.WriteLine();
        }

        if (_hasIndirectLiveSlotTable)
        {
            // Read pointer table size in bits
            _liveSlotPointerBits = (int)DecodeVarLengthUnsigned(POINTER_SIZE_ENCBASE) + 1;

            if (debug)
            {
                DebugConsole.Write("      [Decode] ptrBits=");
                DebugConsole.WriteDecimal((uint)_liveSlotPointerBits);
                DebugConsole.WriteLine();
            }

            // Remember where the pointer table starts
            _pointerTableBitPosition = _bitPosition;

            // Skip the pointer table - it has numSafePoints entries of _liveSlotPointerBits bits each
            _bitPosition += (int)(numSafePoints * (uint)_liveSlotPointerBits);
        }

        // Remember where the actual live state data starts
        _liveStateDataBitPosition = _bitPosition;

        return true;
    }

    /// <summary>
    /// Get a decoded slot by index.
    /// Must call DecodeSlotDefinitionsAndSafePoints() first.
    /// </summary>
    public GCSlot GetSlot(uint index)
    {
        if (index >= (uint)_numDecodedSlots)
            return default;

        fixed (byte* slotPtr = _slotData)
        {
            var slots = (GCSlot*)slotPtr;
            return slots[index];
        }
    }

    /// <summary>
    /// Find the safe point index for a given code offset.
    /// Returns NumSafePoints if no matching safe point found.
    /// </summary>
    public uint FindSafePointIndex(uint codeOffset)
    {
        uint numSafePoints = NumSafePoints;
        if (numSafePoints > MAX_SAFE_POINTS)
            numSafePoints = MAX_SAFE_POINTS;

        fixed (uint* spPtr = _safePointOffsets)
        {
            // Binary search for the safe point
            uint left = 0;
            uint right = numSafePoints;

            while (left < right)
            {
                uint mid = (left + right) / 2;
                if (spPtr[mid] < codeOffset)
                    left = mid + 1;
                else if (spPtr[mid] > codeOffset)
                    right = mid;
                else
                    return mid; // Exact match
            }

            // If we're at a call site, the RIP points to the instruction AFTER the call.
            // The safe point is recorded at the call instruction itself.
            // So we also check if codeOffset-1 matches (for typical 5-byte CALL)
            // Actually, let's just return the closest preceding safe point
            if (left > 0)
            {
                // Check if the previous safe point is close enough
                // (within a reasonable instruction boundary)
                if (codeOffset - spPtr[left - 1] <= 15)
                    return left - 1;
            }
        }

        return NumSafePoints; // Not found
    }

    /// <summary>
    /// Check if a slot is live at a given safe point.
    /// Must call DecodeSlotDefinitionsAndSafePoints() first.
    /// The liveness data uses RLE encoding with an optional pointer table.
    /// </summary>
    public bool IsSlotLiveAtSafePoint(uint safePointIndex, uint slotIndex)
    {
        if (safePointIndex >= NumSafePoints || slotIndex >= NumTrackedSlots)
            return false;

        uint numTracked = NumTrackedSlots;
        if (numTracked > MAX_SLOTS)
            numTracked = MAX_SLOTS;

        // Find the start of the live state data for this safe point
        int liveStateOffset;
        if (_hasIndirectLiveSlotTable)
        {
            // Read offset from pointer table
            int ptrBitPos = _pointerTableBitPosition + (int)(safePointIndex * (uint)_liveSlotPointerBits);
            liveStateOffset = (int)ReadBitsAt(ptrBitPos, _liveSlotPointerBits);
        }
        else
        {
            // Direct layout - need to scan through previous safe points
            // For now, just return false for non-indirect tables (rare case)
            return false;
        }

        // Parse RLE-encoded live slots starting at offset
        int bitPos = _liveStateDataBitPosition + liveStateOffset;

        // Decode RLE: alternating skip/run sequences
        uint numSkipped = 0;
        while (numSkipped < numTracked)
        {
            // Read skip count
            uint skip = DecodeVarLengthAt(ref bitPos, LIVESTATE_RLE_SKIP_ENCBASE);
            numSkipped += skip;

            // If we've skipped past our target slot without finding it in a live run, it's not live
            if (numSkipped > slotIndex)
                return false;

            if (numSkipped >= numTracked)
                break;

            // Read run length (consecutive live slots)
            uint runLength = DecodeVarLengthAt(ref bitPos, LIVESTATE_RLE_RUN_ENCBASE) + 1;

            // Check if our target slot is in this live run
            if (slotIndex >= numSkipped && slotIndex < numSkipped + runLength)
                return true;

            numSkipped += runLength;
        }

        return false;
    }

    /// <summary>
    /// Read bits at a specific bit position without advancing the decoder state.
    /// </summary>
    private uint ReadBitsAt(int bitPos, int count)
    {
        uint result = 0;
        for (int i = 0; i < count; i++)
        {
            int byteOffset = bitPos / 8;
            int bitOffset = bitPos % 8;
            uint bit = (uint)((_start[byteOffset] >> bitOffset) & 1);
            result |= bit << i;
            bitPos++;
        }
        return result;
    }

    /// <summary>
    /// Decode a variable-length unsigned integer at a specific bit position.
    /// Updates bitPos to point past the decoded value.
    /// </summary>
    private uint DecodeVarLengthAt(ref int bitPos, int encBase)
    {
        // Read first chunk of (base + 1) bits
        uint result = ReadBitsAt(bitPos, encBase + 1);
        bitPos += encBase + 1;

        // If high bit (continuation bit) is set, decode more
        uint continuationBit = 1u << encBase;
        if ((result & continuationBit) != 0)
        {
            // XOR with continuation decoding
            result ^= DecodeVarLengthAtMore(ref bitPos, encBase);
        }

        return result;
    }

    /// <summary>
    /// Continue decoding variable-length unsigned integer.
    /// </summary>
    private uint DecodeVarLengthAtMore(ref int bitPos, int encBase)
    {
        uint numEncodings = 1u << encBase;
        uint result = numEncodings;

        for (int shift = encBase; ; shift += encBase)
        {
            uint currentChunk = ReadBitsAt(bitPos, encBase + 1);
            bitPos += encBase + 1;
            result ^= (currentChunk & (numEncodings - 1)) << shift;

            // Check continuation bit
            if ((currentChunk & numEncodings) == 0)
            {
                return result;
            }
        }
    }

    /// <summary>
    /// Enumerate all live slots at a given code offset.
    /// Calls the callback for each live slot with the slot info.
    /// Returns the number of live slots found.
    /// </summary>
    public int EnumerateLiveSlots(uint codeOffset, delegate*<GCSlot*, void*, void> callback, void* context)
    {
        uint safePointIndex = FindSafePointIndex(codeOffset);
        if (safePointIndex >= NumSafePoints)
            return 0; // No safe point found

        int liveCount = 0;
        uint numTracked = NumTrackedSlots;
        if (numTracked > MAX_SLOTS)
            numTracked = MAX_SLOTS;

        for (uint i = 0; i < numTracked; i++)
        {
            if (IsSlotLiveAtSafePoint(safePointIndex, i))
            {
                GCSlot slot = GetSlot(i);
                callback(&slot, context);
                liveCount++;
            }
        }

        return liveCount;
    }

    /// <summary>
    /// Get the safe point offset for debugging.
    /// </summary>
    public uint GetSafePointOffset(uint index)
    {
        if (index >= NumSafePoints || index >= MAX_SAFE_POINTS)
            return 0;

        fixed (uint* spPtr = _safePointOffsets)
        {
            return spPtr[index];
        }
    }

    /// <summary>
    /// Get the byte offset consumed so far.
    /// </summary>
    public int BytesRead => (int)(_ptr - _start) + (_bitPosition > 0 ? 1 : 0);

    /// <summary>
    /// Internal slot reading with proper flags-based encoding selection.
    /// The flags from the previous slot determine if we use delta or absolute encoding.
    /// When prevFlags != 0, use absolute encoding AND read new flags.
    /// When prevFlags == 0, use delta encoding and DO NOT read flags (they stay 0).
    /// For the first slot (slotIndex == 0), always use absolute encoding.
    /// </summary>
    private GCSlot ReadSlotInternal(uint slotIndex, ref int prevRegister, ref int prevStackOffset, ref uint prevFlags)
    {
        GCSlot slot = default;

        if (slotIndex < NumRegisters)
        {
            // Register slot
            slot.IsRegister = true;

            if (slotIndex == 0)
            {
                // First register always uses absolute encoding + read flags
                slot.RegisterNumber = (byte)DecodeVarLengthUnsigned(REGISTER_ENCBASE);
                prevRegister = slot.RegisterNumber;

                // Read flags (2 bits): bit 0 = interior, bit 1 = pinned
                uint flags = ReadBits(2);
                slot.IsInterior = (flags & 1) != 0;
                slot.IsPinned = (flags & 2) != 0;
                prevFlags = flags;
            }
            else if (prevFlags != 0)
            {
                // Previous slot had flags set: use absolute encoding + read new flags
                slot.RegisterNumber = (byte)DecodeVarLengthUnsigned(REGISTER_ENCBASE);
                prevRegister = slot.RegisterNumber;

                // Read flags (2 bits)
                uint flags = ReadBits(2);
                slot.IsInterior = (flags & 1) != 0;
                slot.IsPinned = (flags & 2) != 0;
                prevFlags = flags;
            }
            else
            {
                // Previous slot had flags=0: use delta encoding, NO new flags read
                uint delta = DecodeVarLengthUnsigned(REGISTER_DELTA_ENCBASE) + 1;
                slot.RegisterNumber = (byte)(prevRegister + delta);
                prevRegister = slot.RegisterNumber;

                // Flags stay 0 (inherited from previous)
                slot.IsInterior = false;
                slot.IsPinned = false;
                // prevFlags stays 0
            }
        }
        else
        {
            // Stack slot
            slot.IsRegister = false;
            uint stackSlotIndex = slotIndex - NumRegisters;

            if (stackSlotIndex == 0)
            {
                // First stack slot always uses absolute encoding + read flags
                slot.StackBase = (GCSlotBase)ReadBits(2);
                slot.StackOffset = (int)DecodeVarLengthUnsigned(STACK_SLOT_ENCBASE);
                prevStackOffset = slot.StackOffset;

                // Read flags (2 bits)
                uint flags = ReadBits(2);
                slot.IsInterior = (flags & 1) != 0;
                slot.IsPinned = (flags & 2) != 0;
                prevFlags = flags;
            }
            else if (prevFlags != 0)
            {
                // Previous slot had flags set: use absolute encoding + read new flags
                slot.StackBase = (GCSlotBase)ReadBits(2);
                slot.StackOffset = (int)DecodeVarLengthUnsigned(STACK_SLOT_ENCBASE);
                prevStackOffset = slot.StackOffset;

                // Read flags (2 bits)
                uint flags = ReadBits(2);
                slot.IsInterior = (flags & 1) != 0;
                slot.IsPinned = (flags & 2) != 0;
                prevFlags = flags;
            }
            else
            {
                // Previous slot had flags=0: use delta encoding, NO new flags read
                // But spBase is ALWAYS read for every stack slot!
                slot.StackBase = (GCSlotBase)ReadBits(2);
                uint delta = DecodeVarLengthUnsigned(STACK_SLOT_DELTA_ENCBASE);
                slot.StackOffset = prevStackOffset + (int)delta;
                prevStackOffset = slot.StackOffset;

                // Flags stay 0 (inherited from previous)
                slot.IsInterior = false;
                slot.IsPinned = false;
                // prevFlags stays 0
            }
        }

        return slot;
    }

    /// <summary>
    /// Compute ceiling of log2(n).
    /// </summary>
    private static int CeilLog2(uint n)
    {
        if (n == 0) return 0;
        int result = 0;
        n--;
        while (n > 0)
        {
            result++;
            n >>= 1;
        }
        return result;
    }

    // ==================== Bit Reading Utilities ====================

    /// <summary>
    /// Read N bits from the stream.
    /// </summary>
    private uint ReadBits(int count)
    {
        uint result = 0;
        for (int i = 0; i < count; i++)
        {
            int byteOffset = _bitPosition / 8;
            int bitOffset = _bitPosition % 8;
            uint bit = (uint)((_ptr[byteOffset] >> bitOffset) & 1);
            result |= bit << i;
            _bitPosition++;
        }
        return result;
    }

    /// <summary>
    /// Decode a variable-length unsigned integer with given base.
    /// Uses .NET runtime's exact encoding from BitStreamReader::DecodeVarLengthUnsigned.
    /// </summary>
    private uint DecodeVarLengthUnsigned(int encBase)
    {
        // Read first chunk of (base + 1) bits
        uint result = ReadBits(encBase + 1);

        // If high bit (continuation bit) is set, decode more
        uint continuationBit = 1u << encBase;
        if ((result & continuationBit) != 0)
        {
            // XOR with continuation decoding (matches DecodeVarLengthUnsignedMore)
            result ^= DecodeVarLengthUnsignedMore(encBase);
        }

        return result;
    }

    /// <summary>
    /// Continue decoding variable-length unsigned integer.
    /// Matches .NET runtime's BitStreamReader::DecodeVarLengthUnsignedMore.
    /// </summary>
    private uint DecodeVarLengthUnsignedMore(int encBase)
    {
        uint numEncodings = 1u << encBase;
        uint result = numEncodings;

        for (int shift = encBase; ; shift += encBase)
        {
            uint currentChunk = ReadBits(encBase + 1);
            result ^= (currentChunk & (numEncodings - 1)) << shift;

            // Check continuation bit
            if ((currentChunk & numEncodings) == 0)
            {
                return result;
            }
        }
    }

    /// <summary>
    /// Decode a variable-length signed integer.
    /// </summary>
    private int DecodeVarLengthSigned(int encBase)
    {
        uint unsignedValue = DecodeVarLengthUnsigned(encBase);
        // ZigZag decoding: 0->0, 1->-1, 2->1, 3->-2, 4->2, ...
        return (int)((unsignedValue >> 1) ^ (uint)(-(int)(unsignedValue & 1)));
    }
}

/// <summary>
/// Helper to get GCInfo pointer for a function.
/// </summary>
public static unsafe class GCInfoHelper
{
    // Unwind block flags (from NativeAOT runtime)
    private const byte UBF_FUNC_KIND_MASK = 0x03;
    private const byte UBF_FUNC_HAS_EHINFO = 0x04;
    private const byte UBF_FUNC_HAS_ASSOCIATED_DATA = 0x10;

    // Function kinds (bits 0-1 of unwindBlockFlags)
    private const byte UBF_FUNC_KIND_ROOT = 0;      // Regular function
    private const byte UBF_FUNC_KIND_HANDLER = 1;   // Exception handler funclet
    private const byte UBF_FUNC_KIND_FILTER = 2;    // Filter funclet

    /// <summary>
    /// Get the GCInfo pointer for a function given its UNWIND_INFO.
    /// GCInfo follows the NativeAOT EH info in .xdata.
    /// </summary>
    /// <param name="imageBase">Base address of the PE image</param>
    /// <param name="unwindInfo">Pointer to the function's UNWIND_INFO</param>
    /// <param name="funcKindOut">Optional output for function kind (0=root, 1=handler, 2=filter)</param>
    /// <returns>Pointer to GCInfo, or null if not found or if this is a funclet/chained</returns>
    public static byte* GetGCInfo(ulong imageBase, UnwindInfo* unwindInfo, byte* funcKindOut = null)
    {
        if (unwindInfo == null)
            return null;

        // Check for chained unwind info - these don't have their own GCInfo
        // The chain points to the parent function which contains the GCInfo
        if ((unwindInfo->Flags & UnwindFlags.UNW_FLAG_CHAININFO) != 0)
        {
            // Chained unwind info - no GCInfo here, follow chain to parent
            // For stack root enumeration we skip this as the parent's GCInfo covers all slots
            if (funcKindOut != null)
                *funcKindOut = 0xFF; // Special value indicating chained
            return null;
        }

        // Calculate offset to end of UNWIND_INFO structure
        // Layout: header (4 bytes) + CountOfUnwindCodes * 2 (NO rounding for NativeAOT)
        int headerSize = 4;
        int unwindArrayBytes = unwindInfo->CountOfUnwindCodes * 2;
        int unwindInfoSize = headerSize + unwindArrayBytes;

        // If exception handler flags are set, need to DWORD align and add handler RVA
        if ((unwindInfo->Flags & (UnwindFlags.UNW_FLAG_EHANDLER | UnwindFlags.UNW_FLAG_UHANDLER)) != 0)
        {
            unwindInfoSize = (unwindInfoSize + 3) & ~3;
            unwindInfoSize += 4;  // Exception handler RVA
        }

        // After UNWIND_INFO comes the NativeAOT unwind block flags byte
        byte* p = (byte*)unwindInfo + unwindInfoSize;
        byte unwindBlockFlags = *p++;

        // Check if this is a funclet (handler or filter)
        byte funcKind = (byte)(unwindBlockFlags & UBF_FUNC_KIND_MASK);
        if (funcKindOut != null)
            *funcKindOut = funcKind;

        if (funcKind != UBF_FUNC_KIND_ROOT)
        {
            // Funclets don't have their own GCInfo - they reference the parent function's GCInfo.
            // For stack root enumeration, we skip funclets and only enumerate roots from
            // the parent function's frame (which will be encountered later in the stack walk).
            return null;
        }

        // Skip associated data RVA if present
        if ((unwindBlockFlags & UBF_FUNC_HAS_ASSOCIATED_DATA) != 0)
        {
            p += 4;
        }

        // Skip EH info RVA if present
        if ((unwindBlockFlags & UBF_FUNC_HAS_EHINFO) != 0)
        {
            p += 4;
        }

        // GCInfo starts here
        return p;
    }

    /// <summary>
    /// Decode and dump GCInfo for debugging.
    /// </summary>
    public static void DumpGCInfo(byte* gcInfo)
    {
        if (gcInfo == null)
        {
            DebugConsole.WriteLine("[GCInfo] null");
            return;
        }

        var decoder = new GCInfoDecoder(gcInfo);
        if (!decoder.DecodeHeader())
        {
            DebugConsole.WriteLine("[GCInfo] Failed to decode header");
            return;
        }

        DebugConsole.Write("[GCInfo] CodeLen=");
        DebugConsole.WriteDecimal(decoder.CodeLength);
        DebugConsole.Write(" SafePts=");
        DebugConsole.WriteDecimal(decoder.NumSafePoints);
        DebugConsole.Write(" IntRanges=");
        DebugConsole.WriteDecimal(decoder.NumInterruptibleRanges);

        if (decoder.HasStackBaseRegister)
        {
            DebugConsole.Write(" StackBase=R");
            DebugConsole.WriteDecimal((uint)decoder.StackBaseRegister);
        }

        DebugConsole.WriteLine();

        // Decode slot table
        if (!decoder.DecodeSlotTable())
        {
            DebugConsole.WriteLine("[GCInfo] Failed to decode slot table");
            return;
        }

        DebugConsole.Write("[GCInfo] Regs=");
        DebugConsole.WriteDecimal(decoder.NumRegisters);
        DebugConsole.Write(" StackSlots=");
        DebugConsole.WriteDecimal(decoder.NumStackSlots);
        DebugConsole.Write(" Untracked=");
        DebugConsole.WriteDecimal(decoder.NumUntrackedSlots);
        DebugConsole.Write(" (");
        DebugConsole.WriteDecimal((uint)decoder.BytesRead);
        DebugConsole.WriteLine(" bytes)");
    }

    /// <summary>
    /// Dump GCInfo for a sample of functions from the PE's .pdata section.
    /// </summary>
    public static void DumpSamples(ulong imageBase)
    {
        // Get .pdata from PE exception directory
        var dosHeader = (ImageDosHeader*)imageBase;
        if (dosHeader->e_magic != 0x5A4D) return;

        var ntHeaders = (ImageNtHeaders64*)(imageBase + (uint)dosHeader->e_lfanew);
        if (ntHeaders->Signature != 0x00004550) return;

        var exceptionDir = &ntHeaders->OptionalHeader.ExceptionTable;
        if (exceptionDir->VirtualAddress == 0 || exceptionDir->Size == 0)
        {
            DebugConsole.WriteLine("[GCInfo] No .pdata section");
            return;
        }

        uint entryCount = exceptionDir->Size / 12; // sizeof(RUNTIME_FUNCTION) = 12
        var functions = (RuntimeFunction*)(imageBase + exceptionDir->VirtualAddress);

        DebugConsole.Write("[GCInfo] Validating all ");
        DebugConsole.WriteDecimal(entryCount);
        DebugConsole.WriteLine(" functions...");

        uint successCount = 0;
        uint headerFailCount = 0;
        uint slotFailCount = 0;
        uint sanityFailCount = 0;
        uint noGcInfoCount = 0;
        int firstFailIndex = -1;
        uint firstFailReason = 0; // 1=header, 2=slot, 3=sanity

        // Track totals for sanity
        uint totalSafePoints = 0;
        uint totalSlots = 0;
        uint maxCodeLen = 0;
        uint maxSafePoints = 0;
        uint maxSlots = 0;

        for (int i = 0; i < (int)entryCount; i++)
        {
            var func = &functions[i];
            var unwindInfo = (UnwindInfo*)(imageBase + func->UnwindInfoAddress);
            uint funcSize = func->EndAddress - func->BeginAddress;

            // Get GCInfo
            byte* gcInfo = GetGCInfo(imageBase, unwindInfo);
            if (gcInfo != null)
            {
                var decoder = new GCInfoDecoder(gcInfo);
                if (decoder.DecodeHeader())
                {
                    if (decoder.DecodeSlotTable())
                    {
                        // Sanity checks
                        bool sane = true;

                        // CodeLength should be >= function size (can be larger due to funclets)
                        // But shouldn't be absurdly larger (10x would be suspicious)
                        if (decoder.CodeLength < funcSize)
                        {
                            sane = false; // CodeLength smaller than function - wrong
                        }
                        if (decoder.CodeLength > funcSize * 10 && funcSize > 10)
                        {
                            sane = false; // Way too big
                        }

                        // Safe points shouldn't exceed code length (one per byte would be insane)
                        if (decoder.NumSafePoints > decoder.CodeLength)
                        {
                            sane = false;
                        }

                        // Tracked slots should be reasonable (< 1000)
                        if (decoder.NumTrackedSlots > 1000)
                        {
                            sane = false;
                        }

                        if (sane)
                        {
                            successCount++;
                            totalSafePoints += decoder.NumSafePoints;
                            totalSlots += decoder.NumTrackedSlots;
                            if (decoder.CodeLength > maxCodeLen) maxCodeLen = decoder.CodeLength;
                            if (decoder.NumSafePoints > maxSafePoints) maxSafePoints = decoder.NumSafePoints;
                            if (decoder.NumTrackedSlots > maxSlots) maxSlots = decoder.NumTrackedSlots;
                        }
                        else
                        {
                            sanityFailCount++;
                            if (firstFailIndex < 0) { firstFailIndex = i; firstFailReason = 3; }
                        }
                    }
                    else
                    {
                        slotFailCount++;
                        if (firstFailIndex < 0) { firstFailIndex = i; firstFailReason = 2; }
                    }
                }
                else
                {
                    headerFailCount++;
                    if (firstFailIndex < 0) { firstFailIndex = i; firstFailReason = 1; }
                }
            }
            else
            {
                noGcInfoCount++;
            }
        }

        DebugConsole.Write("[GCInfo] Results: ");
        DebugConsole.WriteDecimal(successCount);
        DebugConsole.Write(" OK, ");
        DebugConsole.WriteDecimal(noGcInfoCount);
        DebugConsole.Write(" no-gcinfo, ");
        DebugConsole.WriteDecimal(headerFailCount);
        DebugConsole.Write(" hdr-fail, ");
        DebugConsole.WriteDecimal(slotFailCount);
        DebugConsole.Write(" slot-fail, ");
        DebugConsole.WriteDecimal(sanityFailCount);
        DebugConsole.WriteLine(" sanity-fail");

        // Print stats
        DebugConsole.Write("[GCInfo] Stats: totalSP=");
        DebugConsole.WriteDecimal(totalSafePoints);
        DebugConsole.Write(" totalSlots=");
        DebugConsole.WriteDecimal(totalSlots);
        DebugConsole.Write(" maxCode=");
        DebugConsole.WriteDecimal(maxCodeLen);
        DebugConsole.Write(" maxSP=");
        DebugConsole.WriteDecimal(maxSafePoints);
        DebugConsole.Write(" maxSlots=");
        DebugConsole.WriteDecimal(maxSlots);
        DebugConsole.WriteLine();

        // If there were failures, dump details of the first one
        if (firstFailIndex >= 0)
        {
            var func = &functions[firstFailIndex];
            var unwindInfo = (UnwindInfo*)(imageBase + func->UnwindInfoAddress);
            uint funcSize = func->EndAddress - func->BeginAddress;

            DebugConsole.Write("[GCInfo] First failure (reason=");
            DebugConsole.WriteDecimal(firstFailReason);
            DebugConsole.Write(") at [");
            DebugConsole.WriteDecimal((uint)firstFailIndex);
            DebugConsole.Write("] RVA=0x");
            DebugConsole.WriteHex(func->BeginAddress);
            DebugConsole.Write("-0x");
            DebugConsole.WriteHex(func->EndAddress);
            DebugConsole.Write(" size=");
            DebugConsole.WriteDecimal(funcSize);
            DebugConsole.WriteLine();

            // Check unwind block flags to see if this is a funclet
            int headerSize = 4;
            int unwindArrayBytes = unwindInfo->CountOfUnwindCodes * 2;
            int unwindInfoSize = headerSize + unwindArrayBytes;
            if ((unwindInfo->Flags & (UnwindFlags.UNW_FLAG_EHANDLER | UnwindFlags.UNW_FLAG_UHANDLER)) != 0)
            {
                unwindInfoSize = (unwindInfoSize + 3) & ~3;
                unwindInfoSize += 4;
            }
            byte* flagsPtr = (byte*)unwindInfo + unwindInfoSize;
            byte unwindBlockFlags = *flagsPtr;
            byte funcKind = (byte)(unwindBlockFlags & 0x03);

            DebugConsole.Write("[GCInfo] unwindBlockFlags=0x");
            DebugConsole.WriteHex(unwindBlockFlags);
            DebugConsole.Write(" funcKind=");
            DebugConsole.WriteDecimal(funcKind);
            if (funcKind == 0) DebugConsole.Write(" (root)");
            else if (funcKind == 1) DebugConsole.Write(" (HANDLER)");
            else if (funcKind == 2) DebugConsole.Write(" (FILTER)");
            DebugConsole.WriteLine();

            // Dump raw bytes for debugging
            byte* gcInfo = GetGCInfo(imageBase, unwindInfo);
            if (gcInfo != null)
            {
                DebugConsole.Write("[GCInfo] Raw bytes: ");
                for (int b = 0; b < 16; b++)
                {
                    DebugConsole.WriteHex(gcInfo[b]);
                    DebugConsole.Write(" ");
                }
                DebugConsole.WriteLine();

                // Try to decode and show what we got
                var decoder = new GCInfoDecoder(gcInfo);
                decoder.DecodeHeader();
                decoder.DecodeSlotTable();
                DebugConsole.Write("[GCInfo] Decoded: Fat=");
                DebugConsole.WriteDecimal(decoder.IsFatHeader ? 1u : 0u);
                DebugConsole.Write(" Code=");
                DebugConsole.WriteDecimal(decoder.CodeLength);
                DebugConsole.Write(" SP=");
                DebugConsole.WriteDecimal(decoder.NumSafePoints);
                DebugConsole.Write(" IR=");
                DebugConsole.WriteDecimal(decoder.NumInterruptibleRanges);
                DebugConsole.Write(" Slots=");
                DebugConsole.WriteDecimal(decoder.NumTrackedSlots);
                DebugConsole.WriteLine();
            }
        }

        if (successCount == entryCount - noGcInfoCount && sanityFailCount == 0 && headerFailCount == 0 && slotFailCount == 0)
        {
            DebugConsole.WriteLine("[GCInfo] All functions validated successfully!");
        }
    }

    /// <summary>
    /// Comprehensive validation of GCInfo decoding including slot definitions and liveness data.
    /// </summary>
    public static void ValidateComprehensive(ulong imageBase)
    {
        var dosHeader = (ImageDosHeader*)imageBase;
        if (dosHeader->e_magic != 0x5A4D) return;

        var ntHeaders = (ImageNtHeaders64*)(imageBase + (uint)dosHeader->e_lfanew);
        if (ntHeaders->Signature != 0x00004550) return;

        var exceptionDir = &ntHeaders->OptionalHeader.ExceptionTable;
        if (exceptionDir->VirtualAddress == 0 || exceptionDir->Size == 0) return;

        uint entryCount = exceptionDir->Size / 12;
        var functions = (RuntimeFunction*)(imageBase + exceptionDir->VirtualAddress);

        DebugConsole.Write("[GCInfo] Comprehensive validation of ");
        DebugConsole.WriteDecimal(entryCount);
        DebugConsole.WriteLine(" functions...");

        uint successCount = 0;
        uint headerFailCount = 0;
        uint slotTableFailCount = 0;
        uint slotDefFailCount = 0;
        uint regRangeFailCount = 0;
        uint stackBaseFailCount = 0;
        uint safePointFailCount = 0;
        uint livenessFailCount = 0;
        uint noGcInfoCount = 0;

        // Detailed tracking
        uint totalRegs = 0;
        uint totalStackSlots = 0;
        uint totalUntracked = 0;
        uint totalSafePoints = 0;
        uint functionsWithLiveness = 0;
        uint functionsWithIndirectTable = 0;

        for (int i = 0; i < (int)entryCount; i++)
        {
            var func = &functions[i];
            var unwindInfo = (UnwindInfo*)(imageBase + func->UnwindInfoAddress);
            uint funcSize = func->EndAddress - func->BeginAddress;

            byte* gcInfo = GetGCInfo(imageBase, unwindInfo);
            if (gcInfo == null)
            {
                noGcInfoCount++;
                continue;
            }

            var decoder = new GCInfoDecoder(gcInfo);

            // Step 1: Decode header
            if (!decoder.DecodeHeader())
            {
                headerFailCount++;
                continue;
            }

            // Check funclet - they use parent's GCInfo
            int headerSize = 4 + unwindInfo->CountOfUnwindCodes * 2;
            if ((unwindInfo->Flags & (UnwindFlags.UNW_FLAG_EHANDLER | UnwindFlags.UNW_FLAG_UHANDLER)) != 0)
                headerSize = ((headerSize + 3) & ~3) + 4;
            byte unwindBlockFlags = *((byte*)unwindInfo + headerSize);
            byte funcKind = (byte)(unwindBlockFlags & 0x03);

            // Skip funclets - they share GCInfo with parent and have different code lengths
            if (funcKind != 0)
            {
                noGcInfoCount++; // Count as no-gcinfo since we skip them
                continue;
            }

            // Sanity check code length
            if (decoder.CodeLength < funcSize || decoder.CodeLength > funcSize * 10)
            {
                headerFailCount++;
                continue;
            }

            // Step 2: Decode slot table (includes safe points and slot counts)
            if (!decoder.DecodeSlotTable())
            {
                slotTableFailCount++;
                continue;
            }

            // Sanity check slot counts - x64 has 16 registers max
            // NumRegisters > 16 means we're definitely reading garbage
            bool slotCountsInvalid = decoder.NumRegisters > 16 || decoder.NumStackSlots > 256 ||
                decoder.NumTrackedSlots > 300 || decoder.NumSafePoints > decoder.CodeLength;

            if (slotCountsInvalid)
            {
                slotTableFailCount++;
                if (slotTableFailCount <= 3)
                {
                    DebugConsole.Write("[GCInfo] slot-table fail: func[");
                    DebugConsole.WriteDecimal((uint)i);
                    DebugConsole.Write("] RVA=0x");
                    DebugConsole.WriteHex(func->BeginAddress);
                    DebugConsole.Write(" codeLen=");
                    DebugConsole.WriteDecimal(decoder.CodeLength);
                    DebugConsole.Write(" SP=");
                    DebugConsole.WriteDecimal(decoder.NumSafePoints);
                    DebugConsole.Write(" IR=");
                    DebugConsole.WriteDecimal(decoder.NumInterruptibleRanges);
                    DebugConsole.Write(" regs=");
                    DebugConsole.WriteDecimal(decoder.NumRegisters);
                    DebugConsole.Write(" stk=");
                    DebugConsole.WriteDecimal(decoder.NumStackSlots);
                    DebugConsole.Write(" fat=");
                    DebugConsole.Write(decoder.IsFatHeader ? "Y" : "N");
                    DebugConsole.WriteLine();

                    // Debug re-decode this function
                    DebugConsole.WriteLine("      [Re-decoding with debug...]");
                    var decoder2 = new GCInfoDecoder(gcInfo);
                    decoder2.DecodeHeader(true);
                    decoder2.DecodeSlotTable(true);
                }
                continue;
            }

            // Step 3: Decode slot definitions
            if (!decoder.DecodeSlotDefinitionsAndSafePoints(false))
            {
                slotDefFailCount++;
                continue;
            }

            // Step 4: Validate all decoded slots
            bool slotsValid = true;
            for (uint s = 0; s < decoder.NumTrackedSlots && s < 64; s++)
            {
                var slot = decoder.GetSlot(s);
                if (slot.IsRegister)
                {
                    // Register must be 0-15 for x64
                    if (slot.RegisterNumber > 15)
                    {
                        slotsValid = false;
                        regRangeFailCount++;
                        if (regRangeFailCount == 1)
                        {
                            DebugConsole.Write("[GCInfo] FIRST reg fail: func[");
                            DebugConsole.WriteDecimal((uint)i);
                            DebugConsole.Write("] RVA=0x");
                            DebugConsole.WriteHex(func->BeginAddress);
                            DebugConsole.Write(" slot=");
                            DebugConsole.WriteDecimal(s);
                            DebugConsole.Write(" reg=");
                            DebugConsole.WriteDecimal(slot.RegisterNumber);
                            DebugConsole.WriteLine();
                        }
                        break;
                    }
                }
                else
                {
                    // Stack base must be valid (0-2)
                    if ((int)slot.StackBase > 2)
                    {
                        slotsValid = false;
                        stackBaseFailCount++;
                        if (stackBaseFailCount == 1)
                        {
                            DebugConsole.Write("[GCInfo] FIRST stkBase fail: func[");
                            DebugConsole.WriteDecimal((uint)i);
                            DebugConsole.Write("] RVA=0x");
                            DebugConsole.WriteHex(func->BeginAddress);
                            DebugConsole.Write(" slot=");
                            DebugConsole.WriteDecimal(s);
                            DebugConsole.Write(" base=");
                            DebugConsole.WriteDecimal((uint)slot.StackBase);
                            DebugConsole.WriteLine();
                        }
                        break;
                    }
                }
            }

            if (!slotsValid)
                continue;

            // Step 5: Validate safe point offsets are within code range
            bool safePointsValid = true;
            for (uint sp = 0; sp < decoder.NumSafePoints && sp < 256; sp++)
            {
                uint offset = decoder.GetSafePointOffset(sp);
                if (offset >= decoder.CodeLength)
                {
                    safePointsValid = false;
                    safePointFailCount++;
                    if (safePointFailCount == 1)
                    {
                        DebugConsole.Write("[GCInfo] FIRST sp fail: func[");
                        DebugConsole.WriteDecimal((uint)i);
                        DebugConsole.Write("] RVA=0x");
                        DebugConsole.WriteHex(func->BeginAddress);
                        DebugConsole.Write(" sp[");
                        DebugConsole.WriteDecimal(sp);
                        DebugConsole.Write("]=");
                        DebugConsole.WriteDecimal(offset);
                        DebugConsole.Write(" >= codeLen=");
                        DebugConsole.WriteDecimal(decoder.CodeLength);
                        DebugConsole.WriteLine();
                    }
                    break;
                }
            }

            if (!safePointsValid)
                continue;

            // Step 6: Test liveness queries (if there are safe points and slots)
            bool livenessValid = true;
            if (decoder.NumSafePoints > 0 && decoder.NumTrackedSlots > 0)
            {
                functionsWithLiveness++;

                // Try querying liveness at each safe point
                for (uint sp = 0; sp < decoder.NumSafePoints && sp < 10; sp++)
                {
                    // Query liveness for first few slots
                    for (uint sl = 0; sl < decoder.NumTrackedSlots && sl < 5; sl++)
                    {
                        // This should not crash - just checking it returns a valid bool
                        bool live = decoder.IsSlotLiveAtSafePoint(sp, sl);
                        // live can be true or false, both are valid
                        _ = live;
                    }
                }
            }

            // Success!
            successCount++;
            totalRegs += decoder.NumRegisters;
            totalStackSlots += decoder.NumStackSlots;
            totalUntracked += decoder.NumUntrackedSlots;
            totalSafePoints += decoder.NumSafePoints;
        }

        // Print results
        DebugConsole.WriteLine("[GCInfo] === Comprehensive Validation Results ===");

        DebugConsole.Write("[GCInfo] Success: ");
        DebugConsole.WriteDecimal(successCount);
        DebugConsole.Write("/");
        DebugConsole.WriteDecimal(entryCount - noGcInfoCount);
        DebugConsole.Write(" (");
        DebugConsole.WriteDecimal(noGcInfoCount);
        DebugConsole.WriteLine(" no-gcinfo)");

        if (headerFailCount > 0 || slotTableFailCount > 0 || slotDefFailCount > 0)
        {
            DebugConsole.Write("[GCInfo] Decode failures: hdr=");
            DebugConsole.WriteDecimal(headerFailCount);
            DebugConsole.Write(" slotTbl=");
            DebugConsole.WriteDecimal(slotTableFailCount);
            DebugConsole.Write(" slotDef=");
            DebugConsole.WriteDecimal(slotDefFailCount);
            DebugConsole.WriteLine();
        }

        if (regRangeFailCount > 0 || stackBaseFailCount > 0 || safePointFailCount > 0 || livenessFailCount > 0)
        {
            DebugConsole.Write("[GCInfo] Validation failures: reg=");
            DebugConsole.WriteDecimal(regRangeFailCount);
            DebugConsole.Write(" stkBase=");
            DebugConsole.WriteDecimal(stackBaseFailCount);
            DebugConsole.Write(" sp=");
            DebugConsole.WriteDecimal(safePointFailCount);
            DebugConsole.Write(" liveness=");
            DebugConsole.WriteDecimal(livenessFailCount);
            DebugConsole.WriteLine();
        }

        DebugConsole.Write("[GCInfo] Totals: regs=");
        DebugConsole.WriteDecimal(totalRegs);
        DebugConsole.Write(" stk=");
        DebugConsole.WriteDecimal(totalStackSlots);
        DebugConsole.Write(" untrk=");
        DebugConsole.WriteDecimal(totalUntracked);
        DebugConsole.Write(" safePoints=");
        DebugConsole.WriteDecimal(totalSafePoints);
        DebugConsole.WriteLine();

        DebugConsole.Write("[GCInfo] Functions with liveness data: ");
        DebugConsole.WriteDecimal(functionsWithLiveness);
        DebugConsole.WriteLine();

        uint totalFailures = headerFailCount + slotTableFailCount + slotDefFailCount +
                            regRangeFailCount + stackBaseFailCount + safePointFailCount + livenessFailCount;

        if (totalFailures == 0)
        {
            DebugConsole.WriteLine("[GCInfo] === ALL VALIDATION PASSED ===");
        }
        else
        {
            DebugConsole.Write("[GCInfo] === ");
            DebugConsole.WriteDecimal(totalFailures);
            DebugConsole.WriteLine(" TOTAL FAILURES ===");
        }
    }
}
