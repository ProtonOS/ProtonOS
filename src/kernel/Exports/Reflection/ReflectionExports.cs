// ProtonOS kernel - Reflection Exports
// Exports reflection APIs to korlib via RuntimeExport.
// Implementation lives in ProtonOS.Runtime.Reflection.ReflectionRuntime.

using System.Runtime;
using ProtonOS.Runtime.Reflection;

namespace ProtonOS.Exports.Reflection;

/// <summary>
/// Reflection exports for korlib.
/// These are callable from korlib via DllImport("*").
/// </summary>
public static unsafe class ReflectionExports
{
    // ========================================================================
    // Type Info APIs
    // ========================================================================

    /// <summary>
    /// Get type info from a MethodTable pointer.
    /// Returns assembly ID and type token, or 0 if not found.
    /// </summary>
    [RuntimeExport("Reflection_GetTypeInfo")]
    public static void GetTypeInfo(void* methodTable, uint* outAssemblyId, uint* outTypeDefToken)
    {
        ReflectionRuntime.GetTypeInfo(methodTable, outAssemblyId, outTypeDefToken);
    }

    /// <summary>
    /// Get type name from TypeDef token.
    /// </summary>
    [RuntimeExport("Reflection_GetTypeName")]
    public static byte* GetTypeName(uint assemblyId, uint typeDefToken)
    {
        return ReflectionRuntime.GetTypeName(assemblyId, typeDefToken);
    }

    /// <summary>
    /// Get type namespace from TypeDef token.
    /// </summary>
    [RuntimeExport("Reflection_GetTypeNamespace")]
    public static byte* GetTypeNamespace(uint assemblyId, uint typeDefToken)
    {
        return ReflectionRuntime.GetTypeNamespace(assemblyId, typeDefToken);
    }

    /// <summary>
    /// Get the TypeDef flags (attributes) for a type.
    /// </summary>
    [RuntimeExport("Reflection_GetTypeFlags")]
    public static uint GetTypeFlags(uint assemblyId, uint typeDefToken)
    {
        return ReflectionRuntime.GetTypeFlags(assemblyId, typeDefToken);
    }

    /// <summary>
    /// Get the base type of a type (extends clause).
    /// Returns 0 if no base type or if base type is System.Object.
    /// </summary>
    [RuntimeExport("Reflection_GetTypeBaseType")]
    public static uint GetTypeBaseType(uint assemblyId, uint typeDefToken)
    {
        return ReflectionRuntime.GetTypeBaseType(assemblyId, typeDefToken);
    }

    /// <summary>
    /// Get the MethodTable pointer for a type token if it has been resolved.
    /// Returns null if the type hasn't been instantiated yet.
    /// </summary>
    [RuntimeExport("Reflection_GetTypeMethodTable")]
    public static void* GetTypeMethodTable(uint assemblyId, uint typeToken)
    {
        return ReflectionRuntime.GetTypeMethodTable(assemblyId, typeToken);
    }

    // ========================================================================
    // Method Invocation APIs
    // ========================================================================

    /// <summary>
    /// Invoke a method by its assembly ID and token.
    /// </summary>
    [RuntimeExport("Reflection_InvokeMethod")]
    public static object? InvokeMethod(uint assemblyId, uint methodToken, object? target, object?[]? args)
    {
        return ReflectionRuntime.InvokeMethod(assemblyId, methodToken, target, args);
    }

    // ========================================================================
    // Method Metadata APIs
    // ========================================================================

    /// <summary>
    /// Get method name from token (returns pointer to null-terminated UTF-8 string).
    /// </summary>
    [RuntimeExport("Reflection_GetMethodName")]
    public static byte* GetMethodName(uint assemblyId, uint methodToken)
    {
        return ReflectionRuntime.GetMethodName(assemblyId, methodToken);
    }

    /// <summary>
    /// Get method count for a type.
    /// </summary>
    [RuntimeExport("Reflection_GetMethodCount")]
    public static uint GetMethodCount(uint assemblyId, uint typeDefToken)
    {
        return ReflectionRuntime.GetMethodCount(assemblyId, typeDefToken);
    }

    /// <summary>
    /// Get method token at index for a type.
    /// </summary>
    [RuntimeExport("Reflection_GetMethodToken")]
    public static uint GetMethodToken(uint assemblyId, uint typeDefToken, uint index)
    {
        return ReflectionRuntime.GetMethodToken(assemblyId, typeDefToken, index);
    }

