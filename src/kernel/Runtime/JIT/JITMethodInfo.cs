// ProtonOS JIT - Method Runtime Information
// Stores exception handling metadata for JIT-compiled methods:
// - RUNTIME_FUNCTION for SEH
// - UNWIND_INFO for stack unwinding
// - NativeAOT-compatible EH clause data

using System.Runtime.InteropServices;
using ProtonOS.Platform;
using ProtonOS.Memory;
using ProtonOS.X64;
using ProtonOS.Threading;

namespace ProtonOS.Runtime.JIT;

/// <summary>
/// Storage for JIT method runtime information.
/// This struct is allocated alongside compiled code in the code heap.
/// Layout matches what ExceptionHandling expects for registered functions.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct JITMethodInfo
{
    /// <summary>Maximum EH clauses per JIT method.</summary>
    public const int MaxEHClauses = 16;

    /// <summary>Base address of the code region (same for all methods in same code heap chunk).</summary>
    public ulong CodeBase;

    /// <summary>RUNTIME_FUNCTION entry for this method.</summary>
    public RuntimeFunction Function;

    /// <summary>Size of prolog in bytes.</summary>
    public byte PrologSize;

    /// <summary>Number of unwind codes.</summary>
    public byte UnwindCodeCount;

    /// <summary>Frame register (0 = RSP, 5 = RBP).</summary>
    public byte FrameRegister;

    /// <summary>Frame register offset (scaled by 16).</summary>
    public byte FrameOffset;

    /// <summary>Number of EH clauses.</summary>
    public byte EHClauseCount;

    /// <summary>Unwind block flags (NativeAOT format).</summary>
    public byte UnwindBlockFlags;

    /// <summary>Reserved for alignment.</summary>
    public ushort Reserved;

    /// <summary>
    /// Fixed storage for UNWIND_INFO structure.
    /// Layout:
    /// - Bytes 0-3: UNWIND_INFO header (version, flags, prolog, count, frame)
    /// - Bytes 4+: Unwind codes array (up to 8 codes = 16 bytes)
    /// - After codes (DWORD aligned): Exception handler RVA if EHANDLER/UHANDLER set
    /// - After handler RVA: Unwind block flags + EH info offset
    /// </summary>
    private fixed byte _unwindData[64];

    /// <summary>
    /// Fixed storage for EH clause data in NativeAOT format.
    /// Format per clause (variable size):
    /// - TryStart (NativeUnsigned)
    /// - (TryLength << 2) | ClauseKind (NativeUnsigned)
    /// - HandlerOffset (NativeUnsigned) for typed/fault/finally
    /// - TypeRVA (4 bytes relative) for Typed only
    /// - FilterOffset (NativeUnsigned) for Filter only
    /// </summary>
    private fixed byte _ehClauseData[256];

    /// <summary>
    /// Create JITMethodInfo for a compiled method.
    /// </summary>
    /// <param name="codeBase">Base address of the code region</param>
    /// <param name="codeStart">Start address of the method code</param>
    /// <param name="codeSize">Size of method code in bytes</param>
    /// <param name="prologSize">Size of method prolog</param>
    /// <param name="frameRegister">Frame register (0=none, 5=RBP)</param>
    /// <param name="frameOffset">Frame offset (scaled by 16)</param>
    /// <returns>Initialized JITMethodInfo</returns>
    public static JITMethodInfo Create(
        ulong codeBase,
        ulong codeStart,
        uint codeSize,
        byte prologSize,
        byte frameRegister,
        byte frameOffset)
    {
        JITMethodInfo info = default;
        info.CodeBase = codeBase;
        info.PrologSize = prologSize;
        info.FrameRegister = frameRegister;
        info.FrameOffset = frameOffset;
        info.EHClauseCount = 0;

        // Set up RUNTIME_FUNCTION
        uint beginRva = (uint)(codeStart - codeBase);
        info.Function.BeginAddress = beginRva;
        info.Function.EndAddress = beginRva + codeSize;
        // UnwindInfoAddress will be set when we finalize

        return info;
    }

    /// <summary>
    /// Add standard unwind codes for frame pointer-based prologue.
    /// Assumes prologue:
    ///   push rbp         (1 byte: 0x55)
    ///   mov rbp, rsp     (3 bytes: 0x48 0x89 0xE5)
    ///   sub rsp, N       (variable)
    /// </summary>
    public void AddStandardUnwindCodes(int stackAlloc)
    {
        fixed (byte* p = _unwindData)
        {
            // Build unwind codes array (reverse order of operations)
            // We need:
            // 1. UWOP_SET_FPREG at offset 4 (after push rbp + mov rbp,rsp)
            // 2. UWOP_PUSH_NONVOL(RBP) at offset 1
            // 3. If stackAlloc > 0: UWOP_ALLOC_SMALL or UWOP_ALLOC_LARGE

            int codeIndex = 0;
            var codes = (UnwindCode*)(p + 4);

            // Stack allocation (if any)
            if (stackAlloc > 0)
            {
                if (stackAlloc <= 128)
                {
                    // UWOP_ALLOC_SMALL: size = (opinfo * 8 + 8), so opinfo = (size - 8) / 8
                    int opinfo = (stackAlloc - 8) / 8;
                    if (opinfo < 0) opinfo = 0;
                    codes[codeIndex].CodeOffset = PrologSize;
                    codes[codeIndex].OpAndInfo = (byte)(UnwindOpCodes.UWOP_ALLOC_SMALL | (opinfo << 4));
                    codeIndex++;
                }
                else if (stackAlloc <= 512 * 1024)
                {
                    // UWOP_ALLOC_LARGE with opinfo=0: next slot is size/8
                    codes[codeIndex].CodeOffset = PrologSize;
                    codes[codeIndex].OpAndInfo = (byte)(UnwindOpCodes.UWOP_ALLOC_LARGE);
                    codeIndex++;
                    *(ushort*)&codes[codeIndex] = (ushort)(stackAlloc / 8);
                    codeIndex++;
                }
            }

            // Set frame pointer (after push rbp + mov rbp, rsp)
            codes[codeIndex].CodeOffset = 4; // After push rbp (1) + mov rbp,rsp (3)
            codes[codeIndex].OpAndInfo = (byte)UnwindOpCodes.UWOP_SET_FPREG;
            codeIndex++;

            // Push RBP
            codes[codeIndex].CodeOffset = 1; // After push rbp
            codes[codeIndex].OpAndInfo = (byte)(UnwindOpCodes.UWOP_PUSH_NONVOL | (UnwindRegister.RBP << 4));
            codeIndex++;

            UnwindCodeCount = (byte)codeIndex;
        }
    }

    /// <summary>
    /// Finalize the UNWIND_INFO structure.
    /// Must be called after adding unwind codes and before registration.
    /// </summary>
    /// <param name="hasEHClauses">True if this method has exception handlers</param>
    /// <returns>Offset from start of JITMethodInfo to UNWIND_INFO</returns>
    public uint FinalizeUnwindInfo(bool hasEHClauses)
    {
        fixed (byte* p = _unwindData)
        {
            // UNWIND_INFO header (4 bytes)
            // Byte 0: Version (3 bits) | Flags (5 bits)
            byte flags = 0;
            if (hasEHClauses)
            {
                flags = (byte)((UnwindFlags.UNW_FLAG_EHANDLER | UnwindFlags.UNW_FLAG_UHANDLER) << 3);
            }
            p[0] = (byte)(1 | flags);  // Version 1

            // Byte 1: Size of prolog
            p[1] = PrologSize;

            // Byte 2: Count of unwind codes
            p[2] = UnwindCodeCount;

            // Byte 3: Frame register (4 bits) | Frame offset (4 bits)
            p[3] = (byte)(FrameRegister | (FrameOffset << 4));

            // After unwind codes, if we have handlers, add exception handler RVA
            int offset = 4 + ((UnwindCodeCount + 1) & ~1) * 2;  // DWORD align

            if (hasEHClauses)
            {
                // Exception handler RVA - we'll use a generic personality routine
                // For now, just set to 0 - the lookup will use our custom method
                *(uint*)(p + offset) = 0;  // Will be filled in during registration
                offset += 4;

                // NativeAOT unwind block extension
                // Byte: UnwindBlockFlags
                UnwindBlockFlags = UBF_FUNC_KIND_ROOT;
                if (EHClauseCount > 0)
                {
                    UnwindBlockFlags |= UBF_FUNC_HAS_EHINFO;
                }
                p[offset] = UnwindBlockFlags;
                offset++;

                // If has EH info, the EH info pointer follows
                // This will be a relative offset to _ehClauseData
                if (EHClauseCount > 0)
                {
                    // Calculate RVA from code base to EH clause data
                    // We'll set this when we know the actual addresses
                    *(uint*)(p + offset) = 0;  // Placeholder - will be patched
                    offset += 4;
                }
            }
        }

        // Return offset of _unwindData from start of struct
        return (uint)((ulong)GetUnwindInfoPtr() - (ulong)GetSelfPtr());
    }

    /// <summary>
    /// Add an EH clause to this method's EH data.
    /// </summary>
    public bool AddEHClause(ref JITExceptionClause clause)
    {
        if (EHClauseCount >= MaxEHClauses)
            return false;

        fixed (byte* p = _ehClauseData)
        {
            // Find current write position
            byte* ptr = p;
            for (int i = 0; i < EHClauseCount; i++)
            {
                // Skip existing clause
                ptr = SkipNativeAotClause(ptr);
            }

            // Write new clause in NativeAOT format
            WriteNativeUnsigned(ref ptr, clause.TryStartOffset);

            uint tryLength = clause.TryEndOffset - clause.TryStartOffset;
            EHClauseKind kind = FlagsToKind(clause.Flags);
            WriteNativeUnsigned(ref ptr, (tryLength << 2) | (uint)kind);

            // Handler offset (for all types)
            WriteNativeUnsigned(ref ptr, clause.HandlerStartOffset);

            // Type-specific data
            if (kind == EHClauseKind.Typed)
            {
                // Type RVA as 4-byte relative (write 0 for now - type matching TBD)
                *(int*)ptr = 0;
                ptr += 4;
            }
            else if (kind == EHClauseKind.Filter)
            {
                WriteNativeUnsigned(ref ptr, clause.ClassTokenOrFilterOffset);
            }

            EHClauseCount++;
        }

        return true;
    }

    /// <summary>
    /// Get the size of the EH clause data in bytes.
    /// </summary>
    public int GetEHClauseDataSize()
    {
        if (EHClauseCount == 0)
            return 0;

        fixed (byte* p = _ehClauseData)
        {
            byte* ptr = p;
            for (int i = 0; i < EHClauseCount; i++)
            {
                ptr = SkipNativeAotClause(ptr);
            }
            return (int)(ptr - p);
        }
    }

    /// <summary>
    /// Build the final EH info data with clause count prefix.
    /// Returns pointer to the EH info and its size.
    /// </summary>
    public void BuildEHInfo(byte* dest, out int size)
    {
        byte* ptr = dest;

        // Write clause count
        WriteNativeUnsigned(ref ptr, (uint)EHClauseCount);

        // Copy clause data
        int clauseDataSize = GetEHClauseDataSize();
        if (clauseDataSize > 0)
        {
            fixed (byte* src = _ehClauseData)
            {
                for (int i = 0; i < clauseDataSize; i++)
                {
                    ptr[i] = src[i];
                }
                ptr += clauseDataSize;
            }
        }

        size = (int)(ptr - dest);
    }

    /// <summary>
    /// Get pointer to self (for offset calculations).
    /// </summary>
    private byte* GetSelfPtr()
    {
        fixed (ulong* p = &CodeBase)
        {
            return (byte*)p;
        }
    }

    /// <summary>
    /// Get pointer to UNWIND_INFO data.
    /// </summary>
    public byte* GetUnwindInfoPtr()
    {
        fixed (byte* p = _unwindData)
        {
            return p;
        }
    }

    /// <summary>
    /// Get pointer to EH clause data.
    /// </summary>
    public byte* GetEHClauseDataPtr()
    {
        fixed (byte* p = _ehClauseData)
        {
            return p;
        }
    }

    /// <summary>
    /// Patch the EH info RVA in the UNWIND_INFO structure.
    /// Call after FinalizeUnwindInfo and after knowing where EH info is stored.
    /// </summary>
    /// <param name="ehInfoRva">RVA of EH info relative to CodeBase</param>
    public void PatchEHInfoRva(uint ehInfoRva)
    {
        if (EHClauseCount == 0)
            return;

        fixed (byte* p = _unwindData)
        {
            // Find the EH info RVA location:
            // Header (4) + unwind codes (rounded up to even) * 2 + handler RVA (4) + flags (1)
            int offset = 4 + ((UnwindCodeCount + 1) & ~1) * 2;
            offset += 4;  // Skip handler RVA
            offset += 1;  // Skip unwind block flags

            // Write the EH info RVA
            *(uint*)(p + offset) = ehInfoRva;
        }
    }

    // NativeAOT unwind block flags
    private const byte UBF_FUNC_KIND_MASK = 0x03;
    private const byte UBF_FUNC_KIND_ROOT = 0x00;
    private const byte UBF_FUNC_KIND_HANDLER = 0x01;
    private const byte UBF_FUNC_KIND_FILTER = 0x02;
    private const byte UBF_FUNC_HAS_EHINFO = 0x04;
    private const byte UBF_FUNC_REVERSE_PINVOKE = 0x08;
    private const byte UBF_FUNC_HAS_ASSOCIATED_DATA = 0x10;

    /// <summary>
    /// Convert IL EH clause flags to NativeAOT clause kind.
    /// </summary>
    private static EHClauseKind FlagsToKind(ILExceptionClauseFlags flags)
    {
        return flags switch
        {
            ILExceptionClauseFlags.Exception => EHClauseKind.Typed,
            ILExceptionClauseFlags.Filter => EHClauseKind.Filter,
            ILExceptionClauseFlags.Finally => EHClauseKind.Finally,
            ILExceptionClauseFlags.Fault => EHClauseKind.Fault,
            _ => EHClauseKind.Typed
        };
    }

    /// <summary>
    /// Write a NativeAOT-encoded unsigned integer.
    /// </summary>
    private static void WriteNativeUnsigned(ref byte* ptr, uint value)
    {
        if (value <= 0x7F)
        {
            // 1-byte: value << 1, bit 0 = 0
            *ptr++ = (byte)(value << 1);
        }
        else if (value <= 0x3FFF)
        {
            // 2-byte: bits 0-1 = 01
            *ptr++ = (byte)((value << 2) | 1);
            *ptr++ = (byte)(value >> 6);
        }
        else if (value <= 0x1FFFFF)
        {
            // 3-byte: bits 0-2 = 011
            *ptr++ = (byte)((value << 3) | 3);
            *ptr++ = (byte)(value >> 5);
            *ptr++ = (byte)(value >> 13);
        }
        else if (value <= 0x0FFFFFFF)
        {
            // 4-byte: bits 0-3 = 0111
            *ptr++ = (byte)((value << 4) | 7);
            *ptr++ = (byte)(value >> 4);
            *ptr++ = (byte)(value >> 12);
            *ptr++ = (byte)(value >> 20);
        }
        else
        {
            // 5-byte: first byte = 0x0F, then 4-byte value
            *ptr++ = 0x0F;
            *ptr++ = (byte)value;
            *ptr++ = (byte)(value >> 8);
            *ptr++ = (byte)(value >> 16);
            *ptr++ = (byte)(value >> 24);
        }
    }

    /// <summary>
    /// Skip a NativeAOT clause in the data stream.
    /// </summary>
    private static byte* SkipNativeAotClause(byte* ptr)
    {
        // TryStartOffset
        SkipNativeUnsigned(ref ptr);

        // (TryLength << 2) | kind - read to get kind
        uint tryLengthAndKind = ReadNativeUnsigned(ref ptr);
        byte kind = (byte)(tryLengthAndKind & 0x3);

        // HandlerOffset
        SkipNativeUnsigned(ref ptr);

        // Type-specific
        if (kind == (byte)EHClauseKind.Typed)
        {
            ptr += 4;  // Type RVA
        }
        else if (kind == (byte)EHClauseKind.Filter)
        {
            SkipNativeUnsigned(ref ptr);  // Filter offset
        }

        return ptr;
    }

    /// <summary>
    /// Skip a NativeAOT-encoded unsigned integer.
    /// </summary>
    private static void SkipNativeUnsigned(ref byte* ptr)
    {
        byte b0 = *ptr++;
        if ((b0 & 1) == 0) return;  // 1 byte
        if ((b0 & 2) == 0) { ptr++; return; }  // 2 bytes
        if ((b0 & 4) == 0) { ptr += 2; return; }  // 3 bytes
        if ((b0 & 8) == 0) { ptr += 3; return; }  // 4 bytes
        ptr += 4;  // 5 bytes
    }

    /// <summary>
    /// Read a NativeAOT-encoded unsigned integer.
    /// </summary>
    private static uint ReadNativeUnsigned(ref byte* ptr)
    {
        byte b0 = *ptr++;

        if ((b0 & 1) == 0)
            return (uint)(b0 >> 1);

        if ((b0 & 2) == 0)
        {
            byte b1 = *ptr++;
            return (uint)((b0 >> 2) | (b1 << 6));
        }

        if ((b0 & 4) == 0)
        {
            byte b1 = *ptr++;
            byte b2 = *ptr++;
            return (uint)((b0 >> 3) | (b1 << 5) | (b2 << 13));
        }

        if ((b0 & 8) == 0)
        {
            byte b1 = *ptr++;
            byte b2 = *ptr++;
            byte b3 = *ptr++;
            return (uint)((b0 >> 4) | (b1 << 4) | (b2 << 12) | (b3 << 20));
        }

        uint result = (uint)(*ptr++);
        result |= (uint)(*ptr++) << 8;
        result |= (uint)(*ptr++) << 16;
        result |= (uint)(*ptr++) << 24;
        return result;
    }
}

