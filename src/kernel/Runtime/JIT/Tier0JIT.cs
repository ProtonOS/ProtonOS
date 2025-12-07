// ProtonOS JIT - Tier 0 JIT Entry Point
// High-level interface for JIT compiling methods from metadata tokens.

using ProtonOS.Platform;
using ProtonOS.Runtime;

namespace ProtonOS.Runtime.JIT;

/// <summary>
/// Result of JIT compilation.
/// </summary>
public unsafe struct JitResult
{
    /// <summary>Pointer to the compiled native code.</summary>
    public void* CodeAddress;

    /// <summary>Size of the compiled code in bytes.</summary>
    public int CodeSize;

    /// <summary>True if compilation succeeded.</summary>
    public bool Success;

    /// <summary>Create a successful result.</summary>
    public static JitResult Ok(void* code, int size) => new JitResult { CodeAddress = code, CodeSize = size, Success = true };

    /// <summary>Create a failed result.</summary>
    public static JitResult Fail() => new JitResult { CodeAddress = null, CodeSize = 0, Success = false };
}

/// <summary>
/// Tier 0 JIT Compiler - compiles methods from metadata tokens.
/// This is the main entry point for JIT compilation.
/// </summary>
public static unsafe class Tier0JIT
{
    /// <summary>
    /// Compile a method given its assembly ID and method token.
    /// </summary>
    /// <param name="assemblyId">The assembly containing the method.</param>
    /// <param name="methodToken">The MethodDef token (0x06xxxxxx).</param>
    /// <returns>JIT compilation result.</returns>
    public static JitResult CompileMethod(uint assemblyId, uint methodToken)
    {
        // Save previous context for nested JIT compilation
        uint savedAsmId = MetadataIntegration.GetCurrentAssemblyId();

        // DEBUG: Show context transition
        if (savedAsmId != assemblyId)
        {
            DebugConsole.Write("[Tier0JIT] Context: ");
            DebugConsole.WriteDecimal(savedAsmId);
            DebugConsole.Write(" -> ");
            DebugConsole.WriteDecimal(assemblyId);
            DebugConsole.Write(" for 0x");
            DebugConsole.WriteHex(methodToken);
            DebugConsole.WriteLine();
        }

        // Set current assembly for metadata resolution (enables lazy JIT compilation)
        MetadataIntegration.SetCurrentAssembly(assemblyId);

        // Get the loaded assembly
        var assembly = AssemblyLoader.GetAssembly(assemblyId);
        if (assembly == null)
        {
            DebugConsole.WriteLine("[Tier0JIT] ERROR: Assembly not found");
            RestoreContext(savedAsmId);
            return JitResult.Fail();
        }

        // Extract method RID (row ID)
        uint methodRid = methodToken & 0x00FFFFFF;
        if (methodRid == 0)
        {
            DebugConsole.WriteLine("[Tier0JIT] ERROR: Invalid method token");
            RestoreContext(savedAsmId);
            return JitResult.Fail();
        }

        // Get method RVA, flags, and signature from metadata
        uint rva = MetadataReader.GetMethodDefRva(ref assembly->Tables, ref assembly->Sizes, methodRid);
        ushort methodFlags = MetadataReader.GetMethodDefFlags(ref assembly->Tables, ref assembly->Sizes, methodRid);
        uint sigIdx = MetadataReader.GetMethodDefSignature(ref assembly->Tables, ref assembly->Sizes, methodRid);

        if (rva == 0)
        {
            // Check if this is a PInvoke method
            if ((methodFlags & MethodDefFlags.PInvokeImpl) != 0)
            {
                DebugConsole.Write("[Tier0JIT] PInvoke method 0x");
                DebugConsole.WriteHex(methodToken);
                DebugConsole.Write(" (asm ");
                DebugConsole.WriteDecimal(assemblyId);
                DebugConsole.WriteLine(")");
                var result = ResolvePInvoke(assembly, methodRid, methodToken, assemblyId);
                RestoreContext(savedAsmId);
                return result;
            }

            // Check if this is an abstract/virtual method
            if ((methodFlags & MethodDefFlags.Abstract) != 0)
            {
                DebugConsole.Write("[Tier0JIT] Abstract method 0x");
                DebugConsole.WriteHex(methodToken);
                DebugConsole.Write(" (asm ");
                DebugConsole.WriteDecimal(assemblyId);
                DebugConsole.WriteLine(")");
                var result = RegisterAbstractMethod(assembly, methodRid, methodToken, assemblyId, methodFlags, sigIdx);
                RestoreContext(savedAsmId);
                return result;
            }

            DebugConsole.Write("[Tier0JIT] ERROR: Method 0x");
            DebugConsole.WriteHex(methodToken);
            DebugConsole.Write(" (asm ");
            DebugConsole.WriteDecimal(assemblyId);
            DebugConsole.Write(") has no IL body, flags=0x");
            DebugConsole.WriteHex(methodFlags);
            DebugConsole.WriteLine(" (extern?)");
            RestoreContext(savedAsmId);
            return JitResult.Fail();
        }

        // Get method body from PE file
        byte* methodBodyPtr = (byte*)PEHelper.RvaToFilePointer(assembly->ImageBase, rva);
        if (methodBodyPtr == null)
        {
            DebugConsole.WriteLine("[Tier0JIT] ERROR: Failed to resolve method RVA");
            RestoreContext(savedAsmId);
            return JitResult.Fail();
        }

        // Parse method body header
        if (!MetadataReader.ReadMethodBody(methodBodyPtr, out var body))
        {
            DebugConsole.WriteLine("[Tier0JIT] ERROR: Failed to parse method body");
            RestoreContext(savedAsmId);
            return JitResult.Fail();
        }

        // Parse method signature to get argument count and return type
        byte* sigBlob = MetadataReader.GetBlob(ref assembly->Metadata, sigIdx, out uint sigLen);
        int paramCount = 0;  // Signature's ParamCount (not including 'this')
        ReturnKind returnKind = ReturnKind.Void;
        bool hasThis = false;
        if (sigBlob != null && sigLen > 0)
        {
            if (SignatureReader.ReadMethodSignature(sigBlob, sigLen, out var methodSig))
            {
                paramCount = (int)methodSig.ParamCount;
                hasThis = methodSig.HasThis;
                returnKind = GetReturnKind(ref methodSig.ReturnType);
            }
        }

        // Total argument count for JIT: includes 'this' for instance methods
        // The signature's ParamCount doesn't include 'this', but the actual call
        // passes 'this' in RCX as arg0. We must include it so that:
        // 1. HomeArguments homes RCX (this) to [RBP+16]
        // 2. ldarg.0 correctly loads 'this' from [RBP+16]
        int jitArgCount = hasThis ? paramCount + 1 : paramCount;

        // Get local variable count from LocalVarSig if present
        int localCount = 0;
        if (body.LocalVarSigToken != 0)
        {
            localCount = ParseLocalVarSigCount(assembly, body.LocalVarSigToken);
        }

        DebugConsole.Write("[Tier0JIT] Compiling method 0x");
        DebugConsole.WriteHex(methodToken);
        DebugConsole.Write(" (asm ");
        DebugConsole.WriteDecimal(assemblyId);
        DebugConsole.Write("): IL=");
        DebugConsole.WriteDecimal((uint)body.CodeSize);
        DebugConsole.Write(" bytes, args=");
        DebugConsole.WriteDecimal((uint)jitArgCount);
        DebugConsole.Write(" (params=");
        DebugConsole.WriteDecimal((uint)paramCount);
        DebugConsole.Write(hasThis ? "+this" : "");
        DebugConsole.Write("), locals=");
        DebugConsole.WriteDecimal((uint)localCount);
        DebugConsole.WriteLine();

        // Reserve the method slot BEFORE compilation (prevents infinite recursion)
        // Store paramCount (not including 'this') because CompileNewobj needs
        // to know how many explicit args to pop from the stack.
        var reserved = CompiledMethodRegistry.ReserveForCompilation(
            methodToken, (byte)paramCount, returnKind, hasThis, assemblyId);
        if (reserved == null)
        {
            // Already being compiled - this is a recursive call
            // The outer compilation will complete eventually
            DebugConsole.WriteLine("[Tier0JIT] Method already being compiled (recursive)");
            RestoreContext(savedAsmId);
            return JitResult.Fail();
        }
        if (reserved->IsCompiled)
        {
            // Already compiled by a prior call
            DebugConsole.Write("[Tier0JIT] Already compiled 0x");
            DebugConsole.WriteHex(methodToken);
            DebugConsole.Write(" (asm ");
            DebugConsole.WriteDecimal(assemblyId);
            DebugConsole.Write(", entry asm ");
            DebugConsole.WriteDecimal(reserved->AssemblyId);
            DebugConsole.WriteLine(")");
            RestoreContext(savedAsmId);
            return JitResult.Ok(reserved->NativeCode, 0);
        }

        // Create IL compiler with jitArgCount (includes 'this' for instance methods)
        var compiler = ILCompiler.Create(body.ILCode, (int)body.CodeSize, jitArgCount, localCount);

        // Wire up resolvers
        MetadataIntegration.WireCompiler(ref compiler);

        // Compile to native code
        void* code = compiler.Compile();

        // Free heap-allocated compiler buffers (reduces memory pressure during nested JIT)
        compiler.FreeBuffers();

        // Restore previous assembly context (for nested JIT calls)
        RestoreContext(savedAsmId);

        if (code == null)
        {
            DebugConsole.WriteLine("[Tier0JIT] ERROR: Compilation failed");
            return JitResult.Fail();
        }

        int codeSize = (int)compiler.CodeSize;

        // Complete the compilation (sets native code and clears IsBeingCompiled)
        CompiledMethodRegistry.CompleteCompilation(methodToken, code, assemblyId);

        // If this is a constructor, set the MethodTable for newobj to use
        uint methodNameIdx = MetadataReader.GetMethodDefName(ref assembly->Tables, ref assembly->Sizes, methodRid);
        byte* methodName = MetadataReader.GetString(ref assembly->Metadata, methodNameIdx);
        if (IsConstructor(methodName))
        {
            // Find the declaring type and set its MethodTable
            uint declaringTypeToken = AssemblyLoader.FindDeclaringType(assemblyId, methodToken);
            DebugConsole.Write("[Tier0JIT] .ctor 0x");
            DebugConsole.WriteHex(methodToken);
            DebugConsole.Write(" declaring type=0x");
            DebugConsole.WriteHex(declaringTypeToken);

            if (declaringTypeToken != 0)
            {
                MethodTable* mt = AssemblyLoader.ResolveType(assemblyId, declaringTypeToken);
                DebugConsole.Write(" MT=0x");
                DebugConsole.WriteHex((ulong)mt);

                if (mt != null)
                {
                    bool ok = CompiledMethodRegistry.SetMethodTable(methodToken, mt, assemblyId);
                    DebugConsole.Write(" set=");
                    DebugConsole.Write(ok ? "OK" : "FAIL");
                }
                else
                {
                    DebugConsole.Write(" (ResolveType failed)");
                }
            }
            else
            {
                DebugConsole.Write(" (FindDeclaringType failed)");
            }
            DebugConsole.WriteLine();
        }

        return JitResult.Ok(code, codeSize);
    }

