// ProtonOS kernel - JIT GCInfo Builder
// Generates NativeAOT-compatible GCInfo for JIT-compiled methods.
//
// GCInfo describes which stack slots and registers contain live GC references
// at each safe point (call sites). This allows the GC to enumerate stack roots.
//
// The format must match what GCInfoDecoder expects to parse.
//
// Reference: src/kernel/Runtime/GCInfo.cs for the decoder.

using System;
using System.Runtime.InteropServices;
using ProtonOS.Platform;

namespace ProtonOS.Runtime.JIT;

/// <summary>
/// Tracks a stack slot that contains a GC reference.
/// </summary>
public struct JITGCSlot
{
    /// <summary>Stack offset from RBP (negative for locals).</summary>
    public int StackOffset;

    /// <summary>If true, this is an interior pointer (points within an object).</summary>
    public bool IsInterior;

    /// <summary>If true, this pointer is pinned (cannot be relocated).</summary>
    public bool IsPinned;
}

/// <summary>
/// Builds GCInfo for a JIT-compiled method.
///
/// Usage:
/// 1. Create JITGCInfo
/// 2. Call AddStackSlot() for each local/arg that holds GC references
/// 3. Call AddSafePoint() after each CALL instruction
/// 4. Call BuildGCInfo() to encode the data
/// </summary>
public unsafe struct JITGCInfo
{
    // Maximum slots and safe points we track
    private const int MaxSlots = 32;
    private const int MaxSafePoints = 64;

    // Fixed arrays for slots and safe points
    private fixed byte _slotData[MaxSlots * 8]; // JITGCSlot is 8 bytes
    private fixed uint _safePointOffsets[MaxSafePoints];

    private int _numSlots;
    private int _numSafePoints;
    private uint _codeLength;
    private bool _hasStackBaseRegister;
    private int _stackBaseRegister; // Usually 5 = RBP

    // GCInfo encoding constants (must match GCInfoDecoder)
    private const int CODE_LENGTH_ENCBASE = 8;
    private const int NORM_PROLOG_SIZE_ENCBASE = 5;
    private const int NUM_SAFE_POINTS_ENCBASE = 2;
    private const int NUM_INTERRUPTIBLE_RANGES_ENCBASE = 1;
    private const int NUM_REGISTERS_ENCBASE = 2;
    private const int NUM_STACK_SLOTS_ENCBASE = 2;
    private const int NUM_UNTRACKED_SLOTS_ENCBASE = 1;
    private const int STACK_SLOT_ENCBASE = 6;
    private const int STACK_SLOT_DELTA_ENCBASE = 4;
    private const int STACK_BASE_REGISTER_ENCBASE = 3;
    private const int POINTER_SIZE_ENCBASE = 3;

    /// <summary>
    /// Initialize the GCInfo builder.
    /// </summary>
    /// <param name="codeLength">Total length of generated code.</param>
    /// <param name="hasFramePointer">True if RBP is used as frame pointer.</param>
    public void Init(uint codeLength, bool hasFramePointer = true)
    {
        _numSlots = 0;
        _numSafePoints = 0;
        _codeLength = codeLength;
        _hasStackBaseRegister = hasFramePointer;
        _stackBaseRegister = 5; // RBP
    }

    /// <summary>
    /// Add a stack slot containing a GC reference.
    /// </summary>
    /// <param name="stackOffset">Offset from RBP (typically negative for locals).</param>
    /// <param name="isInterior">True if this is an interior pointer.</param>
    /// <param name="isPinned">True if this pointer is pinned.</param>
    public void AddStackSlot(int stackOffset, bool isInterior = false, bool isPinned = false)
    {
        if (_numSlots >= MaxSlots)
        {
            DebugConsole.WriteLine("[JITGCInfo] WARNING: Max slots exceeded!");
            return;
        }

        fixed (byte* p = _slotData)
        {
            var slots = (JITGCSlot*)p;
            slots[_numSlots].StackOffset = stackOffset;
            slots[_numSlots].IsInterior = isInterior;
            slots[_numSlots].IsPinned = isPinned;
        }
        _numSlots++;
    }

    /// <summary>
    /// Record a safe point (call site) at the given native code offset.
    /// Call this after emitting each CALL instruction.
    /// </summary>
    /// <param name="codeOffset">Native code offset (RIP relative to function start).</param>
    public void AddSafePoint(uint codeOffset)
    {
        if (_numSafePoints >= MaxSafePoints)
        {
            DebugConsole.WriteLine("[JITGCInfo] WARNING: Max safe points exceeded!");
            return;
        }

        fixed (uint* p = _safePointOffsets)
        {
            p[_numSafePoints] = codeOffset;
        }
        _numSafePoints++;
    }

    /// <summary>
    /// Get the number of GC slots.
    /// </summary>
    public int NumSlots => _numSlots;

    /// <summary>
    /// Get the number of safe points.
    /// </summary>
    public int NumSafePoints => _numSafePoints;

