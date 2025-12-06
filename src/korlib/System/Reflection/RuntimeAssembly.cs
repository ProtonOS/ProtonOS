// ProtonOS korlib - RuntimeAssembly
// Runtime implementation of Assembly using kernel PAL reflection APIs.

using System.Runtime;
using System.Runtime.InteropServices;

namespace System.Reflection;

/// <summary>
/// Runtime implementation of Assembly that uses kernel PAL for type enumeration.
/// </summary>
public unsafe sealed class RuntimeAssembly : Assembly
{
    private readonly uint _assemblyId;
    private string? _fullName;
    private Type[]? _cachedTypes;

    /// <summary>
    /// Create a RuntimeAssembly from an assembly ID.
    /// </summary>
    internal RuntimeAssembly(uint assemblyId)
    {
        _assemblyId = assemblyId;
    }

    /// <summary>
    /// Get the kernel assembly ID.
    /// </summary>
    internal uint AssemblyId => _assemblyId;

    public override string FullName
    {
        get
        {
            if (_fullName == null)
            {
                _fullName = GetAssemblyName() ?? "Unknown Assembly";
            }
            return _fullName;
        }
    }

    private string? GetAssemblyName()
    {
        // For now, use assembly ID as identifier
        // Full implementation would read Assembly table from metadata
        return "Assembly_" + _assemblyId.ToString();
    }

    /// <summary>
    /// Gets the types defined in this assembly.
    /// </summary>
    public override Type[] GetTypes()
    {
        if (_cachedTypes != null)
            return _cachedTypes;

        uint typeCount = Reflection_GetTypeCount(_assemblyId);
        if (typeCount == 0)
        {
            _cachedTypes = Array.Empty<Type>();
            return _cachedTypes;
        }

        // First pass: count valid types (excluding <Module>)
        int validCount = 0;
        for (uint i = 0; i < typeCount; i++)
        {
            uint typeToken = Reflection_GetTypeTokenByIndex(_assemblyId, i);
            if (typeToken == 0)
                continue;

            // Skip <Module> type (row 1, index 0)
            if (i == 0)
            {
                byte* name = Reflection_GetTypeName(_assemblyId, typeToken);
                if (name != null && name[0] == '<')
                    continue;
            }
            validCount++;
        }

        // Second pass: create types array
        var types = new Type[validCount];
        int idx = 0;

        for (uint i = 0; i < typeCount; i++)
        {
            uint typeToken = Reflection_GetTypeTokenByIndex(_assemblyId, i);
            if (typeToken == 0)
                continue;

            // Skip <Module> type (row 1, index 0)
            if (i == 0)
            {
                byte* name = Reflection_GetTypeName(_assemblyId, typeToken);
                if (name != null && name[0] == '<')
                    continue;
            }

            // Get or create RuntimeType for this type
            void* mt = Reflection_GetTypeMethodTable(_assemblyId, typeToken);
            RuntimeType runtimeType;
            if (mt != null)
            {
                runtimeType = new RuntimeType((MethodTable*)mt, _assemblyId, typeToken);
            }
            else
            {
                // Type hasn't been instantiated yet, create a lightweight RuntimeType
                runtimeType = new RuntimeType(null, _assemblyId, typeToken);
            }
            types[idx++] = runtimeType;
        }

        _cachedTypes = types;
        return _cachedTypes;
    }

    /// <summary>
    /// Gets the public types defined in this assembly that are visible outside the assembly.
    /// </summary>
    public override Type[] GetExportedTypes()
    {
        // For simplicity, return all types that are public
        var allTypes = GetTypes();

        // First pass: count public types
        int publicCount = 0;
        for (int i = 0; i < allTypes.Length; i++)
        {
            if (allTypes[i] is RuntimeType rt)
            {
                uint flags = Reflection_GetTypeFlags(_assemblyId, GetTypeToken(rt));
                // Visibility mask is 0x7, Public = 1
                if ((flags & 0x7) == 1)
                    publicCount++;
            }
        }

        if (publicCount == allTypes.Length)
            return allTypes;

        // Second pass: build public types array
        var exported = new Type[publicCount];
        int idx = 0;
        for (int i = 0; i < allTypes.Length; i++)
        {
            if (allTypes[i] is RuntimeType rt)
            {
                uint flags = Reflection_GetTypeFlags(_assemblyId, GetTypeToken(rt));
                if ((flags & 0x7) == 1)
                    exported[idx++] = allTypes[i];
            }
        }

        return exported;
    }