    /// <summary>
    /// Restore the saved assembly context if it was valid.
    /// </summary>
    private static void RestoreContext(uint savedAsmId)
    {
        if (savedAsmId != 0)
        {
            uint currentAsmId = MetadataIntegration.GetCurrentAssemblyId();
            if (currentAsmId != savedAsmId)
            {
                DebugConsole.Write("[Tier0JIT] Restore: ");
                DebugConsole.WriteDecimal(currentAsmId);
                DebugConsole.Write(" -> ");
                DebugConsole.WriteDecimal(savedAsmId);
                DebugConsole.WriteLine();
            }
            MetadataIntegration.SetCurrentAssembly(savedAsmId);
        }
    }

    /// <summary>
    /// Check if a method name indicates a constructor (.ctor or .cctor).
    /// </summary>
    private static bool IsConstructor(byte* name)
    {
        if (name == null)
            return false;

        // Check for ".ctor" (instance constructor)
        if (name[0] == '.' && name[1] == 'c' && name[2] == 't' && name[3] == 'o' && name[4] == 'r' && name[5] == 0)
            return true;

        // Note: .cctor (static constructor) doesn't need a MethodTable for newobj
        return false;
    }

    /// <summary>
    /// Parse a LocalVarSig token to get the local variable count.
    /// </summary>
    private static int ParseLocalVarSigCount(LoadedAssembly* assembly, uint token)
    {
        // LocalVarSig token is a StandAloneSig token (0x11xxxxxx)
        uint tableId = (token >> 24) & 0xFF;
        uint rid = token & 0x00FFFFFF;

        if (tableId != 0x11 || rid == 0)
            return 0;

        // Get signature blob from StandAloneSig table
        uint sigIdx = MetadataReader.GetStandAloneSigSignature(ref assembly->Tables, ref assembly->Sizes, rid);
        byte* sigBlob = MetadataReader.GetBlob(ref assembly->Metadata, sigIdx, out uint sigLen);

        if (sigBlob == null || sigLen < 2)
            return 0;

        // LocalVarSig format: LOCAL_SIG (0x07) followed by count (compressed uint)
        if (sigBlob[0] != 0x07)
            return 0;

        // Parse compressed count (skip the LOCAL_SIG byte)
        byte* ptr = sigBlob + 1;
        uint count = MetadataReader.ReadCompressedUInt(ref ptr);

        return (int)count;
    }