    /// <summary>
    /// Build the GCInfo data in NativeAOT format.
    /// </summary>
    /// <param name="buffer">Buffer to write GCInfo to.</param>
    /// <param name="outSize">Output: size of generated GCInfo in bytes.</param>
    /// <returns>True if successful.</returns>
    public bool BuildGCInfo(byte* buffer, out int outSize)
    {
        outSize = 0;
        if (buffer == null)
            return false;

        // Sort safe points by code offset (required by decoder)
        SortSafePoints();

        var writer = new BitWriter(buffer);

        // === HEADER ===
        // For simplicity, we'll use a slim header (bit 0 = 0)
        // Slim header format:
        // - Bit 0: 0 = slim header
        // - Bit 1: HasStackBaseRegister
        // - CodeLength (var-length)
        // - NumSafePoints (var-length)
        // - (No NumInterruptibleRanges for slim header - implicitly 0)

        writer.WriteBits(0, 1); // Slim header
        writer.WriteBits(_hasStackBaseRegister ? 1u : 0u, 1); // HasStackBaseRegister

        // Code length
        writer.WriteVarLengthUnsigned(_codeLength, CODE_LENGTH_ENCBASE);

        // NumSafePoints
        writer.WriteVarLengthUnsigned((uint)_numSafePoints, NUM_SAFE_POINTS_ENCBASE);

        // For slim headers, NumInterruptibleRanges is NOT encoded (implicitly 0)

        // === SAFE POINT OFFSETS ===
        // Each safe point is encoded as a fixed number of bits (ceil(log2(codeLength)))
        int bitsPerOffset = CeilLog2(_codeLength);
        if (bitsPerOffset == 0) bitsPerOffset = 1;

        fixed (uint* spPtr = _safePointOffsets)
        {
            for (int i = 0; i < _numSafePoints; i++)
            {
                writer.WriteBits(spPtr[i], bitsPerOffset);
            }
        }

        // === SLOT TABLE COUNTS ===
        // Format:
        // - Bit: hasRegisters (0 = no registers)
        // - If hasRegisters: NumRegisters (var-length)
        // - Bit: hasStackSlots (1 = has stack slots)
        // - If hasStackSlots: NumStackSlots (var-length), NumUntrackedSlots (var-length)

        // We have no register slots (naive JIT uses stack only)
        writer.WriteBits(0, 1); // No register slots

        // Stack slots
        if (_numSlots > 0)
        {
            writer.WriteBits(1, 1); // Has stack slots
            writer.WriteVarLengthUnsigned((uint)_numSlots, NUM_STACK_SLOTS_ENCBASE);
            writer.WriteVarLengthUnsigned(0, NUM_UNTRACKED_SLOTS_ENCBASE); // No untracked
        }
        else
        {
            writer.WriteBits(0, 1); // No stack slots
        }

        // === SLOT DEFINITIONS ===
        // For stack slots:
        // - First slot: base (2 bits) + normalized offset (var-length) + flags (2 bits)
        // - Subsequent slots: base (2 bits) + delta or absolute based on prevFlags

        if (_numSlots > 0)
        {
            fixed (byte* p = _slotData)
            {
                var slots = (JITGCSlot*)p;
                uint prevFlags = 0;
                int prevNormOffset = 0;

                for (int i = 0; i < _numSlots; i++)
                {
                    // Normalize offset: divide by 8 (pointer size) for encoding
                    // GCInfo stores normalized offsets
                    int normOffset = slots[i].StackOffset >> 3; // NORMALIZE_STACK_SLOT

                    // Build flags
                    uint flags = 0;
                    if (slots[i].IsInterior) flags |= 1;
                    if (slots[i].IsPinned) flags |= 2;

                    if (i == 0)
                    {
                        // First slot - absolute encoding
                        // Base: 2 = FramePointer (we use RBP-relative)
                        writer.WriteBits(2, 2); // GCSlotBase.FramePointer
                        writer.WriteVarLengthSigned(normOffset, STACK_SLOT_ENCBASE);
                        writer.WriteBits(flags, 2);
                    }
                    else
                    {
                        // Always write base for subsequent slots
                        writer.WriteBits(2, 2); // GCSlotBase.FramePointer

                        if (prevFlags != 0)
                        {
                            // Previous had flags, use absolute encoding
                            writer.WriteVarLengthSigned(normOffset, STACK_SLOT_ENCBASE);
                            writer.WriteBits(flags, 2);
                        }
                        else
                        {
                            // Previous had no flags, use delta encoding (NO flags read)
                            int delta = normOffset - prevNormOffset;
                            writer.WriteVarLengthUnsigned((uint)delta, STACK_SLOT_DELTA_ENCBASE);
                            // Don't write flags when using delta encoding
                            flags = 0; // Force flags to 0 for next iteration
                        }
                    }

                    prevFlags = flags;
                    prevNormOffset = normOffset;
                }
            }
        }

        // === LIVENESS DATA ===
        // For naive JIT, we use conservative approach: all slots live at all safe points.
        // This is encoded as non-RLE, non-indirect: just 1 bit per slot per safe point.

        if (_numSafePoints > 0 && _numSlots > 0)
        {
            // No indirect table flag
            writer.WriteBits(0, 1); // Not using indirect live slot table

            // For each safe point, write 1 bit per slot (all live)
            for (int sp = 0; sp < _numSafePoints; sp++)
            {
                for (int slot = 0; slot < _numSlots; slot++)
                {
                    writer.WriteBits(1, 1); // Slot is live
                }
            }
        }

        // Get final size (round up to bytes)
        outSize = writer.BytesWritten;

        return true;
    }

