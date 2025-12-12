// ProtonOS JIT - IL Exception Handling Clause Parser
// Parses EH clauses from IL method bodies (ECMA-335 II.25.4.5-6)

using System.Runtime.InteropServices;
using ProtonOS.Platform;

namespace ProtonOS.Runtime.JIT;

/// <summary>
/// IL Method header flags (ECMA-335 II.25.4.1-4)
/// </summary>
public static class CorILMethod
{
    // Method header format (low 2 bits)
    public const byte FormatMask = 0x03;
    public const byte TinyFormat = 0x02;     // 1-byte header
    public const byte FatFormat = 0x03;      // 12-byte header

    // Fat header flags (bits 2-7)
    public const ushort MoreSects = 0x08;    // Extra data sections follow code
    public const ushort InitLocals = 0x10;   // Initialize locals to zero
}

/// <summary>
/// IL Method data section flags (ECMA-335 II.25.4.5)
/// </summary>
public static class CorILMethodSect
{
    public const byte EHTable = 0x01;        // This is an exception handling section
    public const byte OptILTable = 0x02;     // Reserved (obsolete)
    public const byte FatFormat = 0x40;      // Fat format (24-byte clauses)
    public const byte MoreSects = 0x80;      // Another section follows
}

/// <summary>
/// IL Exception clause flags (ECMA-335 II.25.4.6)
/// </summary>
public enum ILExceptionClauseFlags : uint
{
    Exception = 0x0000,   // Typed catch clause (ClassToken is type token)
    Filter = 0x0001,      // Filter clause (FilterOffset is filter IL offset)
    Finally = 0x0002,     // Finally clause
    Fault = 0x0004,       // Fault clause (finally that only runs on exception)
}

/// <summary>
/// Parsed IL exception handling clause.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct ILExceptionClause
{
    /// <summary>Exception clause type.</summary>
    public ILExceptionClauseFlags Flags;

    /// <summary>IL offset where try block starts.</summary>
    public uint TryOffset;

    /// <summary>Length of try block in bytes.</summary>
    public uint TryLength;

    /// <summary>IL offset where handler starts.</summary>
    public uint HandlerOffset;

    /// <summary>Length of handler in bytes.</summary>
    public uint HandlerLength;

    /// <summary>
    /// For typed catch: metadata token of exception type.
    /// For filter: IL offset of filter expression.
    /// </summary>
    public uint ClassTokenOrFilterOffset;

    /// <summary>IL offset where try block ends (TryOffset + TryLength).</summary>
    public uint TryEndOffset => TryOffset + TryLength;

    /// <summary>IL offset where handler ends (HandlerOffset + HandlerLength).</summary>
    public uint HandlerEndOffset => HandlerOffset + HandlerLength;
}

/// <summary>
/// Collection of exception handling clauses for a method.
/// Uses a fixed-size array to avoid heap allocation.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct ILExceptionClauses
{
    /// <summary>Maximum number of EH clauses per method.</summary>
    public const int MaxClauses = 32;

    /// <summary>Number of clauses parsed.</summary>
    public int Count;

    // Fixed-size storage for clauses
    private fixed byte _clauseData[MaxClauses * 28]; // sizeof(ILExceptionClause) = 28

    /// <summary>Get a clause by index.</summary>
    public ILExceptionClause GetClause(int index)
    {
        if (index < 0 || index >= Count)
            return default;

        fixed (byte* p = _clauseData)
        {
            return ((ILExceptionClause*)p)[index];
        }
    }

    /// <summary>Set a clause by index.</summary>
    public void SetClause(int index, ILExceptionClause clause)
    {
        if (index < 0 || index >= MaxClauses)
            return;

        fixed (byte* p = _clauseData)
        {
            ((ILExceptionClause*)p)[index] = clause;
        }
    }

    /// <summary>Add a clause and return its index.</summary>
    public int AddClause(ILExceptionClause clause)
    {
        if (Count >= MaxClauses)
            return -1;

        SetClause(Count, clause);
        return Count++;
    }
}