    /// <summary>
    /// Convert a TypeSig to a ReturnKind for calling convention purposes.
    /// </summary>
    private static ReturnKind GetReturnKind(ref TypeSig typeSig)
    {
        byte elemType = typeSig.ElementType;

        // Void
        if (elemType == ElementType.Void)
            return ReturnKind.Void;

        // 32-bit integers
        if (elemType == ElementType.Boolean ||
            elemType == ElementType.Char ||
            elemType == ElementType.I1 ||
            elemType == ElementType.U1 ||
            elemType == ElementType.I2 ||
            elemType == ElementType.U2 ||
            elemType == ElementType.I4 ||
            elemType == ElementType.U4)
            return ReturnKind.Int32;

        // 64-bit integers
        if (elemType == ElementType.I8 || elemType == ElementType.U8)
            return ReturnKind.Int64;

        // Native pointers
        if (elemType == ElementType.I ||
            elemType == ElementType.U ||
            elemType == ElementType.Ptr ||
            elemType == ElementType.ByRef ||
            elemType == ElementType.FnPtr)
            return ReturnKind.IntPtr;

        // Floating point
        if (elemType == ElementType.R4)
            return ReturnKind.Float32;

        if (elemType == ElementType.R8)
            return ReturnKind.Float64;

        // Reference types (all are pointers)
        if (elemType == ElementType.String ||
            elemType == ElementType.Object ||
            elemType == ElementType.Class ||
            elemType == ElementType.Array ||
            elemType == ElementType.SzArray ||
            elemType == ElementType.GenericInst)
            return ReturnKind.IntPtr;

        // Value types
        if (elemType == ElementType.ValueType)
            return ReturnKind.Struct;

        // Default to IntPtr for unknown types
        return ReturnKind.IntPtr;
    }

