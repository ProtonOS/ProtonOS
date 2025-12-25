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
        _ensureVtableSlotCompiledAddress = (nint)(delegate*<nint, short, nint>)&EnsureVtableSlotCompiled;

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
    ///
    /// Returns the method address to call (the JIT codegen uses this directly instead
    /// of loading from vtable, which handles out-of-bounds slots correctly).
    /// </summary>
    /// <param name="objPtr">Pointer to the object (not MethodTable).</param>
    /// <param name="vtableSlot">The vtable slot index to check/compile.</param>
    /// <returns>The native method address to call.</returns>
    public static nint EnsureVtableSlotCompiled(nint objPtr, short vtableSlot)
    {
        if (objPtr == 0 || vtableSlot < 0)
            return 0;

        // Get the MethodTable pointer from the object
        // Object layout: [MethodTable* at offset 0]
        nint methodTable = *(nint*)objPtr;
        if (methodTable == 0)
            return 0;

        // Get vtable from MethodTable
        // MethodTable layout: [ComponentSize (2)] [Flags (2)] [BaseSize (4)] [RelatedType (8)]
        //                     [NumVtableSlots (2)] [NumInterfaces (2)] [HashCode (4)] [VTable...]
        // MethodTable.HeaderSize = 24 bytes
        nint* vtable = (nint*)(methodTable + ProtonOS.Runtime.MethodTable.HeaderSize);
        nint currentSlotCode = vtable[vtableSlot];

        // Check if vtable slot is within bounds
        // NativeAOT may optimize away vtable slots for sealed types (e.g., String.GetHashCode)
        // In such cases, we need to look up the method from the AOT registry instead
        MethodTable* mt = (MethodTable*)methodTable;

        if (vtableSlot >= mt->_usNumVtableSlots)
        {
            // Slot is out of bounds - this could be:
            // 1. A sealed virtual slot (for AOT types with HasDispatchMap)
            // 2. A devirtualized method on sealed types like String
            DebugConsole.Write("[VTbounds] slot=");
            DebugConsole.WriteDecimal((uint)vtableSlot);
            DebugConsole.Write(" > numSlots=");
            DebugConsole.WriteDecimal(mt->_usNumVtableSlots);
            DebugConsole.Write(" MT=0x");
            DebugConsole.WriteHex((ulong)methodTable);
            DebugConsole.WriteLine();

            // For AOT types with dispatch maps, out-of-bounds slots are sealed virtual slots.
            // Use MethodTable's centralized sealed slot lookup.
            if (mt->HasDispatchMap)
            {
                int sealedSlotIndex = vtableSlot - mt->_usNumVtableSlots;
                nint sealedMethod = mt->GetSealedVirtualSlot(sealedSlotIndex);

                DebugConsole.Write("[VTbounds] Sealed slot ");
                DebugConsole.WriteDecimal(sealedSlotIndex);
                DebugConsole.Write(" -> 0x");
                DebugConsole.WriteHex((ulong)sealedMethod);
                DebugConsole.WriteLine();

                // Validate the address is in the kernel code range
                if (sealedMethod != 0 && (ulong)sealedMethod >= 0x1DA00000 && (ulong)sealedMethod < 0x1DB00000)
                {
                    return sealedMethod;
                }
                // Invalid sealed slot address - fall through to other fallbacks
            }

            // Look up the method by name from AOT registry for common Object virtuals
            // For sealed types like String, look up the type-specific override first
            nint aotMethod = 0;
            bool isString = MetadataReader.IsStringMethodTable((void*)methodTable);

            if (isString)
            {
                DebugConsole.WriteLine("[VTbounds] Type is String - looking for String-specific method");
            }

            if (vtableSlot == 0)
            {
                // ToString - try type-specific first
                if (isString)
                    aotMethod = AotMethodRegistry.LookupByName("System.String", "ToString");
                if (aotMethod == 0)
                    aotMethod = AotMethodRegistry.LookupByName("System.Object", "ToString");
            }
            else if (vtableSlot == 1)
            {
                // Equals - try type-specific first
                if (isString)
                    aotMethod = AotMethodRegistry.LookupByName("System.String", "Equals");
                if (aotMethod == 0)
                    aotMethod = AotMethodRegistry.LookupByName("System.Object", "Equals");
            }
            else if (vtableSlot == 2)
            {
                // GetHashCode - try type-specific first
                if (isString)
                {
                    aotMethod = AotMethodRegistry.LookupByName("System.String", "GetHashCode");
                    DebugConsole.Write("[VTbounds] String.GetHashCode lookup: 0x");
                    DebugConsole.WriteHex((ulong)aotMethod);
                    DebugConsole.WriteLine();
                }
                if (aotMethod == 0)
                    aotMethod = AotMethodRegistry.LookupByName("System.Object", "GetHashCode");
            }

            if (aotMethod != 0)
            {
                // DON'T write to vtable[slot] - that's past the end of the MT and would corrupt memory!
                // Just return the AOT method directly - the JIT codegen will use our return value
                DebugConsole.Write("[VTbounds] Using AOT fallback: 0x");
                DebugConsole.WriteHex((ulong)aotMethod);
                DebugConsole.Write(" objPtr=0x");
                DebugConsole.WriteHex((ulong)objPtr);
                DebugConsole.WriteLine();

                // Return the AOT method address - JIT will call it with the correct 'this' pointer
                return aotMethod;
            }

            // No AOT fallback available - report error
            DebugConsole.Write("[VTbounds] FATAL: No AOT fallback for slot ");
            DebugConsole.WriteDecimal((uint)vtableSlot);
            DebugConsole.Write(" MT=0x");
            DebugConsole.WriteHex((ulong)methodTable);
            DebugConsole.WriteLine();
            DebugConsole.WriteLine("!!! SYSTEM HALTED - vtable slot out of bounds");
            CPU.HaltForever();
            return 0;  // Never reached
        }

        // If slot already has code, we're done (fast path)
        // Note: AOT methods will already have their addresses in the vtable
        // Primitive types have their ToString/Equals/GetHashCode slots pre-filled
        // during initialization in RegisterPrimitiveViaArrayAllocation.
        if (currentSlotCode != 0)
            return currentSlotCode;

        // Slow path: Need to find and compile the method for this slot
        // Look up the method in the registry by MethodTable and vtable slot
        CompiledMethodInfo* info = CompiledMethodRegistry.LookupByVtableSlot((void*)methodTable, vtableSlot);
        bool foundOnGenericDef = false;

        // If not found and this is an instantiated generic type, try the generic definition MT
        if (info == null)
        {
            MethodTable* genDefMT = AssemblyLoader.GetGenericDefinitionMT((MethodTable*)methodTable);
            if (genDefMT != null)
            {
                info = CompiledMethodRegistry.LookupByVtableSlot(genDefMT, vtableSlot);
                if (info != null)
                    foundOnGenericDef = true;
            }
        }

        if (info != null)
        {
            // Found a registered method for this slot
            // IMPORTANT: If we got this from the generic definition but we're on an instantiated type,
            // we must NOT reuse already-compiled code - we need to recompile with the correct type context.
            // Generic methods may reference type parameters (T) that resolve differently per instantiation.
            if (info->IsCompiled && info->NativeCode != null && !foundOnGenericDef)
            {
                // Method is compiled for THIS specific type, populate the vtable slot
                vtable[vtableSlot] = (nint)info->NativeCode;
                return (nint)info->NativeCode;
            }

            // Method is registered but not compiled for this instantiation - compile it
            if (!info->IsBeingCompiled)
            {
                // DebugConsole.Write("[JitStubs] Lazy compiling vtable slot ");
                // DebugConsole.WriteDecimal((uint)vtableSlot);
                // DebugConsole.Write(" token 0x");
                // DebugConsole.WriteHex(info->Token);
                // DebugConsole.WriteLine();

                // Set up type context if this is an instantiated generic type
                // This ensures that VAR type parameters (like T in ObjectEqualityComparer<T>)
                // can be properly resolved during JIT compilation
                MethodTable* instMT = (MethodTable*)methodTable;
                MethodTable* genDefMT = AssemblyLoader.GetGenericDefinitionMT(instMT);
                bool hasTypeContext = false;

                if (genDefMT != null)
                {
                    // This is a generic instantiation - get all type arguments from cache
                    MethodTable** typeArgs = stackalloc MethodTable*[4];  // Support up to 4 type args
                    int typeArgCount;

                    if (AssemblyLoader.GetGenericInstTypeArgs(instMT, typeArgs, out typeArgCount) && typeArgCount > 0)
                    {
                        // Got full type argument list from cache
                        DebugConsole.Write("[JitStubs] VT cache hit: ");
                        DebugConsole.WriteDecimal((uint)typeArgCount);
                        DebugConsole.Write(" args MT=0x");
                        DebugConsole.WriteHex((ulong)instMT);
                        DebugConsole.Write(" arg0=0x");
                        DebugConsole.WriteHex((ulong)typeArgs[0]);
                        DebugConsole.WriteLine();
                        MetadataIntegration.SetTypeTypeArgs(typeArgs, typeArgCount);
                        hasTypeContext = true;
                    }
                    else if (instMT->GetFirstTypeArgument() != null)
                    {
                        // Fallback: use first type arg for single-argument generics
                        MethodTable* firstArg = instMT->GetFirstTypeArgument();
                        DebugConsole.Write("[JitStubs] VT single arg fallback MT=0x");
                        DebugConsole.WriteHex((ulong)instMT);
                        DebugConsole.Write(" typeArg=0x");
                        DebugConsole.WriteHex((ulong)firstArg);
                        DebugConsole.WriteLine();
                        typeArgs[0] = firstArg;
                        MetadataIntegration.SetTypeTypeArgs(typeArgs, 1);
                        hasTypeContext = true;
                    }
                    else
                    {
                        DebugConsole.Write("[JitStubs] WARNING: No type context for MT=0x");
                        DebugConsole.WriteHex((ulong)instMT);
                        DebugConsole.WriteLine();
                    }
                }

                // Debug: print what we're about to compile
                DebugConsole.Write("[JitStubs] Compiling slot ");
                DebugConsole.WriteDecimal((uint)vtableSlot);
                DebugConsole.Write(" token=0x");
                DebugConsole.WriteHex(info->Token);
                DebugConsole.Write(" asm=");
                DebugConsole.WriteDecimal(info->AssemblyId);
                DebugConsole.WriteLine();

                var result = Tier0JIT.CompileMethod(info->AssemblyId, info->Token);

                // Clear type context after compilation
                if (hasTypeContext)
                {
                    MetadataIntegration.ClearTypeTypeArgs();
                }
                if (result.Success && result.CodeAddress != null)
                {
                    // Update vtable slot with compiled code
                    vtable[vtableSlot] = (nint)result.CodeAddress;
                    return (nint)result.CodeAddress;
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
                    return 0;  // Never reached
                }
            }
            // Being compiled by another thread, return current slot value (may be stale but will retry)
            return vtable[vtableSlot];
        }

        // No method registered for this vtable slot - check for default interface method
        // Note: mt is already declared above at the bounds check

        // Try to find a default interface implementation for this slot
        nint defaultImplCode = TryResolveDefaultInterfaceMethod(mt, vtableSlot);
        if (defaultImplCode != 0)
        {
            // Found a default implementation, populate the vtable
            vtable[vtableSlot] = defaultImplCode;
            return defaultImplCode;
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
        return 0;  // Never reached
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
                    DebugConsole.WriteLine("[JitStubs] Interface method is abstract, looking for impl in class");

                    // Interface method is abstract - find the implementing method in the concrete class
                    // Get the concrete class's type info
                    Reflection.ReflectionRuntime.LookupTypeInfo(mt, out uint classAsmId, out uint classTypeToken);

                    if (classAsmId == 0 || classTypeToken == 0)
                    {
                        DebugConsole.WriteLine("[JitStubs] Could not get concrete class type info");
                        continue;
                    }

                    // Get the interface method name
                    byte* interfaceMethodName = MetadataIntegration.GetMethodName(interfaceAsmId, interfaceMethodToken);
                    if (interfaceMethodName == null)
                    {
                        DebugConsole.WriteLine("[JitStubs] Could not get interface method name");
                        continue;
                    }

                    DebugConsole.Write("[JitStubs] Looking for '");
                    byte* p = interfaceMethodName;
                    while (*p != 0) { DebugConsole.WriteChar((char)*p++); }
                    DebugConsole.Write("' in type 0x");
                    DebugConsole.WriteHex(classTypeToken);
                    DebugConsole.Write(" asm ");
                    DebugConsole.WriteDecimal(classAsmId);
                    DebugConsole.WriteLine();

                    // Find the implementing method by name
                    uint implMethodToken = MetadataIntegration.FindMethodByName(classAsmId, classTypeToken, interfaceMethodName);

                    if (implMethodToken == 0)
                    {
                        DebugConsole.WriteLine("[JitStubs] Could not find implementing method, checking base class");
                        // Try searching in parent classes (for inherited implementations)
                        MethodTable* parentMT = mt->GetParentType();
                        while (parentMT != null && implMethodToken == 0)
                        {
                            Reflection.ReflectionRuntime.LookupTypeInfo(parentMT, out uint parentAsmId, out uint parentTypeToken);
                            if (parentAsmId != 0 && parentTypeToken != 0)
                            {
                                implMethodToken = MetadataIntegration.FindMethodByName(parentAsmId, parentTypeToken, interfaceMethodName);
                                if (implMethodToken != 0)
                                {
                                    classAsmId = parentAsmId;  // Update to use parent's assembly for compilation
                                    DebugConsole.Write("[JitStubs] Found in base class, token=0x");
                                    DebugConsole.WriteHex(implMethodToken);
                                    DebugConsole.WriteLine();
                                }
                            }
                            parentMT = parentMT->GetParentType();
                        }
                    }

                    if (implMethodToken == 0)
                    {
                        DebugConsole.WriteLine("[JitStubs] No implementing method found in class hierarchy");
                        continue;
                    }

                    DebugConsole.Write("[JitStubs] Found implementing method: 0x");
                    DebugConsole.WriteHex(implMethodToken);
                    DebugConsole.Write(" asm ");
                    DebugConsole.WriteDecimal(classAsmId);
                    DebugConsole.WriteLine();

                    // Set type context for generic instantiation
                    // Get all type arguments from cache for proper multi-arg generic support
                    MethodTable* genDefMT = AssemblyLoader.GetGenericDefinitionMT(mt);
                    bool hasTypeContext = false;
                    if (genDefMT != null)
                    {
                        MethodTable** typeArgs = stackalloc MethodTable*[4];  // Support up to 4 type args
                        int typeArgCount;

                        if (AssemblyLoader.GetGenericInstTypeArgs(mt, typeArgs, out typeArgCount) && typeArgCount > 0)
                        {
                            MetadataIntegration.SetTypeTypeArgs(typeArgs, typeArgCount);
                            hasTypeContext = true;
                            DebugConsole.Write("[JitStubs] Set type context: ");
                            DebugConsole.WriteDecimal((uint)typeArgCount);
                            DebugConsole.Write(" args");
                            DebugConsole.WriteLine();
                        }
                        else if (mt->GetFirstTypeArgument() != null)
                        {
                            // Fallback: use first type arg from _relatedType
                            typeArgs[0] = mt->GetFirstTypeArgument();
                            MetadataIntegration.SetTypeTypeArgs(typeArgs, 1);
                            hasTypeContext = true;
                            DebugConsole.Write("[JitStubs] Set type context (single arg): T=0x");
                            DebugConsole.WriteHex((ulong)typeArgs[0]);
                            DebugConsole.WriteLine();
                        }
                    }

                    // Compile the implementing method
                    var implResult = Tier0JIT.CompileMethod(classAsmId, implMethodToken);

                    // Clear type context after compilation
                    if (hasTypeContext)
                    {
                        MetadataIntegration.ClearTypeTypeArgs();
                    }

                    if (implResult.Success && implResult.CodeAddress != null)
                    {
                        DebugConsole.Write("[JitStubs] Impl compiled at 0x");
                        DebugConsole.WriteHex((ulong)implResult.CodeAddress);
                        DebugConsole.WriteLine();
                        return (nint)implResult.CodeAddress;
                    }
                    else
                    {
                        DebugConsole.WriteLine("[JitStubs] Failed to compile implementing method");
                    }
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
