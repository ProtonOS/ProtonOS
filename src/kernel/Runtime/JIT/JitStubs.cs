// ProtonOS JIT - JIT Stubs
// Provides stub functions called before method calls to ensure lazy compilation.
// This allows methods to be compiled on-demand just before they are called.

using ProtonOS.Platform;
using ProtonOS.Runtime;
using ProtonOS.X64;

namespace ProtonOS.Runtime.JIT;

/// <summary>
/// JIT stub functions for lazy method compilation.
/// These are called before each method call site to ensure the target is compiled.
/// </summary>
public static unsafe class JitStubs
{
    private static nint _ensureCompiledAddress;
    private static nint _ensureVirtualCompiledAddress;
    private static nint _ensureVtableSlotCompiledAddress;
    private static bool _initialized;

    /// <summary>
    /// Initialize the JIT stubs module.
    /// Must be called during kernel initialization before JIT compilation.
    /// </summary>
    public static void Init()
    {
        if (_initialized)
            return;

        // Get function pointer addresses for direct calls from JIT code
        _ensureCompiledAddress = (nint)(delegate*<uint, uint, void>)&EnsureCompiled;
        _ensureVirtualCompiledAddress = (nint)(delegate*<uint, uint, nint, short, void>)&EnsureVirtualCompiled;
        _ensureVtableSlotCompiledAddress = (nint)(delegate*<nint, short, void>)&EnsureVtableSlotCompiled;

        _initialized = true;
        DebugConsole.WriteLine("[JitStubs] Initialized");
    }

    /// <summary>
    /// Get the native address of EnsureCompiled for emitting calls from JIT code.
    /// </summary>
    public static nint EnsureCompiledAddress => _ensureCompiledAddress;

    /// <summary>
    /// Get the native address of EnsureVirtualCompiled for emitting calls from JIT code.
    /// </summary>
    public static nint EnsureVirtualCompiledAddress => _ensureVirtualCompiledAddress;

    /// <summary>
    /// Get the native address of EnsureVtableSlotCompiled for emitting calls from JIT code.
    /// This takes an object pointer and vtable slot, and ensures the method at that slot is compiled.
    /// </summary>
    public static nint EnsureVtableSlotCompiledAddress => _ensureVtableSlotCompiledAddress;

    /// <summary>
    /// Ensures a method is compiled before it is called.
    /// Called before every call/calli instruction.
    ///
    /// Fast path: If already compiled, returns immediately.
    /// Slow path: Triggers JIT compilation of the method.
    /// </summary>
    /// <param name="methodToken">The metadata token of the method (0x06xxxxxx for MethodDef).</param>
    /// <param name="assemblyId">The assembly ID containing the method.</param>
    public static void EnsureCompiled(uint methodToken, uint assemblyId)
    {
        DebugConsole.Write("[JitStubs] Ensure 0x");
        DebugConsole.WriteHex(methodToken);
        DebugConsole.Write(" asm ");
        DebugConsole.WriteDecimal(assemblyId);
        DebugConsole.WriteLine();

        // Fast path: Check if already compiled
        CompiledMethodInfo* info = CompiledMethodRegistry.Lookup(methodToken, assemblyId);
        if (info != null && info->IsCompiled)
        {
            // Already compiled - nothing to do
            return;
        }

        // Slow path: Need to compile the method
        DebugConsole.Write("[JitStubs] Lazy compile 0x");
        DebugConsole.WriteHex(methodToken);
        DebugConsole.Write(" asm ");
        DebugConsole.WriteDecimal(assemblyId);
        DebugConsole.WriteLine();

        var result = Tier0JIT.CompileMethod(assemblyId, methodToken);
        if (!result.Success)
        {
            DebugConsole.Write("[JitStubs] FATAL: Failed to compile 0x");
            DebugConsole.WriteHex(methodToken);
            DebugConsole.Write(" asm ");
            DebugConsole.WriteDecimal(assemblyId);
            DebugConsole.WriteLine();
            DebugConsole.WriteLine("!!! SYSTEM HALTED - JIT compilation failure");
            CPU.HaltForever();
        }
    }

