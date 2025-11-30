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
    }

    /// <summary>
    /// Decode the GCInfo header.
    /// </summary>
    public bool DecodeHeader()
    {
        if (_ptr == null) return false;

        // First bit indicates slim (0) or fat (1) header
        IsFatHeader = ReadBits(1) != 0;

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

        // Code length
        CodeLength = DecodeVarLengthUnsigned(CODE_LENGTH_ENCBASE);

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

        // Number of safe points
        NumSafePoints = DecodeVarLengthUnsigned(NUM_SAFE_POINTS_ENCBASE);

        // Number of interruptible ranges
        NumInterruptibleRanges = DecodeVarLengthUnsigned(NUM_INTERRUPTIBLE_RANGES_ENCBASE);

        return true;
    }

    /// <summary>
    /// Decode the slot table (registers and stack slots that can hold GC refs).
    /// Call after DecodeHeader().
    /// Each count is preceded by a presence bit - if 0, count is 0.
    /// </summary>
    public bool DecodeSlotTable()
    {
        // Number of register slots (preceded by presence bit)
        if (ReadBits(1) != 0)
        {
            NumRegisters = DecodeVarLengthUnsigned(NUM_REGISTERS_ENCBASE);
        }
        else
        {
            NumRegisters = 0;
        }

        // Number of stack slots (preceded by presence bit)
        if (ReadBits(1) != 0)
        {
            NumStackSlots = DecodeVarLengthUnsigned(NUM_STACK_SLOTS_ENCBASE);
        }
        else
        {
            NumStackSlots = 0;
        }

        // Number of untracked slots (preceded by presence bit)
        if (ReadBits(1) != 0)
        {
            NumUntrackedSlots = DecodeVarLengthUnsigned(NUM_UNTRACKED_SLOTS_ENCBASE);
        }
        else
        {
            NumUntrackedSlots = 0;
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
    /// Check if a slot is live at a given safe point.
    /// Call after decoding slots. Returns the liveness bit for the slot.
    /// </summary>
    public bool IsSlotLiveAtSafePoint(uint safePointIndex, uint slotIndex)
    {
        // This is a simplified version - full implementation needs to track
        // bit position after slot table and read the liveness matrix.
        // For now, return false (will be implemented properly later).
        return false;
    }

    /// <summary>
    /// Get the byte offset consumed so far.
    /// </summary>
    public int BytesRead => (int)(_ptr - _start) + (_bitPosition > 0 ? 1 : 0);

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

    /// <summary>
    /// Get the GCInfo pointer for a function given its UNWIND_INFO.
    /// GCInfo follows the NativeAOT EH info in .xdata.
    /// </summary>
    /// <param name="imageBase">Base address of the PE image</param>
    /// <param name="unwindInfo">Pointer to the function's UNWIND_INFO</param>
    /// <returns>Pointer to GCInfo, or null if not found</returns>
    public static byte* GetGCInfo(ulong imageBase, UnwindInfo* unwindInfo)
    {
        if (unwindInfo == null)
            return null;

        // Calculate offset to end of UNWIND_INFO structure
        // Layout: header (4 bytes) + CountOfUnwindCodes * 2 (NO rounding)
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
}
