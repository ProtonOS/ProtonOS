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
            // DebugConsole.Write("[Tier0JIT] Context: ");
            // DebugConsole.WriteDecimal(savedAsmId);
            // DebugConsole.Write(" -> ");
            // DebugConsole.WriteDecimal(assemblyId);
            // DebugConsole.Write(" for 0x");
            // DebugConsole.WriteHex(methodToken);
            // DebugConsole.WriteLine();
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
                // DebugConsole.Write("[Tier0JIT] PInvoke method 0x");
                // DebugConsole.WriteHex(methodToken);
                // DebugConsole.Write(" (asm ");
                // DebugConsole.WriteDecimal(assemblyId);
                // DebugConsole.WriteLine(")");
                var result = ResolvePInvoke(assembly, methodRid, methodToken, assemblyId);
                RestoreContext(savedAsmId);
                return result;
            }

            // Check if this is an abstract/virtual method
            if ((methodFlags & MethodDefFlags.Abstract) != 0)
            {
                // DebugConsole.Write("[Tier0JIT] Abstract method 0x");
                // DebugConsole.WriteHex(methodToken);
                // DebugConsole.Write(" (asm ");
                // DebugConsole.WriteDecimal(assemblyId);
                // DebugConsole.WriteLine(")");
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

        // Debug: dump raw header bytes for ReadAndProgramBar (asm 4, token 0x0600002E)
        if (assemblyId == 4 && methodToken == 0x0600002E)
        {
            // DebugConsole.Write("[MethodHeader] Raw 12 bytes: ");
            // for (int i = 0; i < 12; i++)
            // {
            //     DebugConsole.WriteHex((ulong)methodBodyPtr[i]);
            //     DebugConsole.Write(" ");
            // }
            // DebugConsole.WriteLine();
            // DebugConsole.Write("[MethodHeader] Parsed: IsTiny=");
            // DebugConsole.WriteDecimal(body.IsTiny ? 1 : 0);
            // DebugConsole.Write(" MaxStack=");
            // DebugConsole.WriteDecimal(body.MaxStack);
            // DebugConsole.Write(" CodeSize=0x");
            // DebugConsole.WriteHex(body.CodeSize);
            // DebugConsole.Write(" LocalVarSigToken=0x");
            // DebugConsole.WriteHex(body.LocalVarSigToken);
            // DebugConsole.WriteLine();
        }

        // Parse method signature to get argument count and return type
        byte* sigBlob = MetadataReader.GetBlob(ref assembly->Metadata, sigIdx, out uint sigLen);
        int paramCount = 0;  // Signature's ParamCount (not including 'this')
        ReturnKind returnKind = ReturnKind.Void;
        ushort returnStructSize = 0;
        bool hasThis = false;
        if (sigBlob != null && sigLen > 0)
        {
            // Targeted signature dump for VirtioDevice.Initialize (asm 3, token 0x06000015)
            // if (assemblyId == 3 && methodToken == 0x06000015)
            // {
            //     DebugConsole.Write("[Tier0JIT] Sig dump len=");
            //     DebugConsole.WriteDecimal((int)sigLen);
            //     DebugConsole.Write(": ");
            //     byte* p = sigBlob;
            //     for (int i = 0; i < sigLen && i < 32; i++)
            //     {
            //         DebugConsole.WriteHex((ulong)p[i]);
            //         DebugConsole.Write(" ");
            //     }
            //     DebugConsole.WriteLine();
            // }

            if (SignatureReader.ReadMethodSignature(sigBlob, sigLen, out var methodSig))
            {
                paramCount = (int)methodSig.ParamCount;
                hasThis = methodSig.HasThis;
                returnKind = GetReturnKind(ref methodSig.ReturnType);
            }
            // Parse return type size for struct returns
            if (returnKind == ReturnKind.Struct)
            {
                bool isValueType;
                ParseMethodSigReturnType(sigBlob, sigLen, out isValueType, out returnStructSize);
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

        // DebugConsole.Write("[Tier0JIT] Compiling method 0x");
        // DebugConsole.WriteHex(methodToken);
        // DebugConsole.Write(" (asm ");
        // DebugConsole.WriteDecimal(assemblyId);
        // DebugConsole.Write("): IL=");
        // DebugConsole.WriteDecimal((uint)body.CodeSize);
        // DebugConsole.Write(" bytes, args=");
        // DebugConsole.WriteDecimal((uint)jitArgCount);
        // DebugConsole.Write(" (params=");
        // DebugConsole.WriteDecimal((uint)paramCount);
        // DebugConsole.Write(hasThis ? "+this" : "");
        // DebugConsole.Write("), locals=");
        // DebugConsole.WriteDecimal((uint)localCount);
        // DebugConsole.Write(" localSigTok=0x");
        // DebugConsole.WriteHex(body.LocalVarSigToken);
        // DebugConsole.WriteLine();

        // if (assemblyId == 4 && methodToken == 0x0600002A)
        // {
        //     DebugConsole.Write("[Tier0JIT] IL dump Bind size=");
        //     DebugConsole.WriteDecimal((uint)body.CodeSize);
        //     DebugConsole.WriteLine();
        //     byte* ilPtr = body.ILCode;
        //     int dumpLen = body.CodeSize < 1024 ? (int)body.CodeSize : 1024;
        //     for (int i = 0; i < dumpLen; i++)
        //     {
        //         DebugConsole.WriteHex((ulong)ilPtr[i]);
        //         DebugConsole.Write(" ");
        //         if ((i & 0x0F) == 0x0F)
        //             DebugConsole.WriteLine();
        //     }
        //     if ((dumpLen & 0x0F) != 0)
        //         DebugConsole.WriteLine();
        // }
        // else if (assemblyId == 3 && methodToken == 0x06000006)
        // {
        //     DebugConsole.Write("[Tier0JIT] IL dump VirtioDevice.Initialize size=");
        //     DebugConsole.WriteDecimal((uint)body.CodeSize);
        //     DebugConsole.WriteLine();
        //     byte* ilPtr = body.ILCode;
        //     int dumpLen = body.CodeSize < 512 ? (int)body.CodeSize : 512;
        //     for (int i = 0; i < dumpLen; i++)
        //     {
        //         DebugConsole.WriteHex((ulong)ilPtr[i]);
        //         DebugConsole.Write(" ");
        //         if ((i & 0x0F) == 0x0F)
        //             DebugConsole.WriteLine();
        //     }
        //     if ((dumpLen & 0x0F) != 0)
        //         DebugConsole.WriteLine();
        // }
        // else if (assemblyId == 3 && (methodToken == 0x06000007 || methodToken == 0x06000008))
        // {
        //     DebugConsole.Write("[Tier0JIT] IL dump VirtioDevice.InitializeVariant token=0x");
        //     DebugConsole.WriteHex(methodToken);
        //     DebugConsole.Write(" size=");
        //     DebugConsole.WriteDecimal((uint)body.CodeSize);
        //     DebugConsole.WriteLine();
        //     byte* ilPtr = body.ILCode;
        //     int dumpLen = body.CodeSize < 1024 ? (int)body.CodeSize : 1024;
        //     for (int i = 0; i < dumpLen; i++)
        //     {
        //         DebugConsole.WriteHex((ulong)ilPtr[i]);
        //         DebugConsole.Write(" ");
        //         if ((i & 0x0F) == 0x0F)
        //             DebugConsole.WriteLine();
        //     }
        //     if ((dumpLen & 0x0F) != 0)
        //         DebugConsole.WriteLine();
        // }

        // Fetch method name for targeted debugging
        uint dbgMethodNameIdx = MetadataReader.GetMethodDefName(ref assembly->Tables, ref assembly->Sizes, methodRid);
        byte* dbgMethodName = MetadataReader.GetString(ref assembly->Metadata, dbgMethodNameIdx);

        // if (assemblyId == 3)
        // {
        //     DebugConsole.Write("[Tier0JIT] asm3 method token=0x");
        //     DebugConsole.WriteHex(methodToken);
        //     DebugConsole.Write(" name=");
        //     if (dbgMethodName != null)
        //     {
        //         byte* p = dbgMethodName;
        //         while (p != null && *p != 0)
        //         {
        //             DebugConsole.WriteByte(*p);
        //             p++;
        //         }
        //     }
        //     DebugConsole.WriteLine();
        // }

        if (assemblyId == 3 && NameEquals(dbgMethodName, "Initialize"))
        {
            // DebugConsole.Write("[Tier0JIT] asm3 Initialize token=0x");
            // DebugConsole.WriteHex(methodToken);
            // DebugConsole.Write(" returnKind=");
            // DebugConsole.WriteDecimal((uint)returnKind);
            // DebugConsole.Write(" paramCount=");
            // DebugConsole.WriteDecimal((uint)paramCount);
            // DebugConsole.WriteLine();
        }

        // Targeted debug: inspect VirtioDevice.Initialize (asm 3, token 0x06000015)
        if (assemblyId == 3 && methodToken == 0x06000015)
        {
            // DebugConsole.Write("[Tier0JIT] VirtioDevice.Initialize returnKind=");
            // DebugConsole.WriteDecimal((uint)returnKind);
            // DebugConsole.Write(" structSize=");
            // DebugConsole.WriteDecimal(returnStructSize);
            // DebugConsole.WriteLine();
        }

        // Reserve the method slot BEFORE compilation (prevents infinite recursion)
        // Store paramCount (not including 'this') because CompileNewobj needs
        // to know how many explicit args to pop from the stack.
        var reserved = CompiledMethodRegistry.ReserveForCompilation(
            methodToken, (byte)paramCount, returnKind, returnStructSize, hasThis, assemblyId);
        if (reserved == null)
        {
            // Already being compiled - this is a recursive call
            // The outer compilation will complete eventually
            // DebugConsole.WriteLine("[Tier0JIT] Method already being compiled (recursive)");
            RestoreContext(savedAsmId);
            return JitResult.Fail();
        }
        if (reserved->IsCompiled)
        {
            // Already compiled by a prior call
            // DebugConsole.Write("[Tier0JIT] Already compiled 0x");
            // DebugConsole.WriteHex(methodToken);
            // DebugConsole.Write(" (asm ");
            // DebugConsole.WriteDecimal(assemblyId);
            // DebugConsole.Write(", entry asm ");
            // DebugConsole.WriteDecimal(reserved->AssemblyId);
            // DebugConsole.WriteLine(")");
            RestoreContext(savedAsmId);
            return JitResult.Ok(reserved->NativeCode, 0);
        }

        // Create IL compiler with jitArgCount (includes 'this' for instance methods)
        var compiler = ILCompiler.Create(body.ILCode, (int)body.CodeSize, jitArgCount, localCount);
        compiler.SetDebugContext(assemblyId, methodToken);

        // Wire up resolvers
        MetadataIntegration.WireCompiler(ref compiler);

        // Parse local variable types for value type handling in ldfld
        if (body.LocalVarSigToken != 0 && localCount > 0)
        {
            // Allocate temp buffer for local types and sizes (on stack, small)
            const int MaxLocalTypesOnStack = 64;
            bool* localTypes = stackalloc bool[MaxLocalTypesOnStack];
            ushort* localSizes = stackalloc ushort[MaxLocalTypesOnStack];
            for (int i = 0; i < MaxLocalTypesOnStack; i++)
            {
                localTypes[i] = false;
                localSizes[i] = 0;
            }

            int parsedCount = ParseLocalVarSigTypes(assembly, body.LocalVarSigToken, localTypes, localSizes, MaxLocalTypesOnStack);
            if (parsedCount > 0)
            {
                compiler.SetLocalTypes(localTypes, parsedCount);
                compiler.SetLocalTypeSizes(localSizes, parsedCount);
            }
            else
            {
                // DEBUG: Log when parsing fails
                // DebugConsole.Write("[Tier0JIT] ParseLocalVarSigTypes returned 0 for token 0x");
                // DebugConsole.WriteHex(body.LocalVarSigToken);
                // DebugConsole.WriteLine();
            }
        }

        // Parse argument types for value type handling in ldfld
        if (jitArgCount > 0 && sigBlob != null)
        {
            const int MaxArgTypesOnStack = 32;
            bool* argTypes = stackalloc bool[MaxArgTypesOnStack];
            ushort* argSizes = stackalloc ushort[MaxArgTypesOnStack];
            for (int i = 0; i < MaxArgTypesOnStack; i++)
            {
                argTypes[i] = false;
                argSizes[i] = 0;
            }

            int parsedArgCount = ParseMethodSigArgTypes(sigBlob, sigLen, hasThis, paramCount, argTypes, argSizes, MaxArgTypesOnStack);
            if (parsedArgCount > 0)
            {
                compiler.SetArgTypes(argTypes, parsedArgCount);
                compiler.SetArgTypeSizes(argSizes, parsedArgCount);
            }
        }

        // Parse return type for struct return handling
        if (sigBlob != null)
        {
            bool returnIsValueType = false;
            ushort returnTypeSize = 0;
            ParseMethodSigReturnType(sigBlob, sigLen, out returnIsValueType, out returnTypeSize);
            if (returnIsValueType && returnTypeSize > 0)
            {
                compiler.SetReturnType(true, returnTypeSize);
                if (returnTypeSize > 8)
                {
                    // DebugConsole.Write("[Tier0JIT] Struct return: size=");
                    // DebugConsole.WriteDecimal(returnTypeSize);
                    // DebugConsole.WriteLine();
                }
            }
        }

        // Parse EH clauses if method has exception handlers
        ILExceptionClauses ilClauses = default;
        bool hasEH = false;
        if (body.HasMoreSections)
        {
            if (ILMethodParser.ParseEHClauses(methodBodyPtr, body.ILCode, (int)body.CodeSize, out ilClauses))
            {
                hasEH = ilClauses.Count > 0;
                if (hasEH)
                {
                    compiler.SetILEHClauses(ref ilClauses);
                    DebugConsole.Write("[Tier0JIT] Method 0x");
                    DebugConsole.WriteHex(methodToken);
                    DebugConsole.Write(" has ");
                    DebugConsole.WriteDecimal(ilClauses.Count);
                    DebugConsole.WriteLine(" EH clause(s)");
                }
            }
        }

        // Compile to native code
        void* code;
        JITMethodInfo methodInfo = default;
        JITExceptionClauses nativeClauses = default;

        if (hasEH)
        {
            // Use funclet-based compilation for methods with exception handlers
            code = compiler.CompileWithFunclets(out methodInfo, out nativeClauses);
        }
        else
        {
            // Simple compilation for methods without EH
            code = compiler.Compile();
        }

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

        // For methods without EH, create basic method info for stack unwinding
        // (Methods with EH already have methodInfo from CompileWithFunclets)
        if (!hasEH)
        {
            ulong codeAddr = (ulong)code;
            methodInfo = JITMethodInfo.Create(
                codeAddr,             // codeBase
                codeAddr,             // codeStart
                (uint)codeSize,       // codeSize
                compiler.PrologSize,  // prologSize
                5,                    // frameRegister (RBP)
                0                     // frameOffset
            );

            // Add standard unwind codes for proper stack unwinding
            // This is critical for exception handling to work correctly
            methodInfo.AddStandardUnwindCodes(compiler.StackAdjust);
        }

        // bool isTargetedDump = (assemblyId == 4 && methodToken == 0x0600002A) ||
        //                       (assemblyId == 3 && (methodToken == 0x06000006 || methodToken == 0x06000007 ||
        //                                            methodToken == 0x06000008 || methodToken == 0x06000009));
        //
        // if (isTargetedDump)
        // {
        //     DebugConsole.Write("[Tier0JIT] Code dump 0x");
        //     DebugConsole.WriteHex(methodToken);
        //     DebugConsole.Write(" size=");
        //     DebugConsole.WriteDecimal((uint)codeSize);
        //     DebugConsole.WriteLine();
        //     byte* codeBytes = (byte*)code;
        //     int dumpLen = codeSize < 2048 ? codeSize : 2048;
        //     for (int i = 0; i < dumpLen; i++)
        //     {
        //         DebugConsole.WriteHex((ulong)codeBytes[i]);
        //         DebugConsole.Write(" ");
        //         if ((i & 0x0F) == 0x0F)
        //             DebugConsole.WriteLine();
        //     }
        //     if ((dumpLen & 0x0F) != 0)
        //         DebugConsole.WriteLine();
        // }
        //
        // // Focused dump: MapBars (token 0x06000009) first 256 bytes to inspect prologue
        // if (assemblyId == 3 && methodToken == 0x06000009)
        // {
        //     DebugConsole.Write("[Tier0JIT] Code dump partial 0x06000009 len=");
        //     int partialLen = codeSize < 256 ? codeSize : 256;
        //     DebugConsole.WriteDecimal((uint)partialLen);
        //     DebugConsole.WriteLine();
        //     byte* codeBytes = (byte*)code;
        //     for (int i = 0; i < partialLen; i++)
        //     {
        //         DebugConsole.WriteHex((ulong)codeBytes[i]);
        //         DebugConsole.Write(" ");
        //         if ((i & 0x0F) == 0x0F)
        //             DebugConsole.WriteLine();
        //     }
        //     if ((partialLen & 0x0F) != 0)
        //         DebugConsole.WriteLine();
        // }
        //
        // // Focused dump: Virtqueue.AllocateBuffers (token 0x0600001D) around the failing offset ~0x459
        // if (assemblyId == 3 && methodToken == 0x0600001D)
        // {
        //     DebugConsole.Write("[Tier0JIT] Code dump partial 0x0600001D len=");
        //     int partialLen = codeSize < 1400 ? codeSize : 1400; // cover the failing offset at ~0x459
        //     DebugConsole.WriteDecimal((uint)partialLen);
        //     DebugConsole.WriteLine();
        //     byte* codeBytes = (byte*)code;
        //     for (int i = 0; i < partialLen; i++)
        //     {
        //         DebugConsole.WriteHex((ulong)codeBytes[i]);
        //         DebugConsole.Write(" ");
        //         if ((i & 0x0F) == 0x0F)
        //             DebugConsole.WriteLine();
        //     }
        //     if ((partialLen & 0x0F) != 0)
        //         DebugConsole.WriteLine();
        // }

        bool logCodePtr = (assemblyId == 3) || (assemblyId == 4 && methodToken == 0x0600002A);
        if (logCodePtr)
        {
            // DebugConsole.Write("[Tier0JIT] code ptr token=0x");
            // DebugConsole.WriteHex(methodToken);
            // DebugConsole.Write(" asm=");
            // DebugConsole.WriteDecimal(assemblyId);
            // DebugConsole.Write(" size=");
            // DebugConsole.WriteDecimal((uint)codeSize);
            // DebugConsole.Write(" addr=0x");
            // DebugConsole.WriteHex((ulong)code);
            // DebugConsole.WriteLine();
        }

        // Complete the compilation (sets native code and clears IsBeingCompiled)
        CompiledMethodRegistry.CompleteCompilation(methodToken, code, assemblyId);

        // Register method with exception handling system
        // Methods with EH clauses get full registration (unwind + EH info)
        // Methods without EH clauses still need unwind info for proper stack unwinding
        if (hasEH && nativeClauses.Count > 0)
        {
            if (!JITMethodRegistry.RegisterMethod(ref methodInfo, ref nativeClauses))
            {
                DebugConsole.Write("[Tier0JIT] WARNING: Failed to register method 0x");
                DebugConsole.WriteHex(methodToken);
                DebugConsole.WriteLine(" with EH system");
            }
        }
        else
        {
            // Register for stack unwinding only (no EH clauses)
            // This is needed so exception handling can properly walk the stack
            JITMethodRegistry.RegisterMethodForUnwind(ref methodInfo);
        }

        // If this is a virtual method, populate the vtable slot
        bool isVirtual = (methodFlags & MethodDefFlags.Virtual) != 0;
        bool isStatic = (methodFlags & MethodDefFlags.Static) != 0;
        if (isVirtual && !isStatic)
        {
            PopulateVtableSlot(assembly, methodRid, methodToken, assemblyId, (nint)code, methodFlags);
        }

        // If this is a constructor, set the MethodTable for newobj to use
        if (IsConstructor(dbgMethodName))
        {
            // Find the declaring type and set its MethodTable
            uint declaringTypeToken = AssemblyLoader.FindDeclaringType(assemblyId, methodToken);
            // DebugConsole.Write("[Tier0JIT] .ctor 0x");
            // DebugConsole.WriteHex(methodToken);
            // DebugConsole.Write(" declaring type=0x");
            // DebugConsole.WriteHex(declaringTypeToken);

            if (declaringTypeToken != 0)
            {
                MethodTable* mt = AssemblyLoader.ResolveType(assemblyId, declaringTypeToken);
                // DebugConsole.Write(" MT=0x");
                // DebugConsole.WriteHex((ulong)mt);

                if (mt != null)
                {
                    bool ok = CompiledMethodRegistry.SetMethodTable(methodToken, mt, assemblyId);
                    // DebugConsole.Write(" set=");
                    // DebugConsole.Write(ok ? "OK" : "FAIL");
                }
                // else
                // {
                //     DebugConsole.Write(" (ResolveType failed)");
                // }
            }
            // else
            // {
            //     DebugConsole.Write(" (FindDeclaringType failed)");
            // }
            // DebugConsole.WriteLine();
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
                // DebugConsole.Write("[Tier0JIT] Restore: ");
                // DebugConsole.WriteDecimal(currentAsmId);
                // DebugConsole.Write(" -> ");
                // DebugConsole.WriteDecimal(savedAsmId);
                // DebugConsole.WriteLine();
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
    /// Parse a LocalVarSig token to determine which locals are value types and their sizes.
    /// </summary>
    /// <param name="assembly">Assembly containing the method</param>
    /// <param name="token">LocalVarSig token (0x11xxxxxx)</param>
    /// <param name="isValueType">Output array - true if local is a value type</param>
    /// <param name="typeSize">Output array - size of local's type (for value types)</param>
    /// <param name="maxLocals">Maximum number of locals to process</param>
    /// <returns>Number of locals parsed</returns>
    private static int ParseLocalVarSigTypes(LoadedAssembly* assembly, uint token, bool* isValueType, ushort* typeSize, int maxLocals)
    {
        // LocalVarSig token is a StandAloneSig token (0x11xxxxxx)
        uint tableId = (token >> 24) & 0xFF;
        uint rid = token & 0x00FFFFFF;

        if (tableId != 0x11 || rid == 0 || isValueType == null)
            return 0;

        // Get signature blob from StandAloneSig table
        uint sigIdx = MetadataReader.GetStandAloneSigSignature(ref assembly->Tables, ref assembly->Sizes, rid);
        byte* sigBlob = MetadataReader.GetBlob(ref assembly->Metadata, sigIdx, out uint sigLen);

        if (sigBlob == null || sigLen < 2)
            return 0;

        // LocalVarSig format: LOCAL_SIG (0x07) followed by count, then types
        if (sigBlob[0] != 0x07)
            return 0;

        byte* ptr = sigBlob + 1;
        byte* end = sigBlob + sigLen;

        uint count = MetadataReader.ReadCompressedUInt(ref ptr);
        int numLocals = (int)(count < (uint)maxLocals ? count : (uint)maxLocals);

        // Debug for assembly 4 StandAloneSig table
        // if (assembly->AssemblyId == 4 && rid == 9)
        // {
        //     DebugConsole.Write("[ParseLocal] asm=4 rid=");
        //     DebugConsole.WriteHex(rid);
        //     DebugConsole.Write(" blobIdx=");
        //     DebugConsole.WriteHex(sigIdx);
        //     DebugConsole.Write(" count=");
        //     DebugConsole.WriteHex(count);
        //     DebugConsole.WriteLine();
        //
        //     // Dump all StandAloneSig rows for assembly 4
        //     int numRows = (int)assembly->Tables.RowCounts[0x11];
        //     int rowSz = assembly->Sizes.RowSizes[0x11];
        //     DebugConsole.Write("  StandAloneSig dump: rows=");
        //     DebugConsole.WriteDecimal(numRows);
        //     DebugConsole.Write(" rowSz=");
        //     DebugConsole.WriteDecimal(rowSz);
        //     DebugConsole.WriteLine();
        //     byte* tableBase = assembly->Tables.TableData + assembly->Sizes.TableOffsets[0x11];
        //     for (int r = 0; r < numRows && r < 15; r++)
        //     {
        //         byte* rowPtr = tableBase + r * rowSz;
        //         uint blobVal = rowSz == 2 ? *(ushort*)rowPtr : *(uint*)rowPtr;
        //         DebugConsole.Write("    row ");
        //         DebugConsole.WriteDecimal(r + 1);
        //         DebugConsole.Write(": blobIdx=0x");
        //         DebugConsole.WriteHex(blobVal);
        //         // Read first few bytes of that blob
        //         byte* blobPtr = assembly->Metadata.BlobHeap + blobVal;
        //         DebugConsole.Write(" -> ");
        //         for (int b = 0; b < 5; b++)
        //         {
        //             DebugConsole.WriteHex(blobPtr[b]);
        //             DebugConsole.Write(" ");
        //         }
        //         DebugConsole.WriteLine();
        //     }
        // }

        // Parse each local's type
        for (int i = 0; i < numLocals && ptr < end; i++)
        {
            // Handle PINNED modifier if present
            if (*ptr == 0x45) // ELEMENT_TYPE_PINNED
                ptr++;

            // Handle BYREF modifier if present
            bool isByRef = false;
            if (*ptr == 0x10) // ELEMENT_TYPE_BYREF
            {
                ptr++;
                isByRef = true;
            }

            // Read the element type
            byte elemType = *ptr++;

            // Debug: trace parsing
            // DebugConsole.Write("[ParseLocal] i=");
            // DebugConsole.WriteHex((ulong)i);
            // DebugConsole.Write(" elem=");
            // DebugConsole.WriteHex(elemType);

            // ValueType (0x11) or struct-like types are value types
            // Note: byref to a value type is a pointer, not a value type itself
            if (!isByRef && (elemType == 0x11 || elemType == 0x12)) // ValueType or Class that's actually a struct
            {
                // DebugConsole.Write(" VALUETYPE");
                isValueType[i] = (elemType == 0x11); // Only ValueType is truly a value type
                // Read the TypeDefOrRef token and compute size
                uint typeDefOrRef = MetadataReader.ReadCompressedUInt(ref ptr);
                // Convert TypeDefOrRef encoded token to full token
                // TypeDefOrRef encoding: lower 2 bits = tag, upper bits = RID
                // tag 0 = TypeDef (0x02), tag 1 = TypeRef (0x01), tag 2 = TypeSpec (0x1B)
                uint tag = typeDefOrRef & 0x03;
                uint typeRid = typeDefOrRef >> 2;
                uint fullToken = 0;
                if (tag == 0)
                    fullToken = 0x02000000 | typeRid;  // TypeDef
                else if (tag == 1)
                    fullToken = 0x01000000 | typeRid;  // TypeRef
                else if (tag == 2)
                    fullToken = 0x1B000000 | typeRid;  // TypeSpec

                if (typeSize != null && isValueType[i])
                {
                    uint size = MetadataIntegration.GetTypeSize(fullToken);
                    typeSize[i] = (ushort)size;
                    // Debug: log token and size for value types
                    // DebugConsole.Write(" tok=0x");
                    // DebugConsole.WriteHex(fullToken);
                    // DebugConsole.Write(" sz=");
                    // DebugConsole.WriteDecimal(size);
                }
            }
            else if (!isByRef && (elemType >= 0x02 && elemType <= 0x0D))
            {
                // DebugConsole.Write(" PRIM");
                // Primitive types: Boolean, Char, I1, U1, I2, U2, I4, U4, I8, U8, R4, R8
                // These are value types
                isValueType[i] = true;
                // Set size for primitive types
                if (typeSize != null)
                {
                    switch (elemType)
                    {
                        case 0x02: typeSize[i] = 1; break;  // Boolean
                        case 0x03: typeSize[i] = 2; break;  // Char
                        case 0x04: typeSize[i] = 1; break;  // I1
                        case 0x05: typeSize[i] = 1; break;  // U1
                        case 0x06: typeSize[i] = 2; break;  // I2
                        case 0x07: typeSize[i] = 2; break;  // U2
                        case 0x08: typeSize[i] = 4; break;  // I4
                        case 0x09: typeSize[i] = 4; break;  // U4
                        case 0x0A: typeSize[i] = 8; break;  // I8
                        case 0x0B: typeSize[i] = 8; break;  // U8
                        case 0x0C: typeSize[i] = 4; break;  // R4
                        case 0x0D: typeSize[i] = 8; break;  // R8
                        default: typeSize[i] = 8; break;
                    }
                }
            }
            else if (elemType == 0x1D) // ELEMENT_TYPE_SZARRAY
            {
                // DebugConsole.Write(" SZARRAY");
                // Single-dimension array - skip element type
                isValueType[i] = false;
                typeSize[i] = 8; // Arrays are references (8 bytes on x64)
                // Skip the element type
                if (ptr < end)
                {
                    byte arrElemType = *ptr++;
                    // DebugConsole.Write(" arrElem=");
                    // DebugConsole.WriteHex(arrElemType);
                    if (arrElemType == 0x11 || arrElemType == 0x12) // ValueType or Class
                        MetadataReader.ReadCompressedUInt(ref ptr);
                }
            }
            else if (elemType == 0x15) // ELEMENT_TYPE_GENERICINST
            {
                // Generic instantiation - skip all tokens
                isValueType[i] = false;
                // Skip: CLASS/VALUETYPE marker, TypeDefOrRef, argCount, args...
                if (ptr < end)
                {
                    byte genKind = *ptr++;
                    isValueType[i] = (genKind == 0x11); // ValueType generic
                    if (ptr < end)
                    {
                        MetadataReader.ReadCompressedUInt(ref ptr); // TypeDefOrRef
                        if (ptr < end)
                        {
                            uint argCount = MetadataReader.ReadCompressedUInt(ref ptr);
                            // Skip type arguments (simplified - just skip tokens)
                            for (uint j = 0; j < argCount && ptr < end; j++)
                            {
                                byte argType = *ptr++;
                                if (argType == 0x11 || argType == 0x12)
                                    MetadataReader.ReadCompressedUInt(ref ptr);
                            }
                        }
                    }
                }
            }
            else
            {
                // DebugConsole.Write(" OTHER");
                // Other types (Class, Object, String, SzArray, Ptr, etc.) are reference types
                isValueType[i] = false;
                // Skip any following token if it's a ValueType or Class
                if (elemType == 0x12) // Class
                    MetadataReader.ReadCompressedUInt(ref ptr);
            }
            // DebugConsole.WriteLine(); // End of this local's debug line
        }

        // DebugConsole.WriteLine();
        return numLocals;
    }

    /// <summary>
    /// Parse method signature to determine which arguments are value types and their sizes.
    /// </summary>
    /// <param name="sigBlob">Pointer to signature blob</param>
    /// <param name="sigLen">Length of signature blob</param>
    /// <param name="hasThis">True if method has implicit 'this' parameter</param>
    /// <param name="paramCount">Number of explicit parameters (not including 'this')</param>
    /// <param name="isValueType">Output: true if arg at index is a value type</param>
    /// <param name="typeSizes">Output: size of arg type (0 for non-value types), can be null</param>
    /// <param name="maxArgs">Maximum number of args to process</param>
    /// <returns>Number of args parsed (including 'this' if hasThis)</returns>
    private static int ParseMethodSigArgTypes(byte* sigBlob, uint sigLen, bool hasThis, int paramCount,
                                               bool* isValueType, ushort* typeSizes, int maxArgs)
    {
        if (sigBlob == null || sigLen < 2 || isValueType == null)
            return 0;

        int argIndex = 0;

        // If hasThis, arg0 is 'this' which is always a reference (even for value types, it's a byref)
        if (hasThis && argIndex < maxArgs)
        {
            isValueType[argIndex] = false;
            if (typeSizes != null) typeSizes[argIndex] = 0;
            argIndex++;
        }

        // Parse signature: CallingConv, ParamCount, ReturnType, Params...
        byte* ptr = sigBlob;
        byte* end = sigBlob + sigLen;

        // Skip calling convention
        byte callConv = *ptr++;

        // Skip param count (compressed uint)
        MetadataReader.ReadCompressedUInt(ref ptr);

        // Skip return type
        SkipTypeSig(ref ptr, end);

        // Parse each parameter type
        for (int i = 0; i < paramCount && argIndex < maxArgs && ptr < end; i++)
        {
            bool isVT;
            ushort size;
            GetValueTypeSigWithSize(ref ptr, end, out isVT, out size);
            isValueType[argIndex] = isVT;
            if (typeSizes != null) typeSizes[argIndex] = size;
            argIndex++;
        }

        return argIndex;
    }

    /// <summary>
    /// Parse the return type from a method signature and determine if it's a value type and its size.
    /// </summary>
    private static void ParseMethodSigReturnType(byte* sigBlob, uint sigLen,
                                                  out bool isValueType, out ushort typeSize)
    {
        isValueType = false;
        typeSize = 0;

        if (sigBlob == null || sigLen < 2)
            return;

        byte* ptr = sigBlob;
        byte* end = sigBlob + sigLen;

        // Skip calling convention
        ptr++;

        // Skip param count (compressed uint)
        MetadataReader.ReadCompressedUInt(ref ptr);

        if (ptr >= end)
            return;

        // Now at return type - parse it
        // Handle BYREF modifier - byref is a pointer, not a value type
        if (*ptr == 0x10) // ELEMENT_TYPE_BYREF
        {
            // Return by ref - not a value type for our purposes
            return;
        }

        // Handle VOID
        if (*ptr == 0x01) // ELEMENT_TYPE_VOID
        {
            return;
        }

        byte elemType = *ptr++;

        // ValueType (0x11) - read type token and get size
        if (elemType == 0x11)
        {
            isValueType = true;
            uint typeDefOrRef = MetadataReader.ReadCompressedUInt(ref ptr);
            // Convert TypeDefOrRef encoded token to full token
            uint tag = typeDefOrRef & 0x03;
            uint typeRid = typeDefOrRef >> 2;
            uint fullToken = 0;
            if (tag == 0)
                fullToken = 0x02000000 | typeRid;  // TypeDef
            else if (tag == 1)
                fullToken = 0x01000000 | typeRid;  // TypeRef
            else if (tag == 2)
                fullToken = 0x1B000000 | typeRid;  // TypeSpec

            typeSize = (ushort)MetadataIntegration.GetTypeSize(fullToken);
            return;
        }

        // Primitive types (Boolean through R8) are value types but small (<=8 bytes)
        if (elemType >= 0x02 && elemType <= 0x0D)
        {
            isValueType = true;
            switch (elemType)
            {
                case 0x02: typeSize = 1; break;  // Boolean
                case 0x03: typeSize = 2; break;  // Char
                case 0x04: typeSize = 1; break;  // I1
                case 0x05: typeSize = 1; break;  // U1
                case 0x06: typeSize = 2; break;  // I2
                case 0x07: typeSize = 2; break;  // U2
                case 0x08: typeSize = 4; break;  // I4
                case 0x09: typeSize = 4; break;  // U4
                case 0x0A: typeSize = 8; break;  // I8
                case 0x0B: typeSize = 8; break;  // U8
                case 0x0C: typeSize = 4; break;  // R4
                case 0x0D: typeSize = 8; break;  // R8
                default: typeSize = 8; break;
            }
            return;
        }

        // IntPtr, UIntPtr (native int)
        if (elemType == 0x18 || elemType == 0x19)
        {
            isValueType = true;
            typeSize = 8; // Pointer size on x64
            return;
        }

        // MVAR (0x1E) - method type parameter, resolve from MethodSpec context
        if (elemType == 0x1E)
        {
            uint mvarIndex = MetadataReader.ReadCompressedUInt(ref ptr);
            if (MetadataIntegration.HasMethodTypeArgContext())
            {
                typeSize = MetadataIntegration.GetMethodTypeArgSize((int)mvarIndex);
                // Determine if the substituted type is a value type
                byte substitutedType = MetadataIntegration.GetMethodTypeArgElementType((int)mvarIndex);
                // Primitives (0x02-0x0D), IntPtr/UIntPtr (0x18-0x19), and ValueType (0x11) are value types
                isValueType = (substitutedType >= 0x02 && substitutedType <= 0x0D) ||
                              (substitutedType >= 0x18 && substitutedType <= 0x19) ||
                              substitutedType == 0x11;
            }
            else
            {
                // No context - treat as pointer-sized (conservative fallback)
                typeSize = 8;
                isValueType = false;
            }
            return;
        }

        // Class (0x12), SzArray (0x1D), Object (0x1C), String (0x0E) - not value types
        // isValueType remains false
    }

    /// <summary>
    /// Check if the type at current position is a value type, advancing the pointer.
    /// </summary>
    private static bool IsValueTypeSig(ref byte* ptr, byte* end)
    {
        if (ptr >= end)
            return false;

        // Handle BYREF modifier - byref is a pointer, not a value type
        if (*ptr == 0x10) // ELEMENT_TYPE_BYREF
        {
            ptr++;
            SkipTypeSig(ref ptr, end);
            return false;
        }

        byte elemType = *ptr++;

        // ValueType (0x11) is a value type
        if (elemType == 0x11)
        {
            // Skip TypeDefOrRef token
            MetadataReader.ReadCompressedUInt(ref ptr);
            return true;
        }

        // Primitive types (Boolean through R8) are value types
        if (elemType >= 0x02 && elemType <= 0x0D)
        {
            return true;
        }

        // Class (0x12) - skip token, not a value type
        if (elemType == 0x12)
        {
            MetadataReader.ReadCompressedUInt(ref ptr);
            return false;
        }

        // Generic instantiation
        if (elemType == 0x15) // ELEMENT_TYPE_GENERICINST
        {
            if (ptr < end)
            {
                byte genKind = *ptr++;
                bool isValueTypeGen = (genKind == 0x11);
                // Skip TypeDefOrRef
                if (ptr < end)
                {
                    MetadataReader.ReadCompressedUInt(ref ptr);
                    // Skip type arguments
                    if (ptr < end)
                    {
                        uint argCount = MetadataReader.ReadCompressedUInt(ref ptr);
                        for (uint j = 0; j < argCount && ptr < end; j++)
                        {
                            SkipTypeSig(ref ptr, end);
                        }
                    }
                }
                return isValueTypeGen;
            }
        }

        // SzArray, Object, String, IntPtr, UIntPtr, etc. - not small value types
        // (IntPtr/UIntPtr are technically value types but passed by value as integers)
        if (elemType == 0x18 || elemType == 0x19) // I, U (native int)
            return true;

        // For arrays, skip element type
        if (elemType == 0x1D) // ELEMENT_TYPE_SZARRAY
        {
            SkipTypeSig(ref ptr, end);
            return false;
        }

        return false;
    }

    /// <summary>
    /// Check if the type at current position is a value type, returning both the flag and size.
    /// </summary>
    private static void GetValueTypeSigWithSize(ref byte* ptr, byte* end, out bool isValueType, out ushort typeSize)
    {
        isValueType = false;
        typeSize = 0;

        if (ptr >= end)
            return;

        // Handle BYREF modifier - byref is a pointer, not a value type
        if (*ptr == 0x10) // ELEMENT_TYPE_BYREF
        {
            ptr++;
            SkipTypeSig(ref ptr, end);
            return;
        }

        byte elemType = *ptr++;

        // ValueType (0x11) - read token and get size from metadata
        if (elemType == 0x11)
        {
            isValueType = true;
            uint typeDefOrRef = MetadataReader.ReadCompressedUInt(ref ptr);
            // Convert TypeDefOrRef encoded token to full token
            uint tag = typeDefOrRef & 0x03;
            uint typeRid = typeDefOrRef >> 2;
            uint fullToken = 0;
            if (tag == 0)
                fullToken = 0x02000000 | typeRid;  // TypeDef
            else if (tag == 1)
                fullToken = 0x01000000 | typeRid;  // TypeRef
            else if (tag == 2)
                fullToken = 0x1B000000 | typeRid;  // TypeSpec

            typeSize = (ushort)MetadataIntegration.GetTypeSize(fullToken);
            return;
        }

        // Primitive types (Boolean through R8) are value types
        if (elemType >= 0x02 && elemType <= 0x0D)
        {
            isValueType = true;
            switch (elemType)
            {
                case 0x02: typeSize = 1; break;  // Boolean
                case 0x03: typeSize = 2; break;  // Char
                case 0x04: typeSize = 1; break;  // I1
                case 0x05: typeSize = 1; break;  // U1
                case 0x06: typeSize = 2; break;  // I2
                case 0x07: typeSize = 2; break;  // U2
                case 0x08: typeSize = 4; break;  // I4
                case 0x09: typeSize = 4; break;  // U4
                case 0x0A: typeSize = 8; break;  // I8
                case 0x0B: typeSize = 8; break;  // U8
                case 0x0C: typeSize = 4; break;  // R4
                case 0x0D: typeSize = 8; break;  // R8
                default: typeSize = 8; break;
            }
            return;
        }

        // IntPtr, UIntPtr (native int) - 8 bytes on x64
        if (elemType == 0x18 || elemType == 0x19)
        {
            isValueType = true;
            typeSize = 8;
            return;
        }

        // Class (0x12) - skip token, not a value type
        if (elemType == 0x12)
        {
            MetadataReader.ReadCompressedUInt(ref ptr);
            return;
        }

        // Generic instantiation
        if (elemType == 0x15) // ELEMENT_TYPE_GENERICINST
        {
            if (ptr < end)
            {
                byte genKind = *ptr++;
                isValueType = (genKind == 0x11);
                // Skip TypeDefOrRef
                uint typeDefOrRef = 0;
                if (ptr < end)
                {
                    typeDefOrRef = MetadataReader.ReadCompressedUInt(ref ptr);
                    // Skip type arguments
                    if (ptr < end)
                    {
                        uint argCount = MetadataReader.ReadCompressedUInt(ref ptr);
                        for (uint j = 0; j < argCount && ptr < end; j++)
                        {
                            SkipTypeSig(ref ptr, end);
                        }
                    }
                }
                if (isValueType)
                {
                    // Get size from the base generic type
                    uint tag = typeDefOrRef & 0x03;
                    uint typeRid = typeDefOrRef >> 2;
                    uint fullToken = 0;
                    if (tag == 0)
                        fullToken = 0x02000000 | typeRid;
                    else if (tag == 1)
                        fullToken = 0x01000000 | typeRid;
                    else if (tag == 2)
                        fullToken = 0x1B000000 | typeRid;
                    typeSize = (ushort)MetadataIntegration.GetTypeSize(fullToken);
                }
            }
            return;
        }

        // For arrays, skip element type - not a value type
        if (elemType == 0x1D) // ELEMENT_TYPE_SZARRAY
        {
            SkipTypeSig(ref ptr, end);
            return;
        }

        // MVAR (0x1E) - method type parameter, resolve from MethodSpec context
        if (elemType == 0x1E)
        {
            uint mvarIndex = MetadataReader.ReadCompressedUInt(ref ptr);
            if (MetadataIntegration.HasMethodTypeArgContext())
            {
                typeSize = MetadataIntegration.GetMethodTypeArgSize((int)mvarIndex);
                // Determine if the substituted type is a value type
                byte substitutedType = MetadataIntegration.GetMethodTypeArgElementType((int)mvarIndex);
                // Primitives (0x02-0x0D), IntPtr/UIntPtr (0x18-0x19), and ValueType (0x11) are value types
                isValueType = (substitutedType >= 0x02 && substitutedType <= 0x0D) ||
                              (substitutedType >= 0x18 && substitutedType <= 0x19) ||
                              substitutedType == 0x11;
            }
            else
            {
                // No context - treat as pointer-sized (conservative fallback)
                typeSize = 8;
                isValueType = false;
            }
            return;
        }

        // VAR (0x13) - type parameter from generic type, not currently supported
        // Would need type context from containing generic type
        if (elemType == 0x13)
        {
            MetadataReader.ReadCompressedUInt(ref ptr); // Skip index
            typeSize = 8;
            isValueType = false;
            return;
        }

        // Everything else (STRING, OBJECT, etc.) - not a value type
    }

    /// <summary>
    /// Skip over a type signature without processing it.
    /// </summary>
    private static void SkipTypeSig(ref byte* ptr, byte* end)
    {
        if (ptr >= end)
            return;

        byte elemType = *ptr++;

        // Handle BYREF modifier
        if (elemType == 0x10) // BYREF
        {
            SkipTypeSig(ref ptr, end);
            return;
        }

        // ValueType or Class - skip TypeDefOrRef token
        if (elemType == 0x11 || elemType == 0x12)
        {
            MetadataReader.ReadCompressedUInt(ref ptr);
            return;
        }

        // Generic instantiation
        if (elemType == 0x15)
        {
            if (ptr < end)
            {
                ptr++; // Skip CLASS/VALUETYPE marker
                if (ptr < end)
                {
                    MetadataReader.ReadCompressedUInt(ref ptr); // Skip TypeDefOrRef
                    if (ptr < end)
                    {
                        uint argCount = MetadataReader.ReadCompressedUInt(ref ptr);
                        for (uint j = 0; j < argCount && ptr < end; j++)
                        {
                            SkipTypeSig(ref ptr, end);
                        }
                    }
                }
            }
            return;
        }

        // SzArray - skip element type
        if (elemType == 0x1D)
        {
            SkipTypeSig(ref ptr, end);
            return;
        }

        // MVAR (0x1E) or VAR (0x13) - skip the parameter index
        if (elemType == 0x1E || elemType == 0x13)
        {
            MetadataReader.ReadCompressedUInt(ref ptr);
            return;
        }

        // Ptr (0x0F) - skip the pointed-to type
        if (elemType == 0x0F)
        {
            SkipTypeSig(ref ptr, end);
            return;
        }

        // Primitives, Object, String, etc. - already consumed the byte, nothing more to skip
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

                // DebugConsole.Write("[Tier0JIT] Resolved PInvoke: ");
                // for (int j = 0; importName[j] != 0 && j < 32; j++)
                //     DebugConsole.WriteChar((char)importName[j]);
                // DebugConsole.Write(" -> 0x");
                // DebugConsole.WriteHex((ulong)nativeAddr);
                // DebugConsole.WriteLine();

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
        // For override methods (ReuseSlot), we need to find the base slot by method name.
        // The most common overrides are Object's 3 virtual methods:
        // - slot 0: ToString
        // - slot 1: Equals
        // - slot 2: GetHashCode

        // Get the method name
        uint nameIdx = MetadataReader.GetMethodDefName(ref assembly->Tables, ref assembly->Sizes, targetMethodRid);
        byte* methodName = MetadataReader.GetString(ref assembly->Metadata, nameIdx);

        if (methodName != null)
        {
            // Check for well-known Object override methods
            if (NameEquals(methodName, "ToString"))
                return 0;
            if (NameEquals(methodName, "Equals"))
                return 1;
            if (NameEquals(methodName, "GetHashCode"))
                return 2;
        }

        // For other overrides, fall back to counting virtuals in this type
        // (This isn't correct for deep hierarchies but handles simple cases)
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

    /// <summary>
    /// Populate the vtable slot for a virtual method after JIT compilation.
    /// This updates the declaring type's MethodTable with the compiled method's native code.
    /// </summary>
    private static void PopulateVtableSlot(LoadedAssembly* assembly, uint methodRid, uint methodToken,
                                            uint assemblyId, nint nativeCode, ushort methodFlags)
    {
        // Find the declaring type
        uint typeRow = FindOwningTypeDef(assembly, methodRid);
        if (typeRow == 0)
            return;

        uint typeToken = 0x02000000 | typeRow;

        // Get the type's MethodTable
        MethodTable* mt = AssemblyLoader.ResolveType(assemblyId, typeToken);
        if (mt == null)
            return;

        // Check if MT has vtable slots
        if (mt->_usNumVtableSlots == 0)
            return;

        // Compute the vtable slot for this method
        // For overrides (ReuseSlot), we need to find which base slot is being overridden
        // For new virtual methods (NewSlot), we count new virtuals in this type
        bool isNewSlot = (methodFlags & MethodDefFlags.NewSlot) != 0;

        int vtableSlot;
        if (isNewSlot)
        {
            // New slot - it's positioned after inherited slots
            // Count which new virtual this is in the type
            uint methodStart = MetadataReader.GetTypeDefMethodList(ref assembly->Tables, ref assembly->Sizes, typeRow);
            uint typeDefCount = assembly->Tables.RowCounts[(int)MetadataTableId.TypeDef];
            uint methodEnd = (typeRow < typeDefCount)
                ? MetadataReader.GetTypeDefMethodList(ref assembly->Tables, ref assembly->Sizes, typeRow + 1)
                : assembly->Tables.RowCounts[(int)MetadataTableId.MethodDef] + 1;

            // Get base class vtable slots
            int baseSlots = 0;
            if (mt->_relatedType != null)
            {
                baseSlots = mt->_relatedType->_usNumVtableSlots;
            }
            else
            {
                // Direct Object base - has 3 slots
                baseSlots = 3;
            }

            // Count new virtuals before this method
            int newVirtualIndex = 0;
            for (uint rid = methodStart; rid < methodEnd && rid < methodRid; rid++)
            {
                ushort flags = MetadataReader.GetMethodDefFlags(ref assembly->Tables, ref assembly->Sizes, rid);
                if ((flags & MethodDefFlags.Virtual) != 0 && (flags & MethodDefFlags.NewSlot) != 0)
                {
                    newVirtualIndex++;
                }
            }
            vtableSlot = baseSlots + newVirtualIndex;
        }
        else
        {
            // Override (ReuseSlot) - need to find which base slot we're overriding
            // This requires matching by name/signature to base class
            // For now, use the simple approach: count all virtuals up to this method
            vtableSlot = ComputeVtableSlot(assembly, typeRow, methodRid);
        }

        // Validate slot is within range
        if (vtableSlot < 0 || vtableSlot >= mt->_usNumVtableSlots)
        {
            DebugConsole.Write("[PopulateVT] Slot ");
            DebugConsole.WriteDecimal((uint)vtableSlot);
            DebugConsole.Write(" out of range (max ");
            DebugConsole.WriteDecimal(mt->_usNumVtableSlots);
            DebugConsole.WriteLine(")");
            return;
        }

        // Set the vtable entry
        nint* vtable = mt->GetVtablePtr();
        vtable[vtableSlot] = nativeCode;

        DebugConsole.Write("[PopulateVT] token=0x");
        DebugConsole.WriteHex(methodToken);
        DebugConsole.Write(" type=0x");
        DebugConsole.WriteHex(typeToken);
        DebugConsole.Write(" MT=0x");
        DebugConsole.WriteHex((ulong)mt);
        DebugConsole.Write(" slot=");
        DebugConsole.WriteDecimal((uint)vtableSlot);
        DebugConsole.Write(" code=0x");
        DebugConsole.WriteHex((ulong)nativeCode);
        DebugConsole.WriteLine();
    }

    private static bool NameEquals(byte* name, string expected)
    {
        if (name == null)
            return false;
        for (int i = 0; i < expected.Length; i++)
        {
            if (name[i] != (byte)expected[i])
                return false;
        }
        return name[expected.Length] == 0;
    }
}