    /// <summary>
    /// Check if a method is static.
    /// </summary>
    [RuntimeExport("Reflection_IsMethodStatic")]
    public static bool IsMethodStatic(uint assemblyId, uint methodToken)
    {
        return ReflectionRuntime.IsMethodStatic(assemblyId, methodToken);
    }

    /// <summary>
    /// Check if a method is virtual.
    /// </summary>
    [RuntimeExport("Reflection_IsMethodVirtual")]
    public static bool IsMethodVirtual(uint assemblyId, uint methodToken)
    {
        return ReflectionRuntime.IsMethodVirtual(assemblyId, methodToken);
    }

    // ========================================================================
    // Field Access APIs
    // ========================================================================

    /// <summary>
    /// Get field value using field token and offset.
    /// </summary>
    [RuntimeExport("Reflection_GetFieldValue")]
    public static object? GetFieldValue(uint fieldToken, object? target, int fieldOffset, int fieldSize, bool isValueType)
    {
        return ReflectionRuntime.GetFieldValue(fieldToken, target, fieldOffset, fieldSize, isValueType);
    }

    /// <summary>
    /// Set field value using field token and offset.
    /// </summary>
    [RuntimeExport("Reflection_SetFieldValue")]
    public static void SetFieldValue(uint fieldToken, object? target, int fieldOffset, int fieldSize, bool isValueType, object? value)
    {
        ReflectionRuntime.SetFieldValue(fieldToken, target, fieldOffset, fieldSize, isValueType, value);
    }

    /// <summary>
    /// Get raw field value bytes for value types.
    /// </summary>
    [RuntimeExport("Reflection_GetFieldValueRaw")]
    public static int GetFieldValueRaw(uint fieldToken, object? target, int fieldOffset, int fieldSize, bool isValueType, byte* buffer, int bufferSize)
    {
        return ReflectionRuntime.GetFieldValueRaw(fieldToken, target, fieldOffset, fieldSize, isValueType, buffer, bufferSize);
    }

    // ========================================================================
    // Field Metadata APIs
    // ========================================================================

    /// <summary>
    /// Get field count for a type.
    /// </summary>
    [RuntimeExport("Reflection_GetFieldCount")]
    public static uint GetFieldCount(uint assemblyId, uint typeDefToken)
    {
        return ReflectionRuntime.GetFieldCount(assemblyId, typeDefToken);
    }

    /// <summary>
    /// Get field token at index for a type.
    /// </summary>
    [RuntimeExport("Reflection_GetFieldToken")]
    public static uint GetFieldToken(uint assemblyId, uint typeDefToken, uint index)
    {
        return ReflectionRuntime.GetFieldToken(assemblyId, typeDefToken, index);
    }

    /// <summary>
    /// Get field name from token.
    /// </summary>
    [RuntimeExport("Reflection_GetFieldName")]
    public static byte* GetFieldName(uint assemblyId, uint fieldToken)
    {
        return ReflectionRuntime.GetFieldName(assemblyId, fieldToken);
    }

    /// <summary>
    /// Get field element type for proper boxing.
    /// </summary>
    [RuntimeExport("Reflection_GetFieldElementType")]
    public static byte GetFieldElementType(uint assemblyId, uint fieldToken)
    {
        return ReflectionRuntime.GetFieldElementType(assemblyId, fieldToken);
    }

    /// <summary>
    /// Get the MethodTable pointer for a field's type.
    /// Used for implementing FieldInfo.FieldType.
    /// </summary>
    [RuntimeExport("Reflection_GetFieldTypeMethodTable")]
    public static void* GetFieldTypeMethodTable(uint assemblyId, uint fieldToken)
    {
        return ReflectionRuntime.GetFieldTypeMethodTable(assemblyId, fieldToken);
    }

    /// <summary>
    /// Get the MethodTable pointer for a method's return type.
    /// Used for implementing PropertyInfo.PropertyType (via getter return type).
    /// </summary>
    [RuntimeExport("Reflection_GetMethodReturnTypeMethodTable")]
    public static void* GetMethodReturnTypeMethodTable(uint assemblyId, uint methodToken)
    {
        return ReflectionRuntime.GetMethodReturnTypeMethodTable(assemblyId, methodToken);
    }

    /// <summary>
    /// Get the number of parameters for a method.
    /// Used for implementing MethodInfo.GetParameters().
    /// </summary>
    [RuntimeExport("Reflection_GetMethodParameterCount")]
    public static int GetMethodParameterCount(uint assemblyId, uint methodToken)
    {
        return ReflectionRuntime.GetMethodParameterCount(assemblyId, methodToken);
    }