/// <summary>
/// JIT exception handling clause with native code offsets.
/// This is the converted form of ILExceptionClause after IL→native offset mapping.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct JITExceptionClause
{
    /// <summary>Exception clause type.</summary>
    public ILExceptionClauseFlags Flags;

    /// <summary>Native code offset where try block starts (from function start).</summary>
    public uint TryStartOffset;

    /// <summary>Native code offset where try block ends (from function start).</summary>
    public uint TryEndOffset;

    /// <summary>Native code offset where handler starts (from function start).</summary>
    public uint HandlerStartOffset;

    /// <summary>Native code offset where handler ends (from function start).</summary>
    public uint HandlerEndOffset;

    /// <summary>
    /// For typed catch: metadata token of exception type.
    /// For filter: native code offset of filter expression.
    /// </summary>
    public uint ClassTokenOrFilterOffset;

    /// <summary>
    /// Native code offset where control should continue after the catch handler's leave.
    /// This is the target of the leave instruction inside the handler.
    /// </summary>
    public uint LeaveTargetOffset;

    /// <summary>True if this clause was successfully converted (all offsets valid).</summary>
    public bool IsValid;

    /// <summary>
    /// For typed catch: resolved MethodTable pointer for the catch type.
    /// Set during IL-to-native conversion when assemblyId is available.
    /// </summary>
    public ulong CatchTypeMethodTable;
}

/// <summary>
/// Collection of JIT exception handling clauses with native offsets.
/// Uses a fixed-size array to avoid heap allocation.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct JITExceptionClauses
{
    /// <summary>Maximum number of EH clauses per method.</summary>
    public const int MaxClauses = 32;

    /// <summary>Number of clauses.</summary>
    public int Count;

    // Fixed-size storage for clauses
    private fixed byte _clauseData[MaxClauses * 40]; // sizeof(JITExceptionClause) = 40 (with CatchTypeMethodTable)

    /// <summary>Get a clause by index.</summary>
    public JITExceptionClause GetClause(int index)
    {
        if (index < 0 || index >= Count)
            return default;

        fixed (byte* p = _clauseData)
        {
            return ((JITExceptionClause*)p)[index];
        }
    }

    /// <summary>Set a clause by index.</summary>
    public void SetClause(int index, JITExceptionClause clause)
    {
        if (index < 0 || index >= MaxClauses)
            return;

        fixed (byte* p = _clauseData)
        {
            ((JITExceptionClause*)p)[index] = clause;
        }
    }

    /// <summary>Add a clause and return its index.</summary>
    public int AddClause(JITExceptionClause clause)
    {
        if (Count >= MaxClauses)
            return -1;

        SetClause(Count, clause);
        return Count++;
    }
}

/// <summary>
/// IL to native offset mapping entry.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct ILToNativeMap
{
    public int ILOffset;
    public int NativeOffset;
}

/// <summary>
/// Delegate for looking up native offsets from IL offsets.
/// </summary>
/// <param name="ilOffset">IL byte offset</param>
/// <returns>Native code offset, or -1 if not found</returns>
public delegate int ILToNativeOffsetLookup(int ilOffset);

/// <summary>
/// Parses IL method body headers and exception handling clauses.
/// </summary>
public static unsafe class ILMethodParser
{
    /// <summary>
    /// Parse the method header at the given RVA and extract the IL code location.
    /// </summary>
    /// <param name="methodRva">Pointer to method body RVA</param>
    /// <param name="ilCode">Output: pointer to IL bytecode</param>
    /// <param name="ilLength">Output: length of IL bytecode</param>
    /// <param name="localVarSigToken">Output: LocalVarSig token (0 if none)</param>
    /// <param name="hasMoreSects">Output: true if EH sections follow</param>
    /// <returns>True if header parsed successfully</returns>
    public static bool ParseMethodHeader(
        byte* methodRva,
        out byte* ilCode,
        out int ilLength,
        out uint localVarSigToken,
        out bool hasMoreSects)
    {
        ilCode = null;
        ilLength = 0;
        localVarSigToken = 0;
        hasMoreSects = false;

        if (methodRva == null)
            return false;

        byte firstByte = *methodRva;
        int headerFormat = firstByte & CorILMethod.FormatMask;

        if (headerFormat == CorILMethod.TinyFormat)
        {
            // Tiny header: 1 byte, code size in upper 6 bits
            // Tiny headers never have EH sections
            ilLength = firstByte >> 2;
            ilCode = methodRva + 1;
            hasMoreSects = false;
            return true;
        }
        else if (headerFormat == CorILMethod.FatFormat)
        {
            // Fat header: 12 bytes
            // Word 0: flags (12 bits) | size (4 bits)
            // Word 1: maxstack (16 bits)
            // DWord 2: code size (32 bits)
            // DWord 3: localvarsig token (32 bits)

            ushort flagsAndSize = *(ushort*)methodRva;
            ushort flags = (ushort)(flagsAndSize & 0x0FFF);
            int headerSize = (flagsAndSize >> 12) * 4; // Size is in DWORDs

            if (headerSize < 12)
                headerSize = 12; // Minimum fat header size

            // ushort maxStack = *(ushort*)(methodRva + 2);
            ilLength = *(int*)(methodRva + 4);
            localVarSigToken = *(uint*)(methodRva + 8);

            ilCode = methodRva + headerSize;
            hasMoreSects = (flags & CorILMethod.MoreSects) != 0;

            return true;
        }

        // Invalid header format
        return false;
    }

