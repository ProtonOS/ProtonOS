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
        // Set current assembly for metadata resolution (enables lazy JIT compilation)
        MetadataIntegration.SetCurrentAssembly(assemblyId);

        // Get the loaded assembly
        var assembly = AssemblyLoader.GetAssembly(assemblyId);
        if (assembly == null)
        {
            DebugConsole.WriteLine("[Tier0JIT] ERROR: Assembly not found");
            return JitResult.Fail();
        }

        // Extract method RID (row ID)
        uint methodRid = methodToken & 0x00FFFFFF;
        if (methodRid == 0)
        {
            DebugConsole.WriteLine("[Tier0JIT] ERROR: Invalid method token");
            return JitResult.Fail();
        }

        // Get method RVA and signature from metadata
        uint rva = MetadataReader.GetMethodDefRva(ref assembly->Tables, ref assembly->Sizes, methodRid);
        uint sigIdx = MetadataReader.GetMethodDefSignature(ref assembly->Tables, ref assembly->Sizes, methodRid);

        if (rva == 0)
        {
            DebugConsole.WriteLine("[Tier0JIT] ERROR: Method has no IL body (abstract/extern?)");
            return JitResult.Fail();
        }

        // Get method body from PE file
        byte* methodBodyPtr = (byte*)PEHelper.RvaToFilePointer(assembly->ImageBase, rva);
        if (methodBodyPtr == null)
        {
            DebugConsole.WriteLine("[Tier0JIT] ERROR: Failed to resolve method RVA");
            return JitResult.Fail();
        }

        // Parse method body header
        if (!MetadataReader.ReadMethodBody(methodBodyPtr, out var body))
        {
            DebugConsole.WriteLine("[Tier0JIT] ERROR: Failed to parse method body");
            return JitResult.Fail();
        }

        // Parse method signature to get argument count and return type
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

        // Get local variable count from LocalVarSig if present
        int localCount = 0;
        if (body.LocalVarSigToken != 0)
        {
            localCount = ParseLocalVarSigCount(assembly, body.LocalVarSigToken);
        }

        DebugConsole.Write("[Tier0JIT] Compiling method: IL=");
        DebugConsole.WriteDecimal((uint)body.CodeSize);
        DebugConsole.Write(" bytes, args=");
        DebugConsole.WriteDecimal((uint)argCount);
        DebugConsole.Write(", locals=");
        DebugConsole.WriteDecimal((uint)localCount);
        DebugConsole.WriteLine();

        // Reserve the method slot BEFORE compilation (prevents infinite recursion)
        var reserved = CompiledMethodRegistry.ReserveForCompilation(
            methodToken, (byte)argCount, returnKind, hasThis);
        if (reserved == null)
        {
            // Already being compiled - this is a recursive call
            // The outer compilation will complete eventually
            DebugConsole.WriteLine("[Tier0JIT] Method already being compiled (recursive)");
            return JitResult.Fail();
        }
        if (reserved->IsCompiled)
        {
            // Already compiled by a prior call
            return JitResult.Ok(reserved->NativeCode, 0);
        }

        // Create IL compiler
        var compiler = ILCompiler.Create(body.ILCode, (int)body.CodeSize, argCount, localCount);

        // Wire up resolvers
        MetadataIntegration.WireCompiler(ref compiler);

        // Compile to native code
        void* code = compiler.Compile();

        if (code == null)
        {
            DebugConsole.WriteLine("[Tier0JIT] ERROR: Compilation failed");
            return JitResult.Fail();
        }

        int codeSize = (int)compiler.CodeSize;

        // Complete the compilation (sets native code and clears IsBeingCompiled)
        CompiledMethodRegistry.CompleteCompilation(methodToken, code);

        return JitResult.Ok(code, codeSize);
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
}
