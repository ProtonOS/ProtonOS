// ProtonOS kernel - Reflection Runtime
// Provides kernel-side support for reflection operations.
// Exports functions that korlib can call via DllImport("*").

using System.Runtime;
using ProtonOS.Memory;
using ProtonOS.Platform;
using ProtonOS.Runtime.JIT;

namespace ProtonOS.Runtime.Reflection;

/// <summary>
/// Information about a type for reflection.
/// </summary>
public unsafe struct TypeReflectionInfo
{
    public uint AssemblyId;
    public uint TypeDefToken;
    public MethodTable* MT;
    public bool IsUsed => MT != null;
}

/// <summary>
/// Kernel-side reflection support.
/// Provides method invocation, field access, and metadata queries.
/// </summary>
public static unsafe class ReflectionRuntime
{
    private static bool _initialized;

    // Reverse lookup: MethodTable* -> TypeReflectionInfo
    private const int MaxTypeInfoEntries = 512;
    private static TypeReflectionInfo* _typeInfoRegistry;
    private static int _typeInfoCount;

    /// <summary>
    /// Initialize the reflection runtime.
    /// </summary>
    public static void Init()
    {
        if (_initialized)
            return;

        // Allocate the type info registry
        _typeInfoRegistry = (TypeReflectionInfo*)HeapAllocator.AllocZeroed(
            (ulong)(MaxTypeInfoEntries * sizeof(TypeReflectionInfo)));
        if (_typeInfoRegistry == null)
        {
            DebugConsole.WriteLine("[ReflectionRuntime] Failed to allocate type info registry");
            return;
        }
        _typeInfoCount = 0;

        DebugConsole.WriteLine("[ReflectionRuntime] Initialized");
        _initialized = true;
    }

    /// <summary>
    /// Register a type's reflection info (MethodTable → assembly/token mapping).
    /// Call this when types are resolved by the JIT.
    /// </summary>
    public static void RegisterTypeInfo(uint assemblyId, uint typeDefToken, MethodTable* mt)
    {
        if (!_initialized || mt == null)
            return;

        // Check if already registered
        for (int i = 0; i < _typeInfoCount; i++)
        {
            if (_typeInfoRegistry[i].MT == mt)
            {
                // Update existing
                _typeInfoRegistry[i].AssemblyId = assemblyId;
                _typeInfoRegistry[i].TypeDefToken = typeDefToken;
                return;
            }
        }

        // Add new entry
        if (_typeInfoCount >= MaxTypeInfoEntries)
        {
            DebugConsole.WriteLine("[ReflectionRuntime] Type info registry full");
            return;
        }

        _typeInfoRegistry[_typeInfoCount].AssemblyId = assemblyId;
        _typeInfoRegistry[_typeInfoCount].TypeDefToken = typeDefToken;
        _typeInfoRegistry[_typeInfoCount].MT = mt;
        _typeInfoCount++;
    }

