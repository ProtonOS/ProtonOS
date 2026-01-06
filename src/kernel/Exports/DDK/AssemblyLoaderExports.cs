// Kernel exports for assembly loading from drivers
// These allow JIT-compiled drivers to load additional assemblies

using System.Runtime.InteropServices;
using ProtonOS.Platform;
using ProtonOS.Runtime;
using ProtonOS.Runtime.JIT;

namespace ProtonOS.Exports.DDK;

/// <summary>
/// Kernel exports for dynamic assembly loading.
/// </summary>
public static unsafe class AssemblyLoaderExports
{
    /// <summary>
    /// Load an assembly from a buffer, taking ownership of the memory.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_LoadOwnedAssembly")]
    public static uint LoadOwnedAssembly(byte* buffer, ulong size, uint contextId)
    {
        return AssemblyLoader.LoadOwned(buffer, size, contextId);
    }

    /// <summary>
    /// Find a type with name ending in "Entry" in the assembly.
    /// Used to locate driver entry points.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_FindDriverEntryType")]
    public static uint FindDriverEntryType(uint assemblyId)
    {
        var asm = AssemblyLoader.GetAssembly(assemblyId);
        if (asm == null)
            return 0;

        // Search TypeDef table for types ending in "Entry"
        uint typeDefCount = asm->Tables.RowCounts[(int)MetadataTableId.TypeDef];

        for (uint row = 1; row <= typeDefCount; row++)
        {
            uint nameIdx = MetadataReader.GetTypeDefName(ref asm->Tables, ref asm->Sizes, row);
            byte* typeName = MetadataReader.GetString(ref asm->Metadata, nameIdx);

            if (typeName == null)
                continue;

            // Check if name ends with "Entry"
            int len = 0;
            while (typeName[len] != 0 && len < 256)
                len++;

            if (len >= 5)
            {
                if (typeName[len - 5] == 'E' && typeName[len - 4] == 'n' &&
                    typeName[len - 3] == 't' && typeName[len - 2] == 'r' &&
                    typeName[len - 1] == 'y')
                {
                    return 0x02000000 | row;  // TypeDef token
                }
            }
        }

        return 0;
    }

    /// <summary>
    /// Find a method by name in a type.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_FindMethodByName")]
    public static uint FindMethodByName(uint assemblyId, uint typeToken, byte* methodName)
    {
        if (methodName == null)
            return 0;

        return AssemblyLoader.FindMethodDefByName(assemblyId, typeToken, methodName);
    }

    /// <summary>
    /// JIT compile and call a driver's Initialize method.
    /// Assumes the method returns bool.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_JitAndCallInit")]
    public static bool JitAndCallInit(uint assemblyId, uint methodToken)
    {
        // Get the assembly
        var asm = AssemblyLoader.GetAssembly(assemblyId);
        if (asm == null)
        {
            DebugConsole.WriteLine("[JitInit] Assembly not found");
            return false;
        }

        // JIT compile the method (this will resolve references as needed)
        var result = Tier0JIT.CompileMethod(assemblyId, methodToken);
        if (!result.Success)
        {
            DebugConsole.WriteLine("[JitInit] JIT compilation failed");
            return false;
        }

        // Call the method
        var initFunc = (delegate* unmanaged<bool>)result.CodeAddress;
        try
        {
            return initFunc();
        }
        catch
        {
            DebugConsole.WriteLine("[JitInit] Exception in Initialize");
            return false;
        }
    }
}