/// <summary>
/// Registry for JIT-compiled method metadata.
/// Manages registration with the exception handling system.
/// </summary>
public static unsafe class JITMethodRegistry
{
    private const int MaxMethods = 256;

    // Storage for method info structures
    private static JITMethodInfo* _methods;
    private static int _methodCount;
    private static SpinLock _lock;
    private static bool _initialized;

    // Separate storage for EH info data (needs to be in data section, not code heap)
    private static byte* _ehInfoStorage;
    private static int _ehInfoOffset;
    private const int EHInfoStorageSize = 16 * 1024;  // 16KB for EH info

    /// <summary>
    /// Initialize the JIT method registry.
    /// </summary>
    public static bool Init()
    {
        if (_initialized)
            return true;

        // Allocate method info array
        int size = MaxMethods * sizeof(JITMethodInfo);
        _methods = (JITMethodInfo*)VirtualMemory.AllocateVirtualRange(0, (ulong)size, true,
            PageFlags.Present | PageFlags.Writable);
        if (_methods == null)
        {
            DebugConsole.WriteLine("[JITRegistry] Failed to allocate method storage");
            return false;
        }

        // Allocate EH info storage
        _ehInfoStorage = (byte*)VirtualMemory.AllocateVirtualRange(0, EHInfoStorageSize, true,
            PageFlags.Present | PageFlags.Writable);
        if (_ehInfoStorage == null)
        {
            DebugConsole.WriteLine("[JITRegistry] Failed to allocate EH info storage");
            return false;
        }

        _methodCount = 0;
        _ehInfoOffset = 0;
        _initialized = true;

        DebugConsole.WriteLine("[JITRegistry] Initialized");
        return true;
    }