    /// <summary>
    /// Look up type info by MethodTable pointer.
    /// </summary>
    public static bool LookupTypeInfo(MethodTable* mt, out uint assemblyId, out uint typeDefToken)
    {
        assemblyId = 0;
        typeDefToken = 0;

        if (!_initialized || mt == null)
            return false;

        for (int i = 0; i < _typeInfoCount; i++)
        {
            if (_typeInfoRegistry[i].MT == mt)
            {
                assemblyId = _typeInfoRegistry[i].AssemblyId;
                typeDefToken = _typeInfoRegistry[i].TypeDefToken;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Get type info from a MethodTable pointer.
    /// Returns assembly ID and type token, or 0 if not found.
    /// </summary>
    [RuntimeExport("PalGetTypeInfo")]
    public static void GetTypeInfo(void* methodTable, uint* outAssemblyId, uint* outTypeDefToken)
    {
        if (outAssemblyId != null) *outAssemblyId = 0;
        if (outTypeDefToken != null) *outTypeDefToken = 0;

        if (methodTable == null)
            return;

        if (LookupTypeInfo((MethodTable*)methodTable, out uint asmId, out uint token))
        {
            if (outAssemblyId != null) *outAssemblyId = asmId;
            if (outTypeDefToken != null) *outTypeDefToken = token;
        }
    }

    /// <summary>
    /// Invoke a method by its token.
    /// </summary>
    /// <param name="methodToken">The MethodDef token (0x06xxxxxx).</param>
    /// <param name="target">The target object (null for static methods).</param>
    /// <param name="args">Array of arguments.</param>
    /// <returns>The return value, or null for void methods.</returns>
    [RuntimeExport("PalInvokeMethod")]
    public static object? InvokeMethod(uint methodToken, object? target, object?[]? args)
    {
        // Look up the compiled method
        CompiledMethodInfo* methodInfo = CompiledMethodRegistry.Lookup(methodToken);
        if (methodInfo == null || !methodInfo->IsCompiled)
        {
            // Method not found or not compiled
            DebugConsole.Write("[Reflection] Method not found: 0x");
            DebugConsole.WriteHex(methodToken);
            DebugConsole.WriteLine();
            return null;
        }

        void* nativeCode = methodInfo->NativeCode;
        if (nativeCode == null)
        {
            DebugConsole.WriteLine("[Reflection] Method has no native code");
            return null;
        }

        // Get argument count and check
        int expectedArgs = methodInfo->ArgCount;
        int providedArgs = args?.Length ?? 0;

        if (expectedArgs != providedArgs)
        {
            DebugConsole.Write("[Reflection] Arg count mismatch: expected ");
            DebugConsole.WriteDecimal((uint)expectedArgs);
            DebugConsole.Write(", got ");
            DebugConsole.WriteDecimal((uint)providedArgs);
            DebugConsole.WriteLine();
            return null;
        }

        // Invoke based on return type and argument count
        return InvokeNative(nativeCode, methodInfo, target, args);
    }

    /// <summary>
    /// Get field value using field token and offset.
    /// NOTE: Full boxing support requires RuntimeExports which we don't have in minimal runtime.
    /// This is a stub - real field access should be done via JIT thunks.
    /// </summary>
    [RuntimeExport("PalGetFieldValue")]
    public static object? GetFieldValue(uint fieldToken, object? target, int fieldOffset, int fieldSize, bool isValueType)
    {
        // In minimal runtime without boxing support, we can only handle reference types
        if (target == null && fieldOffset >= 0)
        {
            return null;
        }

        if (!isValueType)
        {
            byte* objPtr;
            if (target != null)
            {
                objPtr = (byte*)System.Runtime.CompilerServices.Unsafe.AsPointer(ref target);
                objPtr += sizeof(void*); // Skip MethodTable*
            }
            else
            {
                objPtr = (byte*)(nint)fieldOffset;
                fieldOffset = 0;
            }
            byte* fieldPtr = objPtr + fieldOffset;
            return *(object*)fieldPtr;
        }

        // Value types require boxing - not supported in minimal runtime
        DebugConsole.WriteLine("[Reflection] GetFieldValue for value types not supported");
        return null;
    }

    /// <summary>
    /// Set field value using field token and offset.
    /// NOTE: Full unboxing support requires RuntimeExports which we don't have in minimal runtime.
    /// This is a stub - real field access should be done via JIT thunks.
    /// </summary>
    [RuntimeExport("PalSetFieldValue")]
    public static void SetFieldValue(uint fieldToken, object? target, int fieldOffset, int fieldSize, bool isValueType, object? value)
    {
        if (target == null && fieldOffset >= 0)
        {
            return;
        }

        if (!isValueType && value != null)
        {
            byte* objPtr;
            if (target != null)
            {
                objPtr = (byte*)System.Runtime.CompilerServices.Unsafe.AsPointer(ref target);
                objPtr += sizeof(void*); // Skip MethodTable*
            }
            else
            {
                objPtr = (byte*)(nint)fieldOffset;
                fieldOffset = 0;
            }
            byte* fieldPtr = objPtr + fieldOffset;
            *(object*)fieldPtr = value;
            return;
        }

        // Value types require unboxing - not supported in minimal runtime
        DebugConsole.WriteLine("[Reflection] SetFieldValue for value types not supported");
    }

    /// <summary>
    /// Get method name from token (returns pointer to null-terminated UTF-8 string).
    /// </summary>
    [RuntimeExport("PalGetMethodName")]
    public static byte* GetMethodName(uint assemblyId, uint methodToken)
    {
        LoadedAssembly* asm = AssemblyLoader.GetAssembly(assemblyId);
        if (asm == null)
            return null;

        uint rowId = methodToken & 0x00FFFFFF;
        uint nameIdx = MetadataReader.GetMethodDefName(ref asm->Tables, ref asm->Sizes, rowId);
        return MetadataReader.GetString(ref asm->Metadata, nameIdx);
    }

    /// <summary>
    /// Get method count for a type.
    /// </summary>
    [RuntimeExport("PalGetMethodCount")]
    public static uint GetMethodCount(uint assemblyId, uint typeDefToken)
    {
        LoadedAssembly* asm = AssemblyLoader.GetAssembly(assemblyId);
        if (asm == null)
            return 0;

        uint typeRowId = typeDefToken & 0x00FFFFFF;
        MetadataReader.GetTypeDefMethods(ref asm->Tables, ref asm->Sizes, typeRowId,
            out uint firstMethod, out uint methodCount);
        return methodCount;
    }

    /// <summary>
    /// Get method token at index for a type.
    /// </summary>
    [RuntimeExport("PalGetMethodToken")]
    public static uint GetMethodToken(uint assemblyId, uint typeDefToken, uint index)
    {
        LoadedAssembly* asm = AssemblyLoader.GetAssembly(assemblyId);
        if (asm == null)
            return 0;

        uint typeRowId = typeDefToken & 0x00FFFFFF;
        MetadataReader.GetTypeDefMethods(ref asm->Tables, ref asm->Sizes, typeRowId,
            out uint firstMethod, out uint methodCount);

        if (index >= methodCount)
            return 0;

        return 0x06000000 | (firstMethod + index);
    }

    /// <summary>
    /// Get field count for a type.
    /// </summary>
    [RuntimeExport("PalGetFieldCount")]
    public static uint GetFieldCount(uint assemblyId, uint typeDefToken)
    {
        LoadedAssembly* asm = AssemblyLoader.GetAssembly(assemblyId);
        if (asm == null)
            return 0;

        uint typeRowId = typeDefToken & 0x00FFFFFF;
        MetadataReader.GetTypeDefFields(ref asm->Tables, ref asm->Sizes, typeRowId,
            out uint firstField, out uint fieldCount);
        return fieldCount;
    }

    /// <summary>
    /// Get field token at index for a type.
    /// </summary>
    [RuntimeExport("PalGetFieldToken")]
    public static uint GetFieldToken(uint assemblyId, uint typeDefToken, uint index)
    {
        LoadedAssembly* asm = AssemblyLoader.GetAssembly(assemblyId);
        if (asm == null)
            return 0;

        uint typeRowId = typeDefToken & 0x00FFFFFF;
        MetadataReader.GetTypeDefFields(ref asm->Tables, ref asm->Sizes, typeRowId,
            out uint firstField, out uint fieldCount);

        if (index >= fieldCount)
            return 0;

        return 0x04000000 | (firstField + index);
    }

    /// <summary>
    /// Get field name from token.
    /// </summary>
    [RuntimeExport("PalGetFieldName")]
    public static byte* GetFieldName(uint assemblyId, uint fieldToken)
    {
        LoadedAssembly* asm = AssemblyLoader.GetAssembly(assemblyId);
        if (asm == null)
            return null;

        uint rowId = fieldToken & 0x00FFFFFF;
        uint nameIdx = MetadataReader.GetFieldName(ref asm->Tables, ref asm->Sizes, rowId);
        return MetadataReader.GetString(ref asm->Metadata, nameIdx);
    }

    /// <summary>
    /// Get type name from TypeDef token.
    /// </summary>
    [RuntimeExport("PalGetTypeName")]
    public static byte* GetTypeName(uint assemblyId, uint typeDefToken)
    {
        LoadedAssembly* asm = AssemblyLoader.GetAssembly(assemblyId);
        if (asm == null)
            return null;

        uint rowId = typeDefToken & 0x00FFFFFF;
        uint nameIdx = MetadataReader.GetTypeDefName(ref asm->Tables, ref asm->Sizes, rowId);
        return MetadataReader.GetString(ref asm->Metadata, nameIdx);
    }

    /// <summary>
    /// Get type namespace from TypeDef token.
    /// </summary>
    [RuntimeExport("PalGetTypeNamespace")]
    public static byte* GetTypeNamespace(uint assemblyId, uint typeDefToken)
    {
        LoadedAssembly* asm = AssemblyLoader.GetAssembly(assemblyId);
        if (asm == null)
            return null;

        uint rowId = typeDefToken & 0x00FFFFFF;
        uint nsIdx = MetadataReader.GetTypeDefNamespace(ref asm->Tables, ref asm->Sizes, rowId);
        return MetadataReader.GetString(ref asm->Metadata, nsIdx);
    }

    /// <summary>
    /// Check if a method is static.
    /// </summary>
    [RuntimeExport("PalIsMethodStatic")]
    public static bool IsMethodStatic(uint assemblyId, uint methodToken)
    {
        LoadedAssembly* asm = AssemblyLoader.GetAssembly(assemblyId);
        if (asm == null)
            return false;

        uint rowId = methodToken & 0x00FFFFFF;
        return MetadataReader.IsMethodDefStatic(ref asm->Tables, ref asm->Sizes, rowId);
    }

    /// <summary>
    /// Check if a method is virtual.
    /// </summary>
    [RuntimeExport("PalIsMethodVirtual")]
    public static bool IsMethodVirtual(uint assemblyId, uint methodToken)
    {
        LoadedAssembly* asm = AssemblyLoader.GetAssembly(assemblyId);
        if (asm == null)
            return false;

        uint rowId = methodToken & 0x00FFFFFF;
        return MetadataReader.IsMethodDefVirtual(ref asm->Tables, ref asm->Sizes, rowId);
    }

    /// <summary>
    /// Native invocation dispatcher.
    /// </summary>
    private static object? InvokeNative(void* nativeCode, CompiledMethodInfo* methodInfo, object? target, object?[]? args)
    {
        // Simple implementation for common cases
        // In a full implementation, we'd need to handle all combinations of argument types

        bool hasThis = methodInfo->HasThis;
        int argCount = methodInfo->ArgCount;
        ReturnKind returnKind = methodInfo->ReturnKind;

        // For now, support methods with 0-3 arguments returning void, int, or object
        if (argCount == 0 && !hasThis)
        {
            // Static, no args
            return returnKind switch
            {
                ReturnKind.Void => InvokeVoidNoArgs(nativeCode),
                ReturnKind.Int32 => InvokeInt32NoArgs(nativeCode),
                ReturnKind.IntPtr => InvokeObjectNoArgs(nativeCode),
                _ => null
            };
        }
        else if (argCount == 0 && hasThis)
        {
            // Instance, no args (just 'this')
            return returnKind switch
            {
                ReturnKind.Void => InvokeVoidThis(nativeCode, target),
                ReturnKind.Int32 => InvokeInt32This(nativeCode, target),
                ReturnKind.IntPtr => InvokeObjectThis(nativeCode, target),
                _ => null
            };
        }
        else if (argCount == 1 && !hasThis && args != null)
        {
            // Static, 1 arg
            return returnKind switch
            {
                ReturnKind.Void => InvokeVoid1Arg(nativeCode, args[0]),
                ReturnKind.Int32 => InvokeInt321Arg(nativeCode, args[0]),
                ReturnKind.IntPtr => InvokeObject1Arg(nativeCode, args[0]),
                _ => null
            };
        }
        else if (argCount == 1 && hasThis && args != null)
        {
            // Instance, 1 arg
            return returnKind switch
            {
                ReturnKind.Void => InvokeVoidThis1Arg(nativeCode, target, args[0]),
                ReturnKind.Int32 => InvokeInt32This1Arg(nativeCode, target, args[0]),
                ReturnKind.IntPtr => InvokeObjectThis1Arg(nativeCode, target, args[0]),
                _ => null
            };
        }

        DebugConsole.WriteLine("[Reflection] Unsupported method signature for invoke");
        return null;
    }

    // Invocation helpers - void return, static
    private static object? InvokeVoidNoArgs(void* code)
    {
        ((delegate*<void>)code)();
        return null;
    }

    private static object? InvokeInt32NoArgs(void* code)
    {
        // Boxing int to object requires RuntimeExports - not supported in minimal runtime
        ((delegate*<int>)code)();  // Call method but discard result
        DebugConsole.WriteLine("[Reflection] Int32 return boxing not supported");
        return null;
    }

    private static object? InvokeObjectNoArgs(void* code)
    {
        return ((delegate*<object?>)code)();
    }

    // Invocation helpers - void return, instance
    private static object? InvokeVoidThis(void* code, object? target)
    {
        ((delegate*<object?, void>)code)(target);
        return null;
    }

    private static object? InvokeInt32This(void* code, object? target)
    {
        ((delegate*<object?, int>)code)(target);
        DebugConsole.WriteLine("[Reflection] Int32 return boxing not supported");
        return null;
    }

    private static object? InvokeObjectThis(void* code, object? target)
    {
        return ((delegate*<object?, object?>)code)(target);
    }

    // Invocation helpers - 1 arg, static
    private static object? InvokeVoid1Arg(void* code, object? arg)
    {
        ((delegate*<object?, void>)code)(arg);
        return null;
    }

    private static object? InvokeInt321Arg(void* code, object? arg)
    {
        ((delegate*<object?, int>)code)(arg);
        DebugConsole.WriteLine("[Reflection] Int32 return boxing not supported");
        return null;
    }

    private static object? InvokeObject1Arg(void* code, object? arg)
    {
        return ((delegate*<object?, object?>)code)(arg);
    }

    // Invocation helpers - 1 arg, instance
    private static object? InvokeVoidThis1Arg(void* code, object? target, object? arg)
    {
        ((delegate*<object?, object?, void>)code)(target, arg);
        return null;
    }

    private static object? InvokeInt32This1Arg(void* code, object? target, object? arg)
    {
        ((delegate*<object?, object?, int>)code)(target, arg);
        DebugConsole.WriteLine("[Reflection] Int32 return boxing not supported");
        return null;
    }

    private static object? InvokeObjectThis1Arg(void* code, object? target, object? arg)
    {
        return ((delegate*<object?, object?, object?>)code)(target, arg);
    }
}