    /// <summary>
    /// Get the parameter type MethodTable at the given index (0-based).
    /// Used for implementing MethodInfo.GetParameters().
    /// </summary>
    [RuntimeExport("Reflection_GetMethodParameterTypeMethodTable")]
    public static void* GetMethodParameterTypeMethodTable(uint assemblyId, uint methodToken, int paramIndex)
    {
        return ReflectionRuntime.GetMethodParameterTypeMethodTable(assemblyId, methodToken, paramIndex);
    }

    /// <summary>
    /// Get the parameter name at the given index (0-based).
    /// Returns pointer to null-terminated UTF-8 string.
    /// </summary>
    [RuntimeExport("Reflection_GetMethodParameterName")]
    public static byte* GetMethodParameterName(uint assemblyId, uint methodToken, int paramIndex)
    {
        return ReflectionRuntime.GetMethodParameterName(assemblyId, methodToken, paramIndex);
    }

    // ========================================================================
    // Assembly Enumeration APIs
    // ========================================================================

    /// <summary>
    /// Get the number of loaded assemblies.
    /// </summary>
    [RuntimeExport("Reflection_GetAssemblyCount")]
    public static uint GetAssemblyCount()
    {
        return ReflectionRuntime.GetAssemblyCount();
    }

    /// <summary>
    /// Get an assembly ID by index (0-based).
    /// </summary>
    [RuntimeExport("Reflection_GetAssemblyIdByIndex")]
    public static uint GetAssemblyIdByIndex(uint index)
    {
        return ReflectionRuntime.GetAssemblyIdByIndex(index);
    }

    // ========================================================================
    // Type Enumeration APIs
    // ========================================================================

    /// <summary>
    /// Get the number of types (TypeDef entries) in an assembly.
    /// </summary>
    [RuntimeExport("Reflection_GetTypeCount")]
    public static uint GetTypeCount(uint assemblyId)
    {
        return ReflectionRuntime.GetTypeCount(assemblyId);
    }

    /// <summary>
    /// Get the type token at the specified index (0-based).
    /// </summary>
    [RuntimeExport("Reflection_GetTypeTokenByIndex")]
    public static uint GetTypeTokenByIndex(uint assemblyId, uint index)
    {
        return ReflectionRuntime.GetTypeTokenByIndex(assemblyId, index);
    }

    /// <summary>
    /// Find a type by its name and namespace in an assembly.
    /// </summary>
    [RuntimeExport("Reflection_FindTypeByName")]
    public static uint FindTypeByName(uint assemblyId, byte* nameUtf8, byte* namespaceUtf8)
    {
        return ReflectionRuntime.FindTypeByName(assemblyId, nameUtf8, namespaceUtf8);
    }

    // ========================================================================
    // Debug APIs (for korlib debugging)
    // ========================================================================

    /// <summary>
    /// Print a debug message with an integer value.
    /// </summary>
    [RuntimeExport("Debug_PrintInt")]
    public static void PrintInt(byte* prefix, int value)
    {
        ProtonOS.Platform.DebugConsole.Write("[korlib] ");
        while (*prefix != 0)
        {
            ProtonOS.Platform.DebugConsole.WriteChar((char)*prefix);
            prefix++;
        }
        ProtonOS.Platform.DebugConsole.WriteDecimal((uint)value);
        ProtonOS.Platform.DebugConsole.WriteLine();
    }

    /// <summary>
    /// Print a debug message.
    /// </summary>
    [RuntimeExport("Debug_Print")]
    public static void Print(byte* message)
    {
        ProtonOS.Platform.DebugConsole.Write("[korlib] ");
        while (*message != 0)
        {
            ProtonOS.Platform.DebugConsole.WriteChar((char)*message);
            message++;
        }
        ProtonOS.Platform.DebugConsole.WriteLine();
    }

    // ========================================================================
    // Boxing/Allocation APIs
    // ========================================================================

    /// <summary>
    /// Box a value type given its MethodTable pointer and value data.
    /// This allocates a new object and copies the value data into it.
    /// Used by TypedReference.ToObject for value type boxing.
    /// </summary>
    /// <param name="methodTable">The MethodTable* for the value type</param>
    /// <param name="valueData">Pointer to the raw value data</param>
    /// <param name="valueSize">Size of the value data in bytes</param>
    /// <returns>Boxed object pointer, or null on failure</returns>
    [RuntimeExport("Reflection_BoxValue")]
    public static void* BoxValue(void* methodTable, void* valueData, int valueSize)
    {
        return ReflectionRuntime.BoxValue(methodTable, valueData, valueSize);
    }
}