    /// <summary>
    /// Parse exception handling clauses from the method body.
    /// Must be called after IL code ends (4-byte aligned).
    /// </summary>
    /// <param name="methodRva">Pointer to method body start</param>
    /// <param name="ilCode">Pointer to IL bytecode</param>
    /// <param name="ilLength">Length of IL bytecode</param>
    /// <param name="clauses">Output: parsed EH clauses</param>
    /// <returns>True if parsed successfully (even if no clauses)</returns>
    public static bool ParseEHClauses(
        byte* methodRva,
        byte* ilCode,
        int ilLength,
        out ILExceptionClauses clauses)
    {
        clauses = default;

        // EH section starts after IL code, 4-byte aligned
        byte* p = ilCode + ilLength;

        // Align to 4-byte boundary
        ulong addr = (ulong)p;
        addr = (addr + 3) & ~3UL;
        p = (byte*)addr;

        // Parse sections until no more MoreSects flag
        bool moreSectsFound = true;
        while (moreSectsFound)
        {
            if (!ParseEHSection(ref p, ref clauses, out moreSectsFound))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Parse a single EH section and advance the pointer.
    /// </summary>
    private static bool ParseEHSection(
        ref byte* p,
        ref ILExceptionClauses clauses,
        out bool moreSects)
    {
        moreSects = false;

        byte kind = *p;

        // Check if this is an EH table
        if ((kind & CorILMethodSect.EHTable) == 0)
        {
            // Not an EH section - skip it
            // For small sections: size is 1 byte
            // For fat sections: size is 3 bytes (little-endian)
            if ((kind & CorILMethodSect.FatFormat) != 0)
            {
                int dataSize = p[1] | (p[2] << 8) | (p[3] << 16);
                p += dataSize;
            }
            else
            {
                int dataSize = p[1];
                p += dataSize;
            }

            moreSects = (kind & CorILMethodSect.MoreSects) != 0;
            return true;
        }

        moreSects = (kind & CorILMethodSect.MoreSects) != 0;

        if ((kind & CorILMethodSect.FatFormat) != 0)
        {
            // Fat section header: 4 bytes
            // Byte 0: Kind
            // Bytes 1-3: DataSize (little-endian, includes header)
            int dataSize = p[1] | (p[2] << 8) | (p[3] << 16);
            int clauseCount = (dataSize - 4) / 24; // Fat clauses are 24 bytes

            p += 4; // Skip header

            for (int i = 0; i < clauseCount && clauses.Count < ILExceptionClauses.MaxClauses; i++)
            {
                ILExceptionClause clause;
                clause.Flags = (ILExceptionClauseFlags)(*(uint*)p);
                clause.TryOffset = *(uint*)(p + 4);
                clause.TryLength = *(uint*)(p + 8);
                clause.HandlerOffset = *(uint*)(p + 12);
                clause.HandlerLength = *(uint*)(p + 16);
                clause.ClassTokenOrFilterOffset = *(uint*)(p + 20);

                clauses.AddClause(clause);
                p += 24;
            }
        }
        else
        {
            // Small section header: 4 bytes
            // Byte 0: Kind
            // Byte 1: DataSize (includes header)
            // Bytes 2-3: Reserved (0)
            int dataSize = p[1];
            int clauseCount = (dataSize - 4) / 12; // Small clauses are 12 bytes

            p += 4; // Skip header

            for (int i = 0; i < clauseCount && clauses.Count < ILExceptionClauses.MaxClauses; i++)
            {
                ILExceptionClause clause;
                clause.Flags = (ILExceptionClauseFlags)(*(ushort*)p);
                clause.TryOffset = *(ushort*)(p + 2);
                clause.TryLength = p[4];
                clause.HandlerOffset = *(ushort*)(p + 5);
                clause.HandlerLength = p[7];
                clause.ClassTokenOrFilterOffset = *(uint*)(p + 8);

                clauses.AddClause(clause);
                p += 12;
            }
        }

        return true;
    }

    /// <summary>
    /// Debug: Print EH clauses to console.
    /// </summary>
    public static void DebugPrintClauses(ref ILExceptionClauses clauses)
    {
        DebugConsole.Write("[EH] ");
        DebugConsole.WriteDecimal(clauses.Count);
        DebugConsole.WriteLine(" clauses:");

        for (int i = 0; i < clauses.Count; i++)
        {
            var c = clauses.GetClause(i);

            DebugConsole.Write("  [");
            DebugConsole.WriteDecimal(i);
            DebugConsole.Write("] ");

            switch (c.Flags)
            {
                case ILExceptionClauseFlags.Exception:
                    DebugConsole.Write("catch token=0x");
                    DebugConsole.WriteHex(c.ClassTokenOrFilterOffset);
                    break;
                case ILExceptionClauseFlags.Filter:
                    DebugConsole.Write("filter @IL_");
                    DebugConsole.WriteHex((uint)c.ClassTokenOrFilterOffset);
                    break;
                case ILExceptionClauseFlags.Finally:
                    DebugConsole.Write("finally");
                    break;
                case ILExceptionClauseFlags.Fault:
                    DebugConsole.Write("fault");
                    break;
                default:
                    DebugConsole.Write("unknown=0x");
                    DebugConsole.WriteHex((uint)c.Flags);
                    break;
            }

            DebugConsole.Write(" try=IL_");
            DebugConsole.WriteHex(c.TryOffset);
            DebugConsole.Write("-IL_");
            DebugConsole.WriteHex(c.TryEndOffset);
            DebugConsole.Write(" handler=IL_");
            DebugConsole.WriteHex(c.HandlerOffset);
            DebugConsole.Write("-IL_");
            DebugConsole.WriteHex(c.HandlerEndOffset);
            DebugConsole.WriteLine("");
        }
    }

    /// <summary>
    /// Debug: Print JIT EH clauses with native offsets to console.
    /// </summary>
    public static void DebugPrintNativeClauses(ref JITExceptionClauses clauses)
    {
        DebugConsole.Write("[JIT EH] ");
        DebugConsole.WriteDecimal(clauses.Count);
        DebugConsole.WriteLine(" native clauses:");

        for (int i = 0; i < clauses.Count; i++)
        {
            var c = clauses.GetClause(i);

            DebugConsole.Write("  [");
            DebugConsole.WriteDecimal(i);
            DebugConsole.Write("] ");

            if (!c.IsValid)
            {
                DebugConsole.WriteLine("INVALID");
                continue;
            }

            switch (c.Flags)
            {
                case ILExceptionClauseFlags.Exception:
                    DebugConsole.Write("catch token=0x");
                    DebugConsole.WriteHex(c.ClassTokenOrFilterOffset);
                    break;
                case ILExceptionClauseFlags.Filter:
                    DebugConsole.Write("filter @0x");
                    DebugConsole.WriteHex(c.ClassTokenOrFilterOffset);
                    break;
                case ILExceptionClauseFlags.Finally:
                    DebugConsole.Write("finally");
                    break;
                case ILExceptionClauseFlags.Fault:
                    DebugConsole.Write("fault");
                    break;
                default:
                    DebugConsole.Write("unknown=0x");
                    DebugConsole.WriteHex((uint)c.Flags);
                    break;
            }

            DebugConsole.Write(" try=0x");
            DebugConsole.WriteHex(c.TryStartOffset);
            DebugConsole.Write("-0x");
            DebugConsole.WriteHex(c.TryEndOffset);
            DebugConsole.Write(" handler=0x");
            DebugConsole.WriteHex(c.HandlerStartOffset);
            DebugConsole.Write("-0x");
            DebugConsole.WriteHex(c.HandlerEndOffset);
            DebugConsole.WriteLine("");
        }
    }
}

/// <summary>
/// Converts IL EH clauses to native EH clauses using an offset lookup function.
/// </summary>
public static unsafe class EHClauseConverter
{
    /// <summary>
    /// Convert IL exception clauses to JIT exception clauses with native offsets.
    /// Uses assemblyId to resolve catch type tokens to MethodTable pointers.
    /// </summary>
    /// <param name="ilClauses">Source IL clauses</param>
    /// <param name="nativeClauses">Output native clauses</param>
    /// <param name="labelILOffsets">Array of IL offsets for recorded labels</param>
    /// <param name="labelNativeOffsets">Array of native offsets for recorded labels</param>
    /// <param name="labelCount">Number of labels</param>
    /// <param name="assemblyId">Assembly ID for resolving type tokens</param>
    /// <returns>True if all clauses converted successfully</returns>
    public static bool ConvertClauses(
        ref ILExceptionClauses ilClauses,
        out JITExceptionClauses nativeClauses,
        int* labelILOffsets,
        int* labelNativeOffsets,
        int labelCount,
        uint assemblyId = 0)
    {
        nativeClauses = default;
        bool allValid = true;

        for (int i = 0; i < ilClauses.Count; i++)
        {
            var ilClause = ilClauses.GetClause(i);

            JITExceptionClause nativeClause;
            nativeClause.Flags = ilClause.Flags;
            nativeClause.CatchTypeMethodTable = 0;  // Initialize to 0

            // Look up native offsets for IL offsets
            int tryStart = FindNativeOffset((int)ilClause.TryOffset, labelILOffsets, labelNativeOffsets, labelCount);
            int tryEnd = FindNativeOffset((int)ilClause.TryEndOffset, labelILOffsets, labelNativeOffsets, labelCount);
            int handlerStart = FindNativeOffset((int)ilClause.HandlerOffset, labelILOffsets, labelNativeOffsets, labelCount);
            int handlerEnd = FindNativeOffset((int)ilClause.HandlerEndOffset, labelILOffsets, labelNativeOffsets, labelCount);

            if (tryStart < 0 || tryEnd < 0 || handlerStart < 0 || handlerEnd < 0)
            {
                // One or more offsets not found - mark as invalid
                nativeClause.TryStartOffset = 0;
                nativeClause.TryEndOffset = 0;
                nativeClause.HandlerStartOffset = 0;
                nativeClause.HandlerEndOffset = 0;
                nativeClause.ClassTokenOrFilterOffset = 0;
                nativeClause.LeaveTargetOffset = 0;
                nativeClause.IsValid = false;
                allValid = false;

                DebugConsole.Write("[EH] Clause ");
                DebugConsole.WriteDecimal(i);
                DebugConsole.Write(" conversion failed: missing IL offset (try=");
                DebugConsole.WriteDecimal((uint)ilClause.TryOffset);
                DebugConsole.Write("-");
                DebugConsole.WriteDecimal((uint)ilClause.TryEndOffset);
                DebugConsole.Write(" handler=");
                DebugConsole.WriteDecimal((uint)ilClause.HandlerOffset);
                DebugConsole.Write("-");
                DebugConsole.WriteDecimal((uint)ilClause.HandlerEndOffset);
                DebugConsole.WriteLine(")");
            }
            else
            {
                nativeClause.TryStartOffset = (uint)tryStart;
                nativeClause.TryEndOffset = (uint)tryEnd;
                nativeClause.HandlerStartOffset = (uint)handlerStart;
                nativeClause.HandlerEndOffset = (uint)handlerEnd;
                nativeClause.LeaveTargetOffset = 0;  // Will be set later in ILCompiler

                // For filter clauses, convert the filter offset too
                if (ilClause.Flags == ILExceptionClauseFlags.Filter)
                {
                    int filterOffset = FindNativeOffset((int)ilClause.ClassTokenOrFilterOffset, labelILOffsets, labelNativeOffsets, labelCount);
                    if (filterOffset < 0)
                    {
                        nativeClause.ClassTokenOrFilterOffset = 0;
                        nativeClause.IsValid = false;
                        allValid = false;
                    }
                    else
                    {
                        nativeClause.ClassTokenOrFilterOffset = (uint)filterOffset;
                        nativeClause.IsValid = true;
                    }
                }
                else if (ilClause.Flags == ILExceptionClauseFlags.Exception)
                {
                    // For typed catch, keep the type token and resolve to MethodTable
                    nativeClause.ClassTokenOrFilterOffset = ilClause.ClassTokenOrFilterOffset;
                    nativeClause.IsValid = true;

                    // Resolve type token to MethodTable pointer if we have assemblyId
                    if (assemblyId != 0 && ilClause.ClassTokenOrFilterOffset != 0)
                    {
                        MethodTable* catchType = AssemblyLoader.ResolveType(assemblyId, ilClause.ClassTokenOrFilterOffset);
                        if (catchType != null)
                        {
                            nativeClause.CatchTypeMethodTable = (ulong)catchType;
                        }
                    }
                }
                else
                {
                    // Finally/Fault clauses
                    nativeClause.ClassTokenOrFilterOffset = ilClause.ClassTokenOrFilterOffset;
                    nativeClause.IsValid = true;
                }
            }

            nativeClauses.AddClause(nativeClause);
        }

        return allValid;
    }

    /// <summary>
    /// Find native offset for an IL offset using binary search (labels are in IL order).
    /// </summary>
    private static int FindNativeOffset(int ilOffset, int* labelILOffsets, int* labelNativeOffsets, int labelCount)
    {
        // Linear search for now (could optimize to binary search if labels are sorted)
        for (int i = 0; i < labelCount; i++)
        {
            if (labelILOffsets[i] == ilOffset)
                return labelNativeOffsets[i];
        }
        return -1;
    }
}