    /// <summary>
    /// Ensures a virtual method is compiled and its vtable slot is populated.
    /// Called before callvirt instructions.
    ///
    /// Fast path: If already compiled and vtable populated, returns immediately.
    /// Slow path: Triggers JIT compilation and updates the vtable.
    /// </summary>
    /// <param name="methodToken">The metadata token of the method.</param>
    /// <param name="assemblyId">The assembly ID containing the method.</param>
    /// <param name="methodTable">The MethodTable pointer of the declaring type.</param>
    /// <param name="vtableSlot">The vtable slot index for this method.</param>
    public static void EnsureVirtualCompiled(uint methodToken, uint assemblyId, nint methodTable, short vtableSlot)
    {
        // Fast path: Check if already compiled
        CompiledMethodInfo* info = CompiledMethodRegistry.Lookup(methodToken, assemblyId);
        if (info != null && info->IsCompiled)
        {
            // Already compiled - ensure vtable is populated
            if (methodTable != 0 && vtableSlot >= 0 && info->NativeCode != null)
            {
                // Check if vtable slot needs updating
                nint* vtable = (nint*)(methodTable + 8);  // Vtable starts at offset 8 in MethodTable
                if (vtable[vtableSlot] != (nint)info->NativeCode)
                {
                    // DebugConsole.Write("[JitStubs] Populating vtable slot ");
                    // DebugConsole.WriteDecimal((uint)vtableSlot);
                    // DebugConsole.Write(" for 0x");
                    // DebugConsole.WriteHex(methodToken);
                    // DebugConsole.WriteLine();
                    vtable[vtableSlot] = (nint)info->NativeCode;
                }
            }
            return;
        }

        // Slow path: Need to compile the method
        // DebugConsole.Write("[JitStubs] Lazy compile virtual 0x");
        // DebugConsole.WriteHex(methodToken);
        // DebugConsole.Write(" asm ");
        // DebugConsole.WriteDecimal(assemblyId);
        // DebugConsole.Write(" slot ");
        // DebugConsole.WriteDecimal((uint)vtableSlot);
        // DebugConsole.WriteLine();

        var result = Tier0JIT.CompileMethod(assemblyId, methodToken);
        if (!result.Success)
        {
            DebugConsole.Write("[JitStubs] FATAL: Failed to compile virtual 0x");
            DebugConsole.WriteHex(methodToken);
            DebugConsole.Write(" asm ");
            DebugConsole.WriteDecimal(assemblyId);
            DebugConsole.WriteLine();
            DebugConsole.WriteLine("!!! SYSTEM HALTED - JIT compilation failure");
            CPU.HaltForever();
        }

        // After compilation, update the vtable slot
        if (methodTable != 0 && vtableSlot >= 0 && result.CodeAddress != null)
        {
            nint* vtable = (nint*)(methodTable + 8);
            vtable[vtableSlot] = (nint)result.CodeAddress;
        }
    }