    /// <summary>
    /// Calculate the maximum size needed for GCInfo.
    /// </summary>
    public int MaxGCInfoSize()
    {
        // Conservative estimate:
        // Header: ~20 bits
        // Safe points: numSafePoints * bitsPerOffset
        // Slot counts: ~10 bits
        // Slot definitions: numSlots * ~20 bits each
        // Liveness: numSafePoints * numSlots bits
        int bitsPerOffset = CeilLog2(_codeLength);
        if (bitsPerOffset == 0) bitsPerOffset = 1;

        int headerBits = 32;
        int safePointBits = _numSafePoints * bitsPerOffset;
        int slotCountBits = 16;
        int slotDefBits = _numSlots * 24;
        int livenessBits = _numSafePoints * _numSlots;

        int totalBits = headerBits + safePointBits + slotCountBits + slotDefBits + livenessBits;
        int calculated = (totalBits + 7) / 8 + 8; // Round up to bytes + padding

        // BitWriter clears 128 bytes in its constructor, so we must return at least 128
        return calculated < 128 ? 128 : calculated;
    }

    /// <summary>
    /// Sort safe points by code offset (required by GCInfo format).
    /// Simple bubble sort since we have few safe points.
    /// </summary>
    private void SortSafePoints()
    {
        fixed (uint* p = _safePointOffsets)
        {
            for (int i = 0; i < _numSafePoints - 1; i++)
            {
                for (int j = 0; j < _numSafePoints - i - 1; j++)
                {
                    if (p[j] > p[j + 1])
                    {
                        uint temp = p[j];
                        p[j] = p[j + 1];
                        p[j + 1] = temp;
                    }
                }
            }
        }
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
}

/// <summary>
/// Bit-level writer for encoding GCInfo.
/// Writes bits LSB-first within each byte.
/// </summary>
public unsafe struct BitWriter
{
    private byte* _buffer;
    private int _bitPosition;

    public BitWriter(byte* buffer)
    {
        _buffer = buffer;
        _bitPosition = 0;

        // Clear first 128 bytes to ensure clean encoding
        for (int i = 0; i < 128; i++)
            buffer[i] = 0;
    }

    /// <summary>
    /// Write n bits to the buffer.
    /// </summary>
    public void WriteBits(uint value, int count)
    {
        for (int i = 0; i < count; i++)
        {
            int byteOffset = _bitPosition / 8;
            int bitOffset = _bitPosition % 8;

            // Extract bit i from value and write it
            uint bit = (value >> i) & 1;
            _buffer[byteOffset] |= (byte)(bit << bitOffset);

            _bitPosition++;
        }
    }

    /// <summary>
    /// Write a variable-length unsigned integer.
    /// Uses the same encoding as GCInfo.
    /// </summary>
    public void WriteVarLengthUnsigned(uint value, int encBase)
    {
        uint numEncodings = 1u << encBase;
        uint maxFirstValue = numEncodings - 1;

        if (value <= maxFirstValue)
        {
            // Fits in first chunk without continuation
            WriteBits(value, encBase + 1);
        }
        else
        {
            // Need continuation - set high bit and encode remainder
            uint firstChunk = (value & maxFirstValue) | numEncodings; // Set continuation bit
            WriteBits(firstChunk, encBase + 1);

            value ^= firstChunk; // XOR as per encoding spec

            // Encode remaining chunks
            while (true)
            {
                uint chunk = value & maxFirstValue;
                value >>= encBase;

                if (value == 0)
                {
                    // Last chunk - no continuation bit
                    WriteBits(chunk, encBase + 1);
                    break;
                }
                else
                {
                    // More chunks - set continuation bit
                    WriteBits(chunk | numEncodings, encBase + 1);
                }
            }
        }
    }

    /// <summary>
    /// Write a variable-length signed integer using zigzag encoding.
    /// </summary>
    public void WriteVarLengthSigned(int value, int encBase)
    {
        // ZigZag encoding: 0->0, -1->1, 1->2, -2->3, 2->4, ...
        uint encoded = (uint)((value << 1) ^ (value >> 31));
        WriteVarLengthUnsigned(encoded, encBase);
    }

    /// <summary>
    /// Get the number of bytes written (rounded up).
    /// </summary>
    public int BytesWritten => (_bitPosition + 7) / 8;

    /// <summary>
    /// Get current bit position.
    /// </summary>
    public int BitPosition => _bitPosition;
}
