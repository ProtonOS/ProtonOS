// ProtonOS kernel - Reflection Runtime
// Provides kernel-side support for reflection operations.
// Exports functions that korlib can call via DllImport("*").

using System.Runtime;
using ProtonOS.Memory;
using ProtonOS.Platform;
using ProtonOS.Runtime;
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
    // Now uses BlockChain for unlimited growth.
    private const int TypeInfoBlockSize = 32;
    private static BlockChain _typeInfoChain;

    /// <summary>
    /// Initialize the reflection runtime.
    /// </summary>
    public static void Init()
    {
        if (_initialized)
            return;

        // Initialize type info registry block chain
        fixed (BlockChain* chain = &_typeInfoChain)
        {
            if (!BlockAllocator.Init(chain, sizeof(TypeReflectionInfo), TypeInfoBlockSize))
            {
                DebugConsole.WriteLine("[ReflectionRuntime] Failed to init type info chain");
                return;
            }
        }

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

        // Check if already registered - iterate through blocks
        fixed (BlockChain* chain = &_typeInfoChain)
        {
            var block = chain->First;
            while (block != null)
            {
                for (int i = 0; i < block->Used; i++)
                {
                    var entry = (TypeReflectionInfo*)block->GetEntry(i);
                    if (entry->MT == mt)
                    {
                        // Update existing
                        entry->AssemblyId = assemblyId;
                        entry->TypeDefToken = typeDefToken;
                        return;
                    }
                }
                block = block->Next;
            }

            // Add new entry
            TypeReflectionInfo newEntry;
            newEntry.AssemblyId = assemblyId;
            newEntry.TypeDefToken = typeDefToken;
            newEntry.MT = mt;
            if (BlockAllocator.Add(chain, &newEntry) == null)
            {
                DebugConsole.WriteLine("[ReflectionRuntime] Type info allocation failed");
            }
        }
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

        fixed (BlockChain* chain = &_typeInfoChain)
        {
            var block = chain->First;
            while (block != null)
            {
                for (int i = 0; i < block->Used; i++)
                {
                    var entry = (TypeReflectionInfo*)block->GetEntry(i);
                    if (entry->MT == mt)
                    {
                        assemblyId = entry->AssemblyId;
                        typeDefToken = entry->TypeDefToken;
                        return true;
                    }
                }
                block = block->Next;
            }
        }

        return false;
    }

    /// <summary>
    /// Get type info from a MethodTable pointer.
    /// Returns assembly ID and type token, or 0 if not found.
    /// </summary>
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
    /// Supports reference types fully and primitive value types (int, long, byte, bool, etc.)
    /// by reading raw bytes. Complex value types (structs) are not fully supported.
    /// </summary>
    public static object? GetFieldValue(uint fieldToken, object? target, int fieldOffset, int fieldSize, bool isValueType)
    {
        // Static field case with negative offset means absolute address
        byte* fieldPtr;
        if (target != null)
        {
            byte* objPtr = (byte*)System.Runtime.CompilerServices.Unsafe.AsPointer(ref target);
            objPtr += sizeof(void*); // Skip MethodTable*
            fieldPtr = objPtr + fieldOffset;
        }
        else if (fieldOffset < 0)
        {
            // For static fields, negative offset indicates invalid/unresolved
            DebugConsole.WriteLine("[Reflection] Static field not resolved");
            return null;
        }
        else
        {
            // Static field with absolute address
            fieldPtr = (byte*)(nint)fieldOffset;
        }

        if (!isValueType)
        {
            // Reference type - just read the pointer
            return *(object*)fieldPtr;
        }

        // Value type - read raw bytes based on field size
        // Boxing requires allocation which we can't do fully, but we can read the value
        // and return it in a form the caller can use
        return ReadPrimitiveValue(fieldPtr, fieldSize);
    }

    /// <summary>
    /// Set field value using field token and offset.
    /// Supports reference types fully and primitive value types by unboxing.
    /// </summary>
    public static void SetFieldValue(uint fieldToken, object? target, int fieldOffset, int fieldSize, bool isValueType, object? value)
    {
        byte* fieldPtr;
        if (target != null)
        {
            byte* objPtr = (byte*)System.Runtime.CompilerServices.Unsafe.AsPointer(ref target);
            objPtr += sizeof(void*); // Skip MethodTable*
            fieldPtr = objPtr + fieldOffset;
        }
        else if (fieldOffset < 0)
        {
            DebugConsole.WriteLine("[Reflection] Static field not resolved");
            return;
        }
        else
        {
            fieldPtr = (byte*)(nint)fieldOffset;
        }

        if (!isValueType)
        {
            // Reference type - just write the pointer
            *(object*)fieldPtr = value;
            return;
        }

        // Value type - write raw bytes based on field size
        // The value should already be boxed (object containing the primitive)
        WritePrimitiveValue(fieldPtr, fieldSize, value);
    }

    /// <summary>
    /// Read a primitive value from memory and return as object.
    /// In minimal runtime without full boxing support, we return null for value types.
    /// For proper value type support, use PalGetFieldValueRaw which returns the raw bytes.
    /// </summary>
    private static object? ReadPrimitiveValue(byte* ptr, int size)
    {
        // Boxing requires RuntimeExports which aren't available in minimal runtime.
        // Value type fields require JIT-compiled boxing thunks or a full runtime.
        // For now, log and return null. Callers should use PalGetFieldValueRaw instead.
        DebugConsole.Write("[Reflection] Value type GetFieldValue (size ");
        DebugConsole.WriteDecimal((uint)size);
        DebugConsole.WriteLine(") - use PalGetFieldValueRaw instead");
        return null;
    }

    /// <summary>
    /// Get raw field value bytes for value types.
    /// Writes the field value to the provided buffer.
    /// Returns the number of bytes written, or 0 on error.
    /// </summary>
    public static int GetFieldValueRaw(uint fieldToken, object? target, int fieldOffset, int fieldSize, bool isValueType, byte* buffer, int bufferSize)
    {
        if (buffer == null || bufferSize < fieldSize)
            return 0;

        byte* fieldPtr;
        if (target != null)
        {
            byte* objPtr = (byte*)System.Runtime.CompilerServices.Unsafe.AsPointer(ref target);
            objPtr += sizeof(void*); // Skip MethodTable*
            fieldPtr = objPtr + fieldOffset;
        }
        else if (fieldOffset < 0)
        {
            return 0;
        }
        else
        {
            fieldPtr = (byte*)(nint)fieldOffset;
        }

        // Copy the raw bytes to the buffer
        for (int i = 0; i < fieldSize; i++)
            buffer[i] = fieldPtr[i];

        return fieldSize;
    }

    /// <summary>
    /// Write a primitive value to memory.
    /// </summary>
    private static void WritePrimitiveValue(byte* ptr, int size, object? value)
    {
        if (value == null)
        {
            // Zero-initialize
            for (int i = 0; i < size; i++)
                ptr[i] = 0;
            return;
        }

        // Value is boxed - we need to unbox it
        // In minimal runtime, unboxing is limited
        // We attempt to read the boxed value's data

        byte* valueData = (byte*)System.Runtime.CompilerServices.Unsafe.AsPointer(ref value);
        valueData += sizeof(void*); // Skip MethodTable*

        // Copy the value bytes
        switch (size)
        {
            case 1:
                *ptr = *valueData;
                break;
            case 2:
                *(short*)ptr = *(short*)valueData;
                break;
            case 4:
                *(int*)ptr = *(int*)valueData;
                break;
            case 8:
                *(long*)ptr = *(long*)valueData;
                break;
            default:
                // Copy arbitrary size
                for (int i = 0; i < size; i++)
                    ptr[i] = valueData[i];
                break;
        }
    }

    /// <summary>
    /// Get field information including type signature for proper boxing.
    /// Returns the ElementType for primitive fields, or 0 for unknown/complex types.
    /// </summary>
    public static byte GetFieldElementType(uint assemblyId, uint fieldToken)
    {
        LoadedAssembly* asm = AssemblyLoader.GetAssembly(assemblyId);
        if (asm == null)
            return 0;

        uint rowId = fieldToken & 0x00FFFFFF;
        uint sigBlobIdx = MetadataReader.GetFieldSignature(ref asm->Tables, ref asm->Sizes, rowId);

        byte* blob = MetadataReader.GetBlob(ref asm->Metadata, sigBlobIdx, out uint blobLen);
        if (blob == null || blobLen < 2)
            return 0;

        // Field signature: FIELD (0x06) followed by Type
        if (blob[0] != 0x06) // SignatureHeader.Field
            return 0;

        return blob[1]; // ElementType byte
    }

    /// <summary>
    /// Get method name from token (returns pointer to null-terminated UTF-8 string).
    /// </summary>
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
    public static bool IsMethodVirtual(uint assemblyId, uint methodToken)
    {
        LoadedAssembly* asm = AssemblyLoader.GetAssembly(assemblyId);
        if (asm == null)
            return false;

        uint rowId = methodToken & 0x00FFFFFF;
        return MetadataReader.IsMethodDefVirtual(ref asm->Tables, ref asm->Sizes, rowId);
    }

    // ========================================================================
    // Type Enumeration APIs
    // ========================================================================

    /// <summary>
    /// Get the number of loaded assemblies.
    /// </summary>
    public static uint GetAssemblyCount()
    {
        return (uint)AssemblyLoader.GetAssemblyCount();
    }

    /// <summary>
    /// Get an assembly ID by index (0-based).
    /// Returns 0 if index is out of range.
    /// </summary>
    public static uint GetAssemblyIdByIndex(uint index)
    {
        // Iterate through loaded assemblies to find the nth one
        int count = 0;
        for (uint id = 1; id <= 32; id++)  // AssemblyLoader.MaxAssemblies = 32
        {
            LoadedAssembly* asm = AssemblyLoader.GetAssembly(id);
            if (asm != null && asm->IsLoaded)
            {
                if (count == (int)index)
                {
                    return id;
                }
                count++;
            }
        }
        return 0;
    }

    /// <summary>
    /// Get the number of types (TypeDef entries) in an assembly.
    /// </summary>
    public static uint GetTypeCount(uint assemblyId)
    {
        LoadedAssembly* asm = AssemblyLoader.GetAssembly(assemblyId);
        if (asm == null)
            return 0;

        return asm->Tables.RowCounts[(int)MetadataTableId.TypeDef];
    }

    /// <summary>
    /// Get the type token at the specified index (0-based).
    /// Returns 0x02xxxxxx token or 0 if index is out of range.
    /// Note: TypeDef rows are 1-based in metadata, but this API uses 0-based index.
    /// Row 1 is typically &lt;Module&gt;, so useful types start at index 1.
    /// </summary>
    public static uint GetTypeTokenByIndex(uint assemblyId, uint index)
    {
        LoadedAssembly* asm = AssemblyLoader.GetAssembly(assemblyId);
        if (asm == null)
            return 0;

        uint typeCount = asm->Tables.RowCounts[(int)MetadataTableId.TypeDef];
        if (index >= typeCount)
            return 0;

        // TypeDef rows are 1-based, so row = index + 1
        return 0x02000000 | (index + 1);
    }

    /// <summary>
    /// Find a type by its name and namespace in an assembly.
    /// Returns the TypeDef token (0x02xxxxxx) or 0 if not found.
    /// </summary>
    public static uint FindTypeByName(uint assemblyId, byte* nameUtf8, byte* namespaceUtf8)
    {
        LoadedAssembly* asm = AssemblyLoader.GetAssembly(assemblyId);
        if (asm == null || nameUtf8 == null)
            return 0;

        uint typeCount = asm->Tables.RowCounts[(int)MetadataTableId.TypeDef];

        for (uint row = 1; row <= typeCount; row++)
        {
            uint nameIdx = MetadataReader.GetTypeDefName(ref asm->Tables, ref asm->Sizes, row);
            byte* typeName = MetadataReader.GetString(ref asm->Metadata, nameIdx);

            if (!StringEquals(typeName, nameUtf8))
                continue;

            // If namespace is provided, check it too
            if (namespaceUtf8 != null && namespaceUtf8[0] != 0)
            {
                uint nsIdx = MetadataReader.GetTypeDefNamespace(ref asm->Tables, ref asm->Sizes, row);
                byte* typeNs = MetadataReader.GetString(ref asm->Metadata, nsIdx);

                if (!StringEquals(typeNs, namespaceUtf8))
                    continue;
            }

            return 0x02000000 | row;
        }

        return 0;
    }

    /// <summary>
    /// Get the MethodTable pointer for a type token if it has been resolved.
    /// Returns null if the type hasn't been instantiated yet.
    /// </summary>
    public static void* GetTypeMethodTable(uint assemblyId, uint typeToken)
    {
        LoadedAssembly* asm = AssemblyLoader.GetAssembly(assemblyId);
        if (asm == null)
            return null;

        return asm->Types.Lookup(typeToken);
    }

    /// <summary>
    /// Get the TypeDef flags (attributes) for a type.
    /// </summary>
    public static uint GetTypeFlags(uint assemblyId, uint typeDefToken)
    {
        LoadedAssembly* asm = AssemblyLoader.GetAssembly(assemblyId);
        if (asm == null)
            return 0;

        uint rowId = typeDefToken & 0x00FFFFFF;
        return MetadataReader.GetTypeDefFlags(ref asm->Tables, ref asm->Sizes, rowId);
    }

    /// <summary>
    /// Get the base type of a type (extends clause).
    /// Returns 0 if no base type or if base type is System.Object.
    /// </summary>
    public static uint GetTypeBaseType(uint assemblyId, uint typeDefToken)
    {
        LoadedAssembly* asm = AssemblyLoader.GetAssembly(assemblyId);
        if (asm == null)
            return 0;

        uint rowId = typeDefToken & 0x00FFFFFF;
        var extends = MetadataReader.GetTypeDefExtends(ref asm->Tables, ref asm->Sizes, rowId);

        // TypeDefOrRef coded index: tag 0=TypeDef, 1=TypeRef, 2=TypeSpec
        if (extends.Table == MetadataTableId.TypeDef)
        {
            return 0x02000000 | extends.RowId;
        }
        else if (extends.Table == MetadataTableId.TypeRef)
        {
            return 0x01000000 | extends.RowId;
        }
        else if (extends.Table == MetadataTableId.TypeSpec)
        {
            return 0x1B000000 | extends.RowId;
        }

        return 0;
    }

    /// <summary>
    /// Helper: Compare two null-terminated UTF-8 strings.
    /// </summary>
    private static bool StringEquals(byte* a, byte* b)
    {
        if (a == null || b == null)
            return a == b;

        while (*a != 0 && *b != 0)
        {
            if (*a != *b)
                return false;
            a++;
            b++;
        }
        return *a == *b;
    }

    /// <summary>
    /// Native invocation dispatcher.
    /// Supports methods with 0-4 arguments, returning void or object reference.
    /// Int32/Int64 return boxing is not supported in minimal runtime.
    /// </summary>
    private static object? InvokeNative(void* nativeCode, CompiledMethodInfo* methodInfo, object? target, object?[]? args)
    {
        bool hasThis = methodInfo->HasThis;
        int argCount = methodInfo->ArgCount;
        ReturnKind returnKind = methodInfo->ReturnKind;

        // Dispatch based on argument count and instance/static
        if (!hasThis)
        {
            // Static methods
            return argCount switch
            {
                0 => InvokeStatic0(nativeCode, returnKind),
                1 => InvokeStatic1(nativeCode, returnKind, args),
                2 => InvokeStatic2(nativeCode, returnKind, args),
                3 => InvokeStatic3(nativeCode, returnKind, args),
                4 => InvokeStatic4(nativeCode, returnKind, args),
                _ => LogUnsupportedAndReturn(argCount, hasThis)
            };
        }
        else
        {
            // Instance methods
            return argCount switch
            {
                0 => InvokeInstance0(nativeCode, returnKind, target),
                1 => InvokeInstance1(nativeCode, returnKind, target, args),
                2 => InvokeInstance2(nativeCode, returnKind, target, args),
                3 => InvokeInstance3(nativeCode, returnKind, target, args),
                4 => InvokeInstance4(nativeCode, returnKind, target, args),
                _ => LogUnsupportedAndReturn(argCount, hasThis)
            };
        }
    }

    private static object? LogUnsupportedAndReturn(int argCount, bool hasThis)
    {
        DebugConsole.Write("[Reflection] Unsupported: ");
        DebugConsole.WriteDecimal((uint)argCount);
        DebugConsole.Write(" args, hasThis=");
        DebugConsole.WriteDecimal(hasThis ? 1u : 0u);
        DebugConsole.WriteLine();
        return null;
    }

    // ========================================================================
    // Static method invocations
    // ========================================================================

    private static object? InvokeStatic0(void* code, ReturnKind returnKind)
    {
        return returnKind switch
        {
            ReturnKind.Void => InvokeVoidStatic0(code),
            ReturnKind.IntPtr => ((delegate*<object?>)code)(),
            ReturnKind.Int32 => InvokeInt32Static0(code),
            ReturnKind.Int64 => InvokeInt64Static0(code),
            _ => null
        };
    }

    private static object? InvokeStatic1(void* code, ReturnKind returnKind, object?[]? args)
    {
        if (args == null || args.Length < 1) return null;
        return returnKind switch
        {
            ReturnKind.Void => InvokeVoidStatic1(code, args[0]),
            ReturnKind.IntPtr => ((delegate*<object?, object?>)code)(args[0]),
            ReturnKind.Int32 => InvokeInt32Static1(code, args[0]),
            _ => null
        };
    }

    private static object? InvokeStatic2(void* code, ReturnKind returnKind, object?[]? args)
    {
        if (args == null || args.Length < 2) return null;
        return returnKind switch
        {
            ReturnKind.Void => InvokeVoidStatic2(code, args[0], args[1]),
            ReturnKind.IntPtr => ((delegate*<object?, object?, object?>)code)(args[0], args[1]),
            ReturnKind.Int32 => InvokeInt32Static2(code, args[0], args[1]),
            _ => null
        };
    }

    private static object? InvokeStatic3(void* code, ReturnKind returnKind, object?[]? args)
    {
        if (args == null || args.Length < 3) return null;
        return returnKind switch
        {
            ReturnKind.Void => InvokeVoidStatic3(code, args[0], args[1], args[2]),
            ReturnKind.IntPtr => ((delegate*<object?, object?, object?, object?>)code)(args[0], args[1], args[2]),
            ReturnKind.Int32 => InvokeInt32Static3(code, args[0], args[1], args[2]),
            _ => null
        };
    }

    private static object? InvokeStatic4(void* code, ReturnKind returnKind, object?[]? args)
    {
        if (args == null || args.Length < 4) return null;
        return returnKind switch
        {
            ReturnKind.Void => InvokeVoidStatic4(code, args[0], args[1], args[2], args[3]),
            ReturnKind.IntPtr => ((delegate*<object?, object?, object?, object?, object?>)code)(args[0], args[1], args[2], args[3]),
            ReturnKind.Int32 => InvokeInt32Static4(code, args[0], args[1], args[2], args[3]),
            _ => null
        };
    }

    // ========================================================================
    // Instance method invocations
    // ========================================================================

    private static object? InvokeInstance0(void* code, ReturnKind returnKind, object? target)
    {
        return returnKind switch
        {
            ReturnKind.Void => InvokeVoidInstance0(code, target),
            ReturnKind.IntPtr => ((delegate*<object?, object?>)code)(target),
            ReturnKind.Int32 => InvokeInt32Instance0(code, target),
            _ => null
        };
    }

    private static object? InvokeInstance1(void* code, ReturnKind returnKind, object? target, object?[]? args)
    {
        if (args == null || args.Length < 1) return null;
        return returnKind switch
        {
            ReturnKind.Void => InvokeVoidInstance1(code, target, args[0]),
            ReturnKind.IntPtr => ((delegate*<object?, object?, object?>)code)(target, args[0]),
            ReturnKind.Int32 => InvokeInt32Instance1(code, target, args[0]),
            _ => null
        };
    }

    private static object? InvokeInstance2(void* code, ReturnKind returnKind, object? target, object?[]? args)
    {
        if (args == null || args.Length < 2) return null;
        return returnKind switch
        {
            ReturnKind.Void => InvokeVoidInstance2(code, target, args[0], args[1]),
            ReturnKind.IntPtr => ((delegate*<object?, object?, object?, object?>)code)(target, args[0], args[1]),
            ReturnKind.Int32 => InvokeInt32Instance2(code, target, args[0], args[1]),
            _ => null
        };
    }

    private static object? InvokeInstance3(void* code, ReturnKind returnKind, object? target, object?[]? args)
    {
        if (args == null || args.Length < 3) return null;
        return returnKind switch
        {
            ReturnKind.Void => InvokeVoidInstance3(code, target, args[0], args[1], args[2]),
            ReturnKind.IntPtr => ((delegate*<object?, object?, object?, object?, object?>)code)(target, args[0], args[1], args[2]),
            ReturnKind.Int32 => InvokeInt32Instance3(code, target, args[0], args[1], args[2]),
            _ => null
        };
    }

    private static object? InvokeInstance4(void* code, ReturnKind returnKind, object? target, object?[]? args)
    {
        if (args == null || args.Length < 4) return null;
        return returnKind switch
        {
            ReturnKind.Void => InvokeVoidInstance4(code, target, args[0], args[1], args[2], args[3]),
            ReturnKind.IntPtr => ((delegate*<object?, object?, object?, object?, object?, object?>)code)(target, args[0], args[1], args[2], args[3]),
            ReturnKind.Int32 => InvokeInt32Instance4(code, target, args[0], args[1], args[2], args[3]),
            _ => null
        };
    }

    // ========================================================================
    // Void return helpers - Static
    // ========================================================================

    private static object? InvokeVoidStatic0(void* code)
    {
        ((delegate*<void>)code)();
        return null;
    }

    private static object? InvokeVoidStatic1(void* code, object? a0)
    {
        ((delegate*<object?, void>)code)(a0);
        return null;
    }

    private static object? InvokeVoidStatic2(void* code, object? a0, object? a1)
    {
        ((delegate*<object?, object?, void>)code)(a0, a1);
        return null;
    }

    private static object? InvokeVoidStatic3(void* code, object? a0, object? a1, object? a2)
    {
        ((delegate*<object?, object?, object?, void>)code)(a0, a1, a2);
        return null;
    }

    private static object? InvokeVoidStatic4(void* code, object? a0, object? a1, object? a2, object? a3)
    {
        ((delegate*<object?, object?, object?, object?, void>)code)(a0, a1, a2, a3);
        return null;
    }

    // ========================================================================
    // Void return helpers - Instance
    // ========================================================================

    private static object? InvokeVoidInstance0(void* code, object? target)
    {
        ((delegate*<object?, void>)code)(target);
        return null;
    }

    private static object? InvokeVoidInstance1(void* code, object? target, object? a0)
    {
        ((delegate*<object?, object?, void>)code)(target, a0);
        return null;
    }

    private static object? InvokeVoidInstance2(void* code, object? target, object? a0, object? a1)
    {
        ((delegate*<object?, object?, object?, void>)code)(target, a0, a1);
        return null;
    }

    private static object? InvokeVoidInstance3(void* code, object? target, object? a0, object? a1, object? a2)
    {
        ((delegate*<object?, object?, object?, object?, void>)code)(target, a0, a1, a2);
        return null;
    }

    private static object? InvokeVoidInstance4(void* code, object? target, object? a0, object? a1, object? a2, object? a3)
    {
        ((delegate*<object?, object?, object?, object?, object?, void>)code)(target, a0, a1, a2, a3);
        return null;
    }

    // ========================================================================
    // Int32 return helpers (return discarded - boxing not supported)
    // ========================================================================

    private static object? InvokeInt32Static0(void* code)
    {
        ((delegate*<int>)code)();
        return null;  // Boxing not supported
    }

    private static object? InvokeInt64Static0(void* code)
    {
        ((delegate*<long>)code)();
        return null;  // Boxing not supported
    }

    private static object? InvokeInt32Static1(void* code, object? a0)
    {
        ((delegate*<object?, int>)code)(a0);
        return null;
    }

    private static object? InvokeInt32Static2(void* code, object? a0, object? a1)
    {
        ((delegate*<object?, object?, int>)code)(a0, a1);
        return null;
    }

    private static object? InvokeInt32Static3(void* code, object? a0, object? a1, object? a2)
    {
        ((delegate*<object?, object?, object?, int>)code)(a0, a1, a2);
        return null;
    }

    private static object? InvokeInt32Static4(void* code, object? a0, object? a1, object? a2, object? a3)
    {
        ((delegate*<object?, object?, object?, object?, int>)code)(a0, a1, a2, a3);
        return null;
    }

    private static object? InvokeInt32Instance0(void* code, object? target)
    {
        ((delegate*<object?, int>)code)(target);
        return null;
    }

    private static object? InvokeInt32Instance1(void* code, object? target, object? a0)
    {
        ((delegate*<object?, object?, int>)code)(target, a0);
        return null;
    }

    private static object? InvokeInt32Instance2(void* code, object? target, object? a0, object? a1)
    {
        ((delegate*<object?, object?, object?, int>)code)(target, a0, a1);
        return null;
    }

    private static object? InvokeInt32Instance3(void* code, object? target, object? a0, object? a1, object? a2)
    {
        ((delegate*<object?, object?, object?, object?, int>)code)(target, a0, a1, a2);
        return null;
    }

    private static object? InvokeInt32Instance4(void* code, object? target, object? a0, object? a1, object? a2, object? a3)
    {
        ((delegate*<object?, object?, object?, object?, object?, int>)code)(target, a0, a1, a2, a3);
        return null;
    }
}