    /// <summary>
    /// Register a JIT-compiled method with exception handling support.
    /// </summary>
    /// <param name="info">Method info structure to register</param>
    /// <param name="nativeClauses">EH clauses with native offsets</param>
    /// <returns>True if registered successfully</returns>
    public static bool RegisterMethod(ref JITMethodInfo info, ref JITExceptionClauses nativeClauses)
    {
        if (!_initialized && !Init())
            return false;

        _lock.Acquire();

        if (_methodCount >= MaxMethods)
        {
            _lock.Release();
            DebugConsole.WriteLine("[JITRegistry] Method limit reached");
            return false;
        }

        // Add EH clauses to method info
        for (int i = 0; i < nativeClauses.Count; i++)
        {
            var clause = nativeClauses.GetClause(i);
            if (clause.IsValid)
            {
                info.AddEHClause(ref clause);
            }
        }

        // Finalize unwind info first (to set up the structure)
        info.FinalizeUnwindInfo(info.EHClauseCount > 0);

        // Store method info
        int index = _methodCount++;
        _methods[index] = info;

        // Build EH info data into separate storage
        byte* ehInfoPtr = null;
        if (info.EHClauseCount > 0)
        {
            ehInfoPtr = _ehInfoStorage + _ehInfoOffset;
            int ehInfoSize;
            _methods[index].BuildEHInfo(ehInfoPtr, out ehInfoSize);
            _ehInfoOffset += (ehInfoSize + 3) & ~3;  // DWORD align

            // Patch the EH info RVA in the UNWIND_INFO
            // EH info is in _ehInfoStorage, need to calculate RVA relative to CodeBase
            uint ehInfoRva = (uint)((ulong)ehInfoPtr - info.CodeBase);
            _methods[index].PatchEHInfoRva(ehInfoRva);
        }

        // Calculate UNWIND_INFO RVA
        // The UNWIND_INFO is at a fixed offset within the JITMethodInfo struct
        byte* methodInfoPtr = (byte*)&_methods[index];
        uint unwindRva = (uint)((ulong)_methods[index].GetUnwindInfoPtr() - info.CodeBase);
        _methods[index].Function.UnwindInfoAddress = unwindRva;

        // Register with ExceptionHandling
        bool success = ExceptionHandling.AddFunctionTable(
            &_methods[index].Function,
            1,
            info.CodeBase);

        _lock.Release();

        if (success)
        {
            DebugConsole.Write("[JITRegistry] Registered method at RVA 0x");
            DebugConsole.WriteHex(info.Function.BeginAddress);
            DebugConsole.Write("-0x");
            DebugConsole.WriteHex(info.Function.EndAddress);
            if (info.EHClauseCount > 0)
            {
                DebugConsole.Write(" with ");
                DebugConsole.WriteDecimal(info.EHClauseCount);
                DebugConsole.Write(" EH clause(s)");
            }
            DebugConsole.WriteLine();
        }

        return success;
    }

    /// <summary>
    /// Get the number of registered methods.
    /// </summary>
    public static int MethodCount => _methodCount;
}