    /// <summary>
    /// Resolve a PInvoke method by looking up the kernel export registry.
    /// </summary>
    private static JitResult ResolvePInvoke(LoadedAssembly* assembly, uint methodRid, uint methodToken, uint assemblyId)
    {
        // Find ImplMap entry for this method
        // ImplMap.MemberForwarded is a MemberForwarded coded index: FieldDef (tag=0) or MethodDef (tag=1)
        // For MethodDef, the coded index is (methodRid << 1) | 1
        uint implMapRowCount = assembly->Tables.RowCounts[(int)MetadataTableId.ImplMap];
        uint targetCodedIndex = (methodRid << 1) | 1;  // MethodDef coded index (tag=1)

        for (uint i = 1; i <= implMapRowCount; i++)
        {
            uint memberForwarded = MetadataReader.GetImplMapMemberForwarded(
                ref assembly->Tables, ref assembly->Sizes, i, ref assembly->Tables);

            if (memberForwarded == targetCodedIndex)
            {
                // Found the ImplMap entry for this method
                uint importNameIdx = MetadataReader.GetImplMapImportName(
                    ref assembly->Tables, ref assembly->Sizes, i, ref assembly->Tables);

                // Get the import name string (null-terminated)
                byte* importName = MetadataReader.GetString(
                    ref assembly->Metadata, importNameIdx);

                if (importName == null || *importName == 0)
                {
                    DebugConsole.WriteLine("[Tier0JIT] ERROR: PInvoke has no import name");
                    return JitResult.Fail();
                }

                // Look up in kernel export registry (null-terminated string)
                void* nativeAddr = KernelExportRegistry.Lookup(importName);

                if (nativeAddr == null)
                {
                    DebugConsole.Write("[Tier0JIT] ERROR: PInvoke not found: ");
                    for (int j = 0; importName[j] != 0 && j < 64; j++)
                        DebugConsole.WriteChar((char)importName[j]);
                    DebugConsole.WriteLine();
                    return JitResult.Fail();
                }

                DebugConsole.Write("[Tier0JIT] Resolved PInvoke: ");
                for (int j = 0; importName[j] != 0 && j < 32; j++)
                    DebugConsole.WriteChar((char)importName[j]);
                DebugConsole.Write(" -> 0x");
                DebugConsole.WriteHex((ulong)nativeAddr);
                DebugConsole.WriteLine();

                // Register in CompiledMethodRegistry so it can be found for calls
                // Get signature for argument count and return type
                uint sigIdx = MetadataReader.GetMethodDefSignature(ref assembly->Tables, ref assembly->Sizes, methodRid);
                byte* sigBlob = MetadataReader.GetBlob(ref assembly->Metadata, sigIdx, out uint sigLen);
                int argCount = 0;
                ReturnKind returnKind = ReturnKind.Void;
                bool hasThis = false;
                if (sigBlob != null && sigLen > 0)
                {
                    if (SignatureReader.ReadMethodSignature(sigBlob, sigLen, out var methodSig))
                    {
                        argCount = (int)methodSig.ParamCount;
                        hasThis = methodSig.HasThis;
                        returnKind = GetReturnKind(ref methodSig.ReturnType);
                    }
                }

                // Register as compiled (so the JIT can resolve calls to it)
                // Use the current assembly's ID so lookups with (token, assemblyId) succeed
                CompiledMethodRegistry.RegisterPInvoke(
                    methodToken, nativeAddr, (byte)argCount, returnKind, hasThis, assemblyId);

                return JitResult.Ok(nativeAddr, 0);
            }
        }

        DebugConsole.WriteLine("[Tier0JIT] ERROR: PInvoke method has no ImplMap entry");
        return JitResult.Fail();
    }