    /// <summary>
    /// Ensures a vtable slot has compiled code before virtual dispatch.
    /// Called at runtime before callvirt instructions.
    ///
    /// This function takes the actual object pointer (not the MethodTable) so it can
    /// get the real runtime type's MethodTable and check/compile the method at that slot.
    ///
    /// Fast path: If vtable slot already has code, returns immediately.
    /// Slow path: Looks up the method token for this slot and compiles it.
    /// </summary>
    /// <param name="objPtr">Pointer to the object (not MethodTable).</param>
    /// <param name="vtableSlot">The vtable slot index to check/compile.</param>
    public static void EnsureVtableSlotCompiled(nint objPtr, short vtableSlot)
    {
        if (objPtr == 0 || vtableSlot < 0)
            return;

        // Get the MethodTable pointer from the object
        // Object layout: [MethodTable* at offset 0]
        nint methodTable = *(nint*)objPtr;
        if (methodTable == 0)
            return;

        // Get vtable from MethodTable
        // MethodTable layout: [ComponentSize (2)] [Flags (2)] [BaseSize (4)] [RelatedType (8)]
        //                     [NumVtableSlots (2)] [NumInterfaces (2)] [HashCode (4)] [VTable...]
        // MethodTable.HeaderSize = 24 bytes
        nint* vtable = (nint*)(methodTable + ProtonOS.Runtime.MethodTable.HeaderSize);
        nint currentSlotCode = vtable[vtableSlot];

        // If slot already has code, we're done (fast path)
        // Note: AOT methods will already have their addresses in the vtable
        if (currentSlotCode != 0)
            return;

        // Slow path: Need to find and compile the method for this slot
        // Look up the method in the registry by MethodTable and vtable slot
        CompiledMethodInfo* info = CompiledMethodRegistry.LookupByVtableSlot((void*)methodTable, vtableSlot);

        // If not found and this is an instantiated generic type, try the generic definition MT
        if (info == null)
        {
            MethodTable* genDefMT = AssemblyLoader.GetGenericDefinitionMT((MethodTable*)methodTable);
            if (genDefMT != null)
            {
                info = CompiledMethodRegistry.LookupByVtableSlot(genDefMT, vtableSlot);
            }
        }

        if (info != null)
        {
            // Found a registered method for this slot
            if (info->IsCompiled && info->NativeCode != null)
            {
                // Method is compiled, populate the vtable slot
                vtable[vtableSlot] = (nint)info->NativeCode;
                return;
            }

            // Method is registered but not compiled - compile it
            if (!info->IsBeingCompiled)
            {
                // DebugConsole.Write("[JitStubs] Lazy compiling vtable slot ");
                // DebugConsole.WriteDecimal((uint)vtableSlot);
                // DebugConsole.Write(" token 0x");
                // DebugConsole.WriteHex(info->Token);
                // DebugConsole.WriteLine();

                var result = Tier0JIT.CompileMethod(info->AssemblyId, info->Token);
                if (result.Success && result.CodeAddress != null)
                {
                    // Update vtable slot with compiled code
                    vtable[vtableSlot] = (nint)result.CodeAddress;
                }
                else
                {
                    DebugConsole.Write("[JitStubs] FATAL: Failed to compile vtable slot ");
                    DebugConsole.WriteDecimal((uint)vtableSlot);
                    DebugConsole.Write(" token 0x");
                    DebugConsole.WriteHex(info->Token);
                    DebugConsole.Write(" asm ");
                    DebugConsole.WriteDecimal(info->AssemblyId);
                    DebugConsole.WriteLine();
                    DebugConsole.WriteLine("!!! SYSTEM HALTED - JIT compilation failure");
                    CPU.HaltForever();
                }
            }
            return;
        }

        // No method registered for this vtable slot - check for default interface method
        MethodTable* mt = (MethodTable*)methodTable;

        // Try to find a default interface implementation for this slot
        nint defaultImplCode = TryResolveDefaultInterfaceMethod(mt, vtableSlot);
        if (defaultImplCode != 0)
        {
            // Found a default implementation, populate the vtable
            vtable[vtableSlot] = defaultImplCode;
            return;
        }

        // Still not found - this is a gap in our metadata
        DebugConsole.Write("[JitStubs] FATAL: VTable slot ");
        DebugConsole.WriteDecimal((uint)vtableSlot);
        DebugConsole.Write(" has no registered method for MT 0x");
        DebugConsole.WriteHex((ulong)methodTable);
        DebugConsole.WriteLine();

        // Try to identify the type from ReflectionRuntime
        Reflection.ReflectionRuntime.LookupTypeInfo((MethodTable*)methodTable, out uint asmId, out uint token);
        DebugConsole.Write("  Type info: asmId=");
        DebugConsole.WriteDecimal(asmId);
        DebugConsole.Write(" token=0x");
        DebugConsole.WriteHex(token);
        DebugConsole.WriteLine();

        // Print vtable slots 0-3 to help identify the type
        DebugConsole.Write("  VTable slots: ");
        for (int i = 0; i < 4; i++)
        {
            DebugConsole.Write("[");
            DebugConsole.WriteDecimal((uint)i);
            DebugConsole.Write("]=0x");
            DebugConsole.WriteHex((ulong)vtable[i]);
            DebugConsole.Write(" ");
        }
        DebugConsole.WriteLine();

        DebugConsole.WriteLine("!!! SYSTEM HALTED - Missing vtable method registration");
        CPU.HaltForever();
    }