    /// <summary>
    /// Gets the Type object with the specified name in this assembly.
    /// </summary>
    public override Type? GetType(string name, bool throwOnError, bool ignoreCase)
    {
        if (name == null)
            throw new ArgumentNullException(nameof(name));

        // Parse namespace and type name
        string? ns = null;
        string typeName = name;

        int lastDot = name.LastIndexOf('.');
        if (lastDot >= 0)
        {
            ns = name.Substring(0, lastDot);
            typeName = name.Substring(lastDot + 1);
        }

        // Convert to UTF-8 for PAL call using stackalloc (no heap allocation needed)
        byte* nameUtf8 = stackalloc byte[typeName.Length + 1];
        StringToUtf8(typeName, nameUtf8, typeName.Length + 1);

        byte* nsUtf8 = null;
        byte* nsBuffer = stackalloc byte[ns != null ? ns.Length + 1 : 1];
        if (ns != null)
        {
            nsUtf8 = nsBuffer;
            StringToUtf8(ns, nsUtf8, ns.Length + 1);
        }

        uint typeToken = Reflection_FindTypeByName(_assemblyId, nameUtf8, nsUtf8);

        if (typeToken == 0)
        {
            if (throwOnError)
                throw new TypeLoadException("Could not find type '" + name + "' in assembly.");
            return null;
        }

        // Create RuntimeType for the found type
        void* mt = Reflection_GetTypeMethodTable(_assemblyId, typeToken);
        return new RuntimeType((MethodTable*)mt, _assemblyId, typeToken);
    }

    private uint GetTypeToken(RuntimeType type)
    {
        // Access the type's token - re-query by name
        string? ns = type.Namespace;
        string? name = type.Name;

        if (name == null)
            return 0;

        byte* nameUtf8 = stackalloc byte[name.Length + 1];
        StringToUtf8(name, nameUtf8, name.Length + 1);

        byte* nsUtf8 = null;
        byte* nsBuffer = stackalloc byte[ns != null ? ns.Length + 1 : 1];
        if (ns != null)
        {
            nsUtf8 = nsBuffer;
            StringToUtf8(ns, nsUtf8, ns.Length + 1);
        }

        return Reflection_FindTypeByName(_assemblyId, nameUtf8, nsUtf8);
    }

    // ========================================================================
    // Static API for getting assemblies
    // ========================================================================

    private static RuntimeAssembly[]? _loadedAssemblies;
    private static int _cachedAssemblyCount;

    /// <summary>
    /// Get all loaded assemblies.
    /// </summary>
    public static RuntimeAssembly[] GetLoadedAssemblies()
    {
        uint count = Reflection_GetAssemblyCount();
        if (_loadedAssemblies != null && _cachedAssemblyCount == (int)count)
            return _loadedAssemblies;

        var assemblies = new RuntimeAssembly[count];
        for (uint i = 0; i < count; i++)
        {
            uint asmId = Reflection_GetAssemblyIdByIndex(i);
            if (asmId != 0)
            {
                assemblies[i] = new RuntimeAssembly(asmId);
            }
        }

        _loadedAssemblies = assemblies;
        _cachedAssemblyCount = (int)count;
        return assemblies;
    }

    /// <summary>
    /// Get the assembly containing a specific type.
    /// </summary>
    public static RuntimeAssembly? GetAssemblyForType(RuntimeType type)
    {
        // Get assembly ID from type's metadata
        uint asmId = 0;
        uint token = 0;
        void* mt = type.TypeHandle.Value.ToPointer();
        if (mt != null)
        {
            Reflection_GetTypeInfo(mt, &asmId, &token);
        }

        if (asmId == 0)
            return null;

        return new RuntimeAssembly(asmId);
    }

    // ========================================================================
    // Helper Methods
    // ========================================================================

    private static void StringToUtf8(string s, byte* buffer, int bufferLen)
    {
        int len = s.Length;
        if (len >= bufferLen)
            len = bufferLen - 1;

        for (int i = 0; i < len; i++)
        {
            char c = s[i];
            buffer[i] = c < 128 ? (byte)c : (byte)'?';  // ASCII only for now
        }
        buffer[len] = 0;
    }

    // ========================================================================
    // Kernel Reflection Imports
    // ========================================================================

    [DllImport("*", EntryPoint = "Reflection_GetAssemblyCount", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint Reflection_GetAssemblyCount();

    [DllImport("*", EntryPoint = "Reflection_GetAssemblyIdByIndex", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint Reflection_GetAssemblyIdByIndex(uint index);

    [DllImport("*", EntryPoint = "Reflection_GetTypeCount", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint Reflection_GetTypeCount(uint assemblyId);

    [DllImport("*", EntryPoint = "Reflection_GetTypeTokenByIndex", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint Reflection_GetTypeTokenByIndex(uint assemblyId, uint index);

    [DllImport("*", EntryPoint = "Reflection_FindTypeByName", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint Reflection_FindTypeByName(uint assemblyId, byte* nameUtf8, byte* namespaceUtf8);

    [DllImport("*", EntryPoint = "Reflection_GetTypeMethodTable", CallingConvention = CallingConvention.Cdecl)]
    private static extern void* Reflection_GetTypeMethodTable(uint assemblyId, uint typeToken);

    [DllImport("*", EntryPoint = "Reflection_GetTypeFlags", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint Reflection_GetTypeFlags(uint assemblyId, uint typeDefToken);

    [DllImport("*", EntryPoint = "Reflection_GetTypeName", CallingConvention = CallingConvention.Cdecl)]
    private static extern byte* Reflection_GetTypeName(uint assemblyId, uint typeDefToken);

    [DllImport("*", EntryPoint = "Reflection_GetTypeInfo", CallingConvention = CallingConvention.Cdecl)]
    private static extern void Reflection_GetTypeInfo(void* methodTable, uint* outAssemblyId, uint* outTypeDefToken);
}