    /// <summary>
    /// Register an abstract method with vtable slot information for virtual dispatch.
    /// Abstract methods have no IL body but can be called via callvirt on derived types.
    /// </summary>
    private static JitResult RegisterAbstractMethod(LoadedAssembly* assembly, uint methodRid,
        uint methodToken, uint assemblyId, ushort methodFlags, uint sigIdx)
    {
        // Parse signature for argument count and return type
        byte* sigBlob = MetadataReader.GetBlob(ref assembly->Metadata, sigIdx, out uint sigLen);
        int argCount = 0;
        ReturnKind returnKind = ReturnKind.Void;
        bool hasThis = true;  // Abstract methods are always instance methods
        if (sigBlob != null && sigLen > 0)
        {
            if (SignatureReader.ReadMethodSignature(sigBlob, sigLen, out var methodSig))
            {
                argCount = (int)methodSig.ParamCount;
                hasThis = methodSig.HasThis;
                returnKind = GetReturnKind(ref methodSig.ReturnType);
            }
        }

        // Find the TypeDef that owns this method
        uint owningTypeRow = FindOwningTypeDef(assembly, methodRid);
        if (owningTypeRow == 0)
        {
            DebugConsole.WriteLine("[Tier0JIT] ERROR: Could not find owning type for abstract method");
            return JitResult.Fail();
        }

        // Compute the vtable slot for this method within the declaring type
        int vtableSlot = ComputeVtableSlot(assembly, owningTypeRow, methodRid);

        DebugConsole.Write("[Tier0JIT] Abstract method vtable slot=");
        DebugConsole.WriteDecimal((uint)vtableSlot);
        DebugConsole.WriteLine();

        // Register as a virtual method (no native code - dispatch is via vtable at runtime)
        CompiledMethodRegistry.RegisterVirtual(
            methodToken, null, (byte)argCount, returnKind, hasThis, true, vtableSlot, assemblyId);

        // Return success with null code - callvirt will use vtable dispatch
        return JitResult.Ok(null, 0);
    }

