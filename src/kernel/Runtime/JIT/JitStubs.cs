// ProtonOS JIT - JIT Stubs
// Provides stub functions called before method calls to ensure lazy compilation.
// This allows methods to be compiled on-demand just before they are called.

using ProtonOS.Platform;
using ProtonOS.Runtime;

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
        // Fast path: Check if already compiled
        CompiledMethodInfo* info = CompiledMethodRegistry.Lookup(methodToken, assemblyId);
        if (info != null && info->IsCompiled)
        {
            // Already compiled - nothing to do
            return;
        }

        // Slow path: Need to compile the method
        // DebugConsole.Write("[JitStubs] Lazy compile 0x");
        // DebugConsole.WriteHex(methodToken);
        // DebugConsole.Write(" asm ");
        // DebugConsole.WriteDecimal(assemblyId);
        // DebugConsole.WriteLine();

        var result = Tier0JIT.CompileMethod(assemblyId, methodToken);
        if (!result.Success)
        {
            DebugConsole.Write("[JitStubs] FAILED to compile 0x");
            DebugConsole.WriteHex(methodToken);
            DebugConsole.Write(" asm ");
            DebugConsole.WriteDecimal(assemblyId);
            DebugConsole.WriteLine();
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
            DebugConsole.Write("[JitStubs] FAILED to compile virtual 0x");
            DebugConsole.WriteHex(methodToken);
            DebugConsole.Write(" asm ");
            DebugConsole.WriteDecimal(assemblyId);
            DebugConsole.WriteLine();
            return;
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
                DebugConsole.Write("[JitStubs] Lazy compiling vtable slot ");
                DebugConsole.WriteDecimal((uint)vtableSlot);
                DebugConsole.Write(" token 0x");
                DebugConsole.WriteHex(info->Token);
                DebugConsole.Write(" asm ");
                DebugConsole.WriteDecimal(info->AssemblyId);
                DebugConsole.Write(" objMT=0x");
                DebugConsole.WriteHex((ulong)methodTable);
                DebugConsole.WriteLine();

                var result = Tier0JIT.CompileMethod(info->AssemblyId, info->Token);
                if (result.Success && result.CodeAddress != null)
                {
                    // Update vtable slot with compiled code
                    vtable[vtableSlot] = (nint)result.CodeAddress;
                    DebugConsole.Write("[JitStubs] Updated vtable slot ");
                    DebugConsole.WriteDecimal((uint)vtableSlot);
                    DebugConsole.Write(" to 0x");
                    DebugConsole.WriteHex((ulong)result.CodeAddress);
                    // Read back to verify
                    nint readBack = vtable[vtableSlot];
                    DebugConsole.Write(" readBack=0x");
                    DebugConsole.WriteHex((ulong)readBack);
                    DebugConsole.WriteLine();
                }
                else
                {
                    DebugConsole.Write("[JitStubs] FAILED to compile vtable slot ");
                    DebugConsole.WriteDecimal((uint)vtableSlot);
                    DebugConsole.WriteLine();
                }
            }
            return;
        }

        // No method registered for this vtable slot - this is a gap in our metadata
        DebugConsole.Write("[JitStubs] VTable slot ");
        DebugConsole.WriteDecimal((uint)vtableSlot);
        DebugConsole.Write(" has no registered method for MT 0x");
        DebugConsole.WriteHex((ulong)methodTable);
        DebugConsole.WriteLine();
    }
}
