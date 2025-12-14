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
using ProtonOS.Runtime;

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

    /// <summary>Size of UNWIND_INFO per funclet (smaller than main method).</summary>
    public const int FuncletUnwindInfoSize = 16;

    /// <summary>Size of each FuncletEntry structure.</summary>
    public const int FuncletEntrySize = 12 + FuncletUnwindInfoSize + 4;  // RuntimeFunction + UnwindInfo + metadata

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
    /// Fixed storage for GCInfo data.
    /// GCInfo describes which stack slots and registers contain live GC references
    /// at each safe point (call sites). Used by GC during stack walking.
    /// Max size is ~128 bytes for typical methods.
    /// </summary>
    private fixed byte _gcInfoData[128];

    /// <summary>Size of the GCInfo data stored in _gcInfoData.</summary>
    public int GCInfoSize;

    // ==================== Funclet Support ====================
    // Funclet data is allocated from CodeHeap on demand to save space in JITMethodInfo.
    // Most methods have no exception handlers, so no funclet allocation is needed.

    /// <summary>Number of funclets (exception handlers) for this method.</summary>
    public byte FuncletCount;

    /// <summary>Capacity of funclet array (how many were allocated).</summary>
    public byte FuncletCapacity;

    /// <summary>Reserved for alignment.</summary>
    private ushort _funcletReserved;

    /// <summary>
    /// Pointer to funclet data array in CodeHeap.
    /// Each entry is FuncletEntrySize bytes containing:
    /// - RuntimeFunction (12 bytes)
    /// - UNWIND_INFO (FuncletUnwindInfoSize bytes)
    /// - Metadata: 4 bytes (kind, ehClauseIndex, padding)
    /// Allocated from CodeHeap when funclets are added.
    /// </summary>
    public byte* FuncletData;

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
            // NOTE: We do NOT set UNW_FLAG_EHANDLER for JIT code!
            // JIT code uses NativeAOT-style EH tables, not SEH personality routines.
            // Setting UNW_FLAG_EHANDLER would cause the SEH dispatch path to call
            // a non-existent handler at RVA 0, which re-enters the function.
            byte flags = 0;
            if (hasEHClauses)
            {
                // Use UNW_FLAG_UHANDLER to indicate unwind info, but NOT UNW_FLAG_EHANDLER
                // The NativeAOT EH info is stored in the unwind block extension instead.
                flags = (byte)(UnwindFlags.UNW_FLAG_UHANDLER << 3);
            }
            p[0] = (byte)(1 | flags);  // Version 1

            // Byte 1: Size of prolog
            p[1] = PrologSize;

            // Byte 2: Count of unwind codes
            p[2] = UnwindCodeCount;

            // Byte 3: Frame register (4 bits) | Frame offset (4 bits)
            p[3] = (byte)(FrameRegister | (FrameOffset << 4));

            // After unwind codes, add handler info if we have handlers
            int offset = 4 + ((UnwindCodeCount + 1) & ~1) * 2;  // DWORD align

            if (hasEHClauses)
            {
                // For UNW_FLAG_UHANDLER, we still need the handler RVA slot
                // (Windows requires it), but it won't be called for EH dispatch
                *(uint*)(p + offset) = 0;
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
            DebugConsole.Write("[AddEHClause] tryStart=0x");
            DebugConsole.WriteHex(clause.TryStartOffset);
            DebugConsole.Write(" tryEnd=0x");
            DebugConsole.WriteHex(clause.TryEndOffset);
            DebugConsole.Write(" handler=0x");
            DebugConsole.WriteHex(clause.HandlerStartOffset);
            DebugConsole.WriteLine();

            WriteNativeUnsigned(ref ptr, clause.TryStartOffset);

            uint tryLength = clause.TryEndOffset - clause.TryStartOffset;
            EHClauseKind kind = FlagsToKind(clause.Flags);
            WriteNativeUnsigned(ref ptr, (tryLength << 2) | (uint)kind);

            // Handler offset (for all types)
            WriteNativeUnsigned(ref ptr, clause.HandlerStartOffset);

            // Type-specific data
            if (kind == EHClauseKind.Typed)
            {
                // Write the MethodTable pointer as 8-byte value
                // This will be used by FindMatchingEHClause to check type compatibility
                *(ulong*)ptr = clause.CatchTypeMethodTable;
                ptr += 8;
            }
            else if (kind == EHClauseKind.Filter)
            {
                WriteNativeUnsigned(ref ptr, clause.ClassTokenOrFilterOffset);
            }

            // Leave target offset (for catch/filter handlers - where to resume after handler returns)
            if (kind == EHClauseKind.Typed || kind == EHClauseKind.Filter)
            {
                WriteNativeUnsigned(ref ptr, clause.LeaveTargetOffset);
                DebugConsole.Write("[AddEHClause] leaveTarget=0x");
                DebugConsole.WriteHex(clause.LeaveTargetOffset);
                DebugConsole.WriteLine();
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
                DebugConsole.Write("[BuildEHInfo] clauseDataSize=");
                DebugConsole.WriteDecimal((uint)clauseDataSize);
                DebugConsole.Write(" bytes: ");
                for (int i = 0; i < clauseDataSize && i < 16; i++)
                {
                    DebugConsole.WriteHex((ulong)src[i]);
                    DebugConsole.Write(" ");
                }
                DebugConsole.WriteLine();

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
    /// Get pointer to GCInfo data.
    /// </summary>
    public byte* GetGCInfoPtr()
    {
        fixed (byte* p = _gcInfoData)
        {
            return p;
        }
    }

    /// <summary>
    /// Store GCInfo data for this method.
    /// </summary>
    /// <param name="gcInfoData">Pointer to encoded GCInfo data.</param>
    /// <param name="size">Size of the GCInfo data in bytes.</param>
    /// <returns>True if stored successfully, false if too large.</returns>
    public bool SetGCInfo(byte* gcInfoData, int size)
    {
        if (size > 128 || size <= 0)
        {
            GCInfoSize = 0;
            return false;
        }

        fixed (byte* p = _gcInfoData)
        {
            for (int i = 0; i < size; i++)
            {
                p[i] = gcInfoData[i];
            }
        }
        GCInfoSize = size;
        return true;
    }

    /// <summary>
    /// Check if this method has GCInfo.
    /// </summary>
    public bool HasGCInfo => GCInfoSize > 0;

    // ==================== Funclet Management Methods ====================

    /// <summary>
    /// Allocate storage for funclets from CodeHeap.
    /// Call this before AddFunclet if you know how many funclets you need.
    /// </summary>
    /// <param name="count">Number of funclets to allocate space for</param>
    /// <returns>True if allocation succeeded</returns>
    public bool AllocateFunclets(int count)
    {
        if (count <= 0)
            return true;

        if (FuncletData != null)
        {
            // Already allocated - check if we have enough capacity
            if (count <= FuncletCapacity)
                return true;
            // Would need reallocation - not supported (preallocate correctly!)
            return false;
        }

        ulong size = (ulong)(count * FuncletEntrySize);
        FuncletData = CodeHeap.Alloc(size);
        if (FuncletData == null)
            return false;

        FuncletCapacity = (byte)count;
        return true;
    }

    /// <summary>
    /// Add a funclet to this method.
    /// Will allocate from CodeHeap if needed (single funclet at a time).
    /// For better efficiency, call AllocateFunclets first if you know the count.
    /// </summary>
    /// <param name="codeStart">Start address of the funclet code</param>
    /// <param name="codeSize">Size of funclet code in bytes</param>
    /// <param name="isFilter">True if this is a filter funclet, false for handler</param>
    /// <param name="ehClauseIndex">Index of the EH clause this funclet belongs to</param>
    /// <returns>Funclet index, or -1 if allocation failed or capacity exceeded</returns>
    public int AddFunclet(ulong codeStart, uint codeSize, bool isFilter, int ehClauseIndex)
    {
        // Ensure we have storage
        if (FuncletData == null)
        {
            // Allocate space for 1 funclet (caller should have used AllocateFunclets for efficiency)
            if (!AllocateFunclets(1))
                return -1;
        }
        else if (FuncletCount >= FuncletCapacity)
        {
            return -1;  // No room
        }

        int index = FuncletCount;

        // Calculate offset into funclet data array
        byte* entry = FuncletData + (index * FuncletEntrySize);

        // Set up RUNTIME_FUNCTION for this funclet (first 12 bytes of entry)
        RuntimeFunction* func = (RuntimeFunction*)entry;
        uint beginRva = (uint)(codeStart - CodeBase);
        func->BeginAddress = beginRva;
        func->EndAddress = beginRva + codeSize;
        // UnwindInfoAddress will be set during finalization

        // Set up funclet metadata (last 4 bytes of entry)
        byte* metaPtr = entry + 12 + FuncletUnwindInfoSize;
        byte meta = (byte)(isFilter ? 1 : 0);
        meta |= (byte)((ehClauseIndex & 0x3F) << 2);
        *metaPtr = meta;

        FuncletCount++;
        return index;
    }

    /// <summary>
    /// Finalize UNWIND_INFO for a funclet.
    /// Funclets have a simple prolog: push rbp; mov rbp, rdx
    /// </summary>
    /// <param name="funcletIndex">Index of the funclet</param>
    /// <param name="prologSize">Size of funclet prolog (typically 4 bytes)</param>
    public void FinalizeFuncletUnwindInfo(int funcletIndex, byte prologSize)
    {
        if (funcletIndex < 0 || funcletIndex >= FuncletCount || FuncletData == null)
            return;

        // FuncletEntry layout:
        //   Offset 0:  RuntimeFunction (12 bytes)
        //   Offset 12: UNWIND_INFO (FuncletUnwindInfoSize bytes = 16)
        //   Offset 28: Metadata (4 bytes)
        byte* entry = FuncletData + (funcletIndex * FuncletEntrySize);
        RuntimeFunction* func = (RuntimeFunction*)entry;
        byte* p = entry + 12;  // UNWIND_INFO starts after RuntimeFunction
        byte* metaPtr = entry + 12 + FuncletUnwindInfoSize;

        // UNWIND_INFO header (4 bytes)
        // Flags = 0 (funclets don't have their own handlers)
        p[0] = 1;  // Version 1, no flags

        // Prolog size = 4 bytes for funclet prolog
        // (push rbp=1 + mov rbp,rdx=3)
        p[1] = prologSize;

        // Count of unwind codes = 1
        // 1. PUSH_NONVOL(RBP) at offset 1
        p[2] = 1;

        // Frame register = 0 (none) - RBP doesn't track the stack in funclets
        // RBP points to parent frame, not funclet's stack
        p[3] = 0;

        // Unwind codes (reverse order of operations):
        var codes = (UnwindCode*)(p + 4);

        // PUSH_NONVOL(RBP) at offset 1
        codes[0].CodeOffset = 1;
        codes[0].OpAndInfo = (byte)(UnwindOpCodes.UWOP_PUSH_NONVOL | (UnwindRegister.RBP << 4));

        // After unwind codes: NativeAOT unwind block flags (1 byte)
        // Offset = 4 (header) + 1 * 2 (codes) = 6
        byte* flagsPtr = p + 6;

        // Get funclet metadata
        // metaPtr byte 0: bit 0 = isFilter, bits 2-7 = ehClauseIndex
        bool isFilter = (*metaPtr & 0x01) != 0;
        int ehClauseIndex = (*metaPtr >> 2) & 0x3F;

        // Encode funclet kind and clause index in flags byte:
        // bits 0-1: func kind (ROOT=0, HANDLER=1, FILTER=2)
        // bits 2-7: EH clause index (0-63)
        byte kind = isFilter ? UBF_FUNC_KIND_FILTER : UBF_FUNC_KIND_HANDLER;
        *flagsPtr = (byte)(kind | (ehClauseIndex << 2));

        // Calculate UNWIND_INFO RVA and patch into RUNTIME_FUNCTION
        uint unwindInfoRva = (uint)((ulong)p - CodeBase);
        func->UnwindInfoAddress = unwindInfoRva;
    }

    /// <summary>
    /// Get pointer to a funclet's RUNTIME_FUNCTION entry.
    /// </summary>
    public RuntimeFunction* GetFuncletRuntimeFunction(int index)
    {
        if (index < 0 || index >= FuncletCount || FuncletData == null)
            return null;

        byte* entry = FuncletData + (index * FuncletEntrySize);
        return (RuntimeFunction*)entry;
    }

    /// <summary>
    /// Get pointer to a funclet's UNWIND_INFO.
    /// </summary>
    public byte* GetFuncletUnwindInfoPtr(int index)
    {
        if (index < 0 || index >= FuncletCount || FuncletData == null)
            return null;

        byte* entry = FuncletData + (index * FuncletEntrySize);
        return entry + 12;  // UNWIND_INFO is at offset 12 (after RuntimeFunction)
    }

    /// <summary>
    /// Check if this method has funclets.
    /// </summary>
    public bool HasFunclets => FuncletCount > 0;

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
            ptr += 8;  // MethodTable pointer (8 bytes)
        }
        else if (kind == (byte)EHClauseKind.Filter)
        {
            SkipNativeUnsigned(ref ptr);  // Filter offset
        }

        // Leave target offset (for catch/filter handlers)
        if (kind == (byte)EHClauseKind.Typed || kind == (byte)EHClauseKind.Filter)
        {
            SkipNativeUnsigned(ref ptr);  // Leave target
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
/// Uses block allocator for dynamic growth - no fixed limit.
/// </summary>
public static unsafe class JITMethodRegistry
{
    // Block allocator for method info - small block size to exercise growth during tests
    private const int MethodBlockSize = 32;
    private static BlockChain _methodChain;
    private static SpinLock _lock;
    private static bool _initialized;

    // Note: UNWIND_INFO and EH info are allocated from CodeHeap alongside method code
    // because RUNTIME_FUNCTION RVAs must all be relative to the same ImageBase (CodeBase).

    /// <summary>
    /// Initialize the JIT method registry.
    /// </summary>
    public static bool Init()
    {
        if (_initialized)
            return true;

        // Initialize block allocator for method info structures
        fixed (BlockChain* chainPtr = &_methodChain)
        {
            if (!BlockAllocator.Init(chainPtr, sizeof(JITMethodInfo), MethodBlockSize))
            {
                DebugConsole.WriteLine("[JITRegistry] Failed to initialize method allocator");
                return false;
            }
        }

        _initialized = true;

        DebugConsole.WriteLine("[JITRegistry] Initialized with block allocator");
        return true;
    }

    /// <summary>
    /// Register a JIT-compiled method with exception handling support.
    /// UNWIND_INFO and EH info are allocated from CodeHeap so that all RVAs
    /// in RUNTIME_FUNCTION are relative to the same ImageBase (CodeBase).
    /// Uses block allocator - no fixed limit on number of methods.
    /// </summary>
    /// <param name="info">Method info structure to register</param>
    /// <param name="nativeClauses">EH clauses with native offsets</param>
    /// <returns>True if registered successfully</returns>
    public static bool RegisterMethod(ref JITMethodInfo info, ref JITExceptionClauses nativeClauses)
    {
        if (!_initialized && !Init())
            return false;

        _lock.Acquire();

        // Add EH clauses to method info
        for (int i = 0; i < nativeClauses.Count; i++)
        {
            var clause = nativeClauses.GetClause(i);
            if (clause.IsValid)
            {
                info.AddEHClause(ref clause);
            }
        }

        // Finalize unwind info first (to set up the structure in the local info)
        info.FinalizeUnwindInfo(info.EHClauseCount > 0);

        // Store method info in block allocator
        JITMethodInfo* storedInfo;
        fixed (JITMethodInfo* infoPtr = &info)
        fixed (BlockChain* chainPtr = &_methodChain)
        {
            storedInfo = (JITMethodInfo*)BlockAllocator.Add(chainPtr, infoPtr);
        }

        if (storedInfo == null)
        {
            _lock.Release();
            DebugConsole.WriteLine("[JITRegistry] Failed to allocate method info");
            return false;
        }

        // Allocate UNWIND_INFO from CodeHeap so it can be addressed via RVA from CodeBase
        // UNWIND_INFO is 64 bytes in JITMethodInfo._unwindData
        const int unwindInfoSize = 64;
        byte* unwindInfoInCodeHeap = CodeHeap.Alloc(unwindInfoSize);
        if (unwindInfoInCodeHeap == null)
        {
            _lock.Release();
            DebugConsole.WriteLine("[JITRegistry] Failed to allocate unwind info from CodeHeap");
            return false;
        }

        // Copy unwind info to CodeHeap allocation
        byte* srcUnwind = storedInfo->GetUnwindInfoPtr();
        for (int i = 0; i < unwindInfoSize; i++)
        {
            unwindInfoInCodeHeap[i] = srcUnwind[i];
        }

        // Calculate UNWIND_INFO RVA relative to CodeBase
        uint unwindRva = (uint)((ulong)unwindInfoInCodeHeap - info.CodeBase);
        storedInfo->Function.UnwindInfoAddress = unwindRva;

        // Allocate and build EH info in CodeHeap if needed
        if (info.EHClauseCount > 0)
        {
            // Estimate EH info size: count (1-5 bytes) + clauses (up to ~20 bytes each)
            int ehInfoMaxSize = 5 + (info.EHClauseCount * 24);
            byte* ehInfoInCodeHeap = CodeHeap.Alloc((ulong)ehInfoMaxSize);
            if (ehInfoInCodeHeap == null)
            {
                _lock.Release();
                DebugConsole.WriteLine("[JITRegistry] Failed to allocate EH info from CodeHeap");
                return false;
            }

            // Build EH info directly into CodeHeap allocation
            int ehInfoSize;
            storedInfo->BuildEHInfo(ehInfoInCodeHeap, out ehInfoSize);

            // Calculate EH info RVA relative to CodeBase
            uint ehInfoRva = (uint)((ulong)ehInfoInCodeHeap - info.CodeBase);

            // Patch the EH info RVA in the CodeHeap copy of UNWIND_INFO
            // The RVA field is at: header(4) + unwindcodes(aligned) + handlerRVA(4) + flags(1)
            int unwindCodeCount = unwindInfoInCodeHeap[2];
            int offset = 4 + ((unwindCodeCount + 1) & ~1) * 2;
            offset += 4;  // Skip handler RVA
            offset += 1;  // Skip unwind block flags
            *(uint*)(unwindInfoInCodeHeap + offset) = ehInfoRva;
        }

        // Build RUNTIME_FUNCTION array: main method + funclets
        // IMPORTANT: This array must be persistent (not stack-allocated) because
        // ExceptionHandling.AddFunctionTable stores the pointer
        int totalFunctions = 1 + info.FuncletCount;

        // Allocate persistent RUNTIME_FUNCTION array from CodeHeap
        ulong functionArraySize = (ulong)(totalFunctions * sizeof(RuntimeFunction));
        RuntimeFunction* functions = (RuntimeFunction*)CodeHeap.Alloc(functionArraySize);
        if (functions == null)
        {
            _lock.Release();
            DebugConsole.WriteLine("[JITRegistry] Failed to allocate function table from CodeHeap");
            return false;
        }

        // First entry is the main method
        functions[0] = storedInfo->Function;

        // Finalize and add funclet RUNTIME_FUNCTIONs
        // Note: FuncletData is already allocated from CodeHeap, so UNWIND_INFO has valid RVAs
        for (int i = 0; i < info.FuncletCount; i++)
        {
            // Finalize funclet unwind info - this sets up the UNWIND_INFO in FuncletData
            // and patches the UnwindInfoAddress RVA in the RUNTIME_FUNCTION
            storedInfo->FinalizeFuncletUnwindInfo(i, 4); // 4-byte prolog: push rbp; mov rbp, rdx

            // Get funclet RUNTIME_FUNCTION (already has correct UnwindInfoAddress from finalize)
            RuntimeFunction* funcletFunc = storedInfo->GetFuncletRuntimeFunction(i);

            // Add to function array
            functions[1 + i] = *funcletFunc;
        }

        // Register all functions (main + funclets) with ExceptionHandling
        bool success = ExceptionHandling.AddFunctionTable(
            functions,
            (uint)totalFunctions,
            info.CodeBase);

        _lock.Release();

        if (success)
        {
            DebugConsole.Write("[JITRegistry] Registered method base=0x");
            DebugConsole.WriteHex(info.CodeBase);
            DebugConsole.Write(" RVA 0x");
            DebugConsole.WriteHex(info.Function.BeginAddress);
            DebugConsole.Write("-0x");
            DebugConsole.WriteHex(info.Function.EndAddress);
            DebugConsole.Write(" unwind=0x");
            DebugConsole.WriteHex(unwindRva);
            if (info.EHClauseCount > 0)
            {
                DebugConsole.Write(" with ");
                DebugConsole.WriteDecimal(info.EHClauseCount);
                DebugConsole.Write(" EH clause(s)");
            }
            if (info.FuncletCount > 0)
            {
                DebugConsole.Write(" + ");
                DebugConsole.WriteDecimal(info.FuncletCount);
                DebugConsole.Write(" funclet(s)");
            }
            DebugConsole.WriteLine();
        }

        return success;
    }

    /// <summary>
    /// Register a JIT-compiled method for stack unwinding only (no EH clauses).
    /// This is needed so the exception handler can properly unwind through JIT methods
    /// even if they don't have try/catch/finally blocks.
    /// Uses block allocator - no fixed limit on number of methods.
    /// </summary>
    /// <param name="info">Method info structure to register</param>
    /// <returns>True if registered successfully</returns>
    public static bool RegisterMethodForUnwind(ref JITMethodInfo info)
    {
        if (!_initialized && !Init())
            return false;

        _lock.Acquire();

        // Finalize unwind info (no EH clauses)
        info.FinalizeUnwindInfo(false);

        // Store method info in block allocator
        JITMethodInfo* storedInfo;
        fixed (JITMethodInfo* infoPtr = &info)
        fixed (BlockChain* chainPtr = &_methodChain)
        {
            storedInfo = (JITMethodInfo*)BlockAllocator.Add(chainPtr, infoPtr);
        }

        if (storedInfo == null)
        {
            _lock.Release();
            return false;
        }

        // Allocate UNWIND_INFO from CodeHeap so it can be addressed via RVA from CodeBase
        const int unwindInfoSize = 64;
        byte* unwindInfoInCodeHeap = CodeHeap.Alloc(unwindInfoSize);
        if (unwindInfoInCodeHeap == null)
        {
            _lock.Release();
            return false;
        }

        // Copy unwind info to CodeHeap
        byte* localUnwind = storedInfo->GetUnwindInfoPtr();
        for (int i = 0; i < unwindInfoSize; i++)
            unwindInfoInCodeHeap[i] = localUnwind[i];

        // Calculate UNWIND_INFO RVA relative to CodeBase
        uint unwindRva = (uint)((ulong)unwindInfoInCodeHeap - info.CodeBase);

        // Update the stored info's RUNTIME_FUNCTION with correct unwind RVA
        storedInfo->Function.UnwindInfoAddress = unwindRva;

        // Build single-entry RUNTIME_FUNCTION array
        RuntimeFunction* functions = (RuntimeFunction*)CodeHeap.Alloc((ulong)sizeof(RuntimeFunction));
        if (functions == null)
        {
            _lock.Release();
            return false;
        }

        functions[0] = storedInfo->Function;

        // Register with ExceptionHandling
        bool success = ExceptionHandling.AddFunctionTable(functions, 1, info.CodeBase);

        _lock.Release();
        return success;
    }

    /// <summary>
    /// Get the number of registered methods.
    /// </summary>
    public static int MethodCount
    {
        get
        {
            fixed (BlockChain* chainPtr = &_methodChain)
            {
                return chainPtr->TotalCount;
            }
        }
    }

    /// <summary>
    /// Find the GCInfo for a given instruction pointer.
    /// Used by GC during stack walking to enumerate stack roots in JIT'd frames.
    /// </summary>
    /// <param name="ip">Instruction pointer (absolute address)</param>
    /// <param name="gcInfoPtr">Output: pointer to GCInfo data if found</param>
    /// <param name="gcInfoSize">Output: size of GCInfo data</param>
    /// <param name="codeOffset">Output: offset of IP within the method (for safe point lookup)</param>
    /// <returns>True if GCInfo found for this IP</returns>
    public static bool FindGCInfoForIP(ulong ip, out byte* gcInfoPtr, out int gcInfoSize, out uint codeOffset)
    {
        gcInfoPtr = null;
        gcInfoSize = 0;
        codeOffset = 0;

        if (!_initialized)
            return false;

        fixed (BlockChain* chainPtr = &_methodChain)
        {
            if (chainPtr->TotalCount == 0)
                return false;

            // Linear search through all blocks
            var block = chainPtr->First;
            while (block != null)
            {
                var methods = (JITMethodInfo*)block->Data;
                for (int i = 0; i < block->Used; i++)
                {
                    ref JITMethodInfo info = ref methods[i];

                    // Calculate absolute address range
                    ulong beginAddr = info.CodeBase + info.Function.BeginAddress;
                    ulong endAddr = info.CodeBase + info.Function.EndAddress;

                    if (ip >= beginAddr && ip < endAddr)
                    {
                        // Found the method containing this IP
                        if (info.HasGCInfo)
                        {
                            gcInfoPtr = info.GetGCInfoPtr();
                            gcInfoSize = info.GCInfoSize;
                            codeOffset = (uint)(ip - beginAddr);
                            return true;
                        }
                        // Method found but no GCInfo (no GC refs in this method)
                        return false;
                    }
                }
                block = block->Next;
            }
        }

        return false;
    }

    /// <summary>
    /// Get method info pointer by index (for testing).
    /// </summary>
    public static JITMethodInfo* GetMethodInfo(int index)
    {
        if (!_initialized)
            return null;

        fixed (BlockChain* chainPtr = &_methodChain)
        {
            if (index < 0 || index >= chainPtr->TotalCount)
                return null;

            return (JITMethodInfo*)BlockAllocator.GetAt(chainPtr, index);
        }
    }
}