    /// <summary>
    /// Try to resolve a default interface method for a vtable slot.
    /// Called when a class implements an interface but doesn't override a method
    /// that has a default implementation in the interface.
    /// </summary>
    /// <param name="mt">The MethodTable of the implementing class.</param>
    /// <param name="vtableSlot">The vtable slot to resolve.</param>
    /// <returns>The native code pointer for the default implementation, or 0 if not found.</returns>
    private static nint TryResolveDefaultInterfaceMethod(MethodTable* mt, short vtableSlot)
    {
        if (mt == null || mt->_usNumInterfaces == 0)
            return 0;

        // Iterate through the interface map to find which interface this slot belongs to
        InterfaceMapEntry* map = mt->GetInterfaceMapPtr();
        int numInterfaces = mt->_usNumInterfaces;

        for (int i = 0; i < numInterfaces; i++)
        {
            MethodTable* interfaceMT = map[i].InterfaceMT;
            int startSlot = map[i].StartSlot;

            if (interfaceMT == null)
                continue;

            // Check if vtableSlot is in this interface's range
            int interfaceMethodCount = interfaceMT->_usNumVtableSlots;
            if (vtableSlot >= startSlot && vtableSlot < startSlot + interfaceMethodCount)
            {
                // This slot belongs to this interface
                int methodIndex = vtableSlot - startSlot;

                DebugConsole.Write("[JitStubs] VTable slot ");
                DebugConsole.WriteDecimal((uint)vtableSlot);
                DebugConsole.Write(" is interface method ");
                DebugConsole.WriteDecimal((uint)methodIndex);
                DebugConsole.Write(" of interface MT 0x");
                DebugConsole.WriteHex((ulong)interfaceMT);
                DebugConsole.WriteLine();

                // Look up the interface type info to get its assembly ID and type token
                Reflection.ReflectionRuntime.LookupTypeInfo(interfaceMT, out uint interfaceAsmId, out uint interfaceTypeToken);

                if (interfaceAsmId == 0 || interfaceTypeToken == 0)
                {
                    DebugConsole.WriteLine("[JitStubs] Could not resolve interface type info");
                    continue;
                }

                // Find the method token for the interface method at this index
                uint interfaceMethodToken = MetadataIntegration.GetInterfaceMethodToken(
                    interfaceAsmId, interfaceTypeToken, methodIndex);

                if (interfaceMethodToken == 0)
                {
                    DebugConsole.Write("[JitStubs] Could not find interface method token at index ");
                    DebugConsole.WriteDecimal((uint)methodIndex);
                    DebugConsole.WriteLine();
                    continue;
                }

                DebugConsole.Write("[JitStubs] Interface method token: 0x");
                DebugConsole.WriteHex(interfaceMethodToken);
                DebugConsole.Write(" asm ");
                DebugConsole.WriteDecimal(interfaceAsmId);
                DebugConsole.WriteLine();

                // Check if this method has a body (is not abstract)
                bool hasBody = MetadataIntegration.InterfaceMethodHasBody(interfaceAsmId, interfaceMethodToken);
                if (!hasBody)
                {
                    DebugConsole.WriteLine("[JitStubs] Interface method is abstract, no default impl");
                    continue;
                }

                DebugConsole.WriteLine("[JitStubs] Found default interface method - compiling");

                // Compile the interface's default implementation
                var result = Tier0JIT.CompileMethod(interfaceAsmId, interfaceMethodToken);
                if (result.Success && result.CodeAddress != null)
                {
                    DebugConsole.Write("[JitStubs] Default impl compiled at 0x");
                    DebugConsole.WriteHex((ulong)result.CodeAddress);
                    DebugConsole.WriteLine();
                    return (nint)result.CodeAddress;
                }
                else
                {
                    DebugConsole.WriteLine("[JitStubs] Failed to compile default interface method");
                }
            }
        }

        return 0;
    }
}