    /// <summary>
    /// Find the TypeDef row that owns a given MethodDef row.
    /// </summary>
    private static uint FindOwningTypeDef(LoadedAssembly* assembly, uint methodRid)
    {
        uint typeDefCount = assembly->Tables.RowCounts[(int)MetadataTableId.TypeDef];

        for (uint typeRow = 1; typeRow <= typeDefCount; typeRow++)
        {
            uint methodStart = MetadataReader.GetTypeDefMethodList(ref assembly->Tables, ref assembly->Sizes, typeRow);
            uint methodEnd;

            if (typeRow < typeDefCount)
                methodEnd = MetadataReader.GetTypeDefMethodList(ref assembly->Tables, ref assembly->Sizes, typeRow + 1);
            else
                methodEnd = assembly->Tables.RowCounts[(int)MetadataTableId.MethodDef] + 1;

            // Check if methodRid falls within this type's method range
            if (methodRid >= methodStart && methodRid < methodEnd)
                return typeRow;
        }

        return 0;  // Not found
    }

    /// <summary>
    /// Compute the vtable slot for a method within its declaring type.
    /// Counts virtual methods from the type's first method up to the target method.
    /// </summary>
    private static int ComputeVtableSlot(LoadedAssembly* assembly, uint typeRow, uint targetMethodRid)
    {
        // Get the method range for this type
        uint methodStart = MetadataReader.GetTypeDefMethodList(ref assembly->Tables, ref assembly->Sizes, typeRow);
        uint methodEnd;
        uint typeDefCount = assembly->Tables.RowCounts[(int)MetadataTableId.TypeDef];

        if (typeRow < typeDefCount)
            methodEnd = MetadataReader.GetTypeDefMethodList(ref assembly->Tables, ref assembly->Sizes, typeRow + 1);
        else
            methodEnd = assembly->Tables.RowCounts[(int)MetadataTableId.MethodDef] + 1;

        // Count virtual methods (including the target) to determine slot
        // Vtable slots are assigned in order of virtual method declaration
        int virtualSlot = 0;

        for (uint rid = methodStart; rid < methodEnd; rid++)
        {
            ushort flags = MetadataReader.GetMethodDefFlags(ref assembly->Tables, ref assembly->Sizes, rid);

            // Check if this is a virtual method (Virtual flag set)
            if ((flags & MethodDefFlags.Virtual) != 0)
            {
                if (rid == targetMethodRid)
                    return virtualSlot;
                virtualSlot++;
            }
        }

        // If we get here, the method wasn't found - return slot 0 as fallback
        return 0;
    }
}
