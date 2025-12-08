// ProtonOS kernel - Runtime helpers for JIT-compiled code
// These are fundamental runtime helpers called by JIT-generated code.
//
// Allocation helpers:
//   RhpNewFast  - Allocate a new object (newobj opcode)
//   RhpNewArray - Allocate a new single-dimensional array (newarr opcode)
//
// Multi-dimensional array helpers:
//   NewMDArray2D/3D - Allocate MD arrays
//   Get2D/3D_Int32  - Get element from MD array
//   Set2D/3D_Int32  - Set element in MD array
//   Address2D/3D    - Get address of element in MD array
//
// MD Array Layout (N dimensions):
//   [0]  MethodTable* (8 bytes)
//   [8]  Total length (4 bytes) = product of all dimensions
//   [12] Rank (4 bytes) = N
//   [16] Bounds[0..N-1] (4 bytes each)
//   [16 + 4*N] LoBounds[0..N-1] (4 bytes each) - usually all zeros
//   [16 + 8*N] Data (elements in row-major order)
//
// Header size formula: 16 + 8*N bytes (2D=32, 3D=40, 4D=48)

using ProtonOS.Memory;
using ProtonOS.Platform;
using ProtonOS.Runtime.JIT;

namespace ProtonOS.Runtime;

/// <summary>
/// Runtime helpers for JIT-compiled code.
/// These functions are called directly by JIT-generated machine code.
/// </summary>
public static unsafe class RuntimeHelpers
{
    private static bool _initialized;

    // Cached function pointers for ILCompiler
    private static void* _rhpNewFastPtr;
    private static void* _rhpNewArrayPtr;
    private static void* _isAssignableToPtr;
    private static void* _getInterfaceMethodPtr;

    // MD array header offsets (common to all ranks)
    private const int MDOffsetMethodTable = 0;
    private const int MDOffsetLength = 8;
    private const int MDOffsetRank = 12;
    private const int MDOffsetBounds = 16;

    #region Well-Known Tokens

    /// <summary>
    /// Well-known tokens for runtime helpers.
    /// These use the 0xF000xxxx range reserved for internal runtime methods.
    /// The JIT compiler resolves calls to these methods using these tokens.
    /// </summary>
    public static class Tokens
    {
        // 2D MD array helpers
        public const uint Get2D_Int32 = 0xF0001001;
        public const uint Set2D_Int32 = 0xF0001002;
        public const uint NewMDArray2D = 0xF0001003;
        public const uint Address2D = 0xF0001004;

        // 3D MD array helpers
        public const uint Get3D_Int32 = 0xF0001011;
        public const uint Set3D_Int32 = 0xF0001012;
        public const uint NewMDArray3D = 0xF0001013;
        public const uint Address3D = 0xF0001014;
    }

    #endregion

    #region Initialization

    /// <summary>
    /// Initialize all runtime helpers and register them with the JIT.
    /// Must be called during kernel initialization before JIT compilation.
    /// </summary>
    public static void Init()
    {
        if (_initialized)
            return;

        DebugConsole.WriteLine("[RuntimeHelpers] Initializing...");

        // Cache allocation helper function pointers for the ILCompiler
        _rhpNewFastPtr = (void*)(delegate*<MethodTable*, void*>)&RhpNewFast;
        _rhpNewArrayPtr = (void*)(delegate*<MethodTable*, int, void*>)&RhpNewArray;

        // Cache type helper function pointers for castclass/isinst
        _isAssignableToPtr = (void*)(delegate*<MethodTable*, MethodTable*, bool>)&TypeHelpers.IsAssignableTo;
        _getInterfaceMethodPtr = (void*)(delegate*<void*, MethodTable*, int, void*>)&TypeHelpers.GetInterfaceMethod;

        // Cache debug helper pointers
        _debugStfldPtr = (void*)(delegate*<void*, int, void>)&DebugStfld;
        _debugStelemStackPtr = (void*)(delegate*<void*, ulong, void*, int, void>)&DebugStelemStack;

        // Register MD array helpers with the CompiledMethodRegistry
        RegisterMDArrayHelpers();

        _initialized = true;
        DebugConsole.WriteLine("[RuntimeHelpers] Initialized (2 alloc + 2 type + 8 MD array helpers)");
    }

    private static void RegisterMDArrayHelpers()
    {
        // 2D array helpers
        CompiledMethodRegistry.Register(
            Tokens.Get2D_Int32,
            (delegate*<void*, int, int, int>)&Get2D_Int32,
            3, ReturnKind.Int32, false);

        CompiledMethodRegistry.Register(
            Tokens.Set2D_Int32,
            (delegate*<void*, int, int, int, void>)&Set2D_Int32,
            4, ReturnKind.Void, false);

        CompiledMethodRegistry.Register(
            Tokens.NewMDArray2D,
            (delegate*<MethodTable*, int, int, void*>)&NewMDArray2D,
            3, ReturnKind.IntPtr, false);

        CompiledMethodRegistry.Register(
            Tokens.Address2D,
            (delegate*<void*, int, int, int, void*>)&Address2D,
            4, ReturnKind.IntPtr, false);

        // 3D array helpers
        CompiledMethodRegistry.Register(
            Tokens.Get3D_Int32,
            (delegate*<void*, int, int, int, int>)&Get3D_Int32,
            4, ReturnKind.Int32, false);

        CompiledMethodRegistry.Register(
            Tokens.Set3D_Int32,
            (delegate*<void*, int, int, int, int, void>)&Set3D_Int32,
            5, ReturnKind.Void, false);

        CompiledMethodRegistry.Register(
            Tokens.NewMDArray3D,
            (delegate*<MethodTable*, int, int, int, void*>)&NewMDArray3D,
            4, ReturnKind.IntPtr, false);

        CompiledMethodRegistry.Register(
            Tokens.Address3D,
            (delegate*<void*, int, int, int, int, void*>)&Address3D,
            5, ReturnKind.IntPtr, false);
    }

    #endregion

    #region Allocation Helper Getters

    /// <summary>
    /// Get the RhpNewFast function pointer for the ILCompiler.
    /// </summary>
    public static void* GetRhpNewFastPtr() => _rhpNewFastPtr;

    /// <summary>
    /// Get the RhpNewArray function pointer for the ILCompiler.
    /// </summary>
    public static void* GetRhpNewArrayPtr() => _rhpNewArrayPtr;

    /// <summary>
    /// Get the IsAssignableTo function pointer for the ILCompiler.
    /// Signature: bool IsAssignableTo(MethodTable* objectMT, MethodTable* targetMT)
    /// </summary>
    public static void* GetIsAssignableToPtr() => _isAssignableToPtr;

    /// <summary>
    /// Get the GetInterfaceMethod function pointer for the ILCompiler.
    /// Signature: void* GetInterfaceMethod(void* obj, MethodTable* interfaceMT, int methodIndex)
    /// </summary>
    public static void* GetInterfaceMethodPtr() => _getInterfaceMethodPtr;

    // Debug helper pointer
    private static void* _debugStfldPtr;

    /// <summary>
    /// Get the debug stfld function pointer for tracing.
    /// Signature: void DebugStfld(void* objPtr, int offset)
    /// </summary>
    public static void* GetDebugStfldPtr() => _debugStfldPtr;

    #endregion

    #region Debug Helpers

    /// <summary>
    /// Debug helper called from JIT code to trace stfld operations.
    /// Shows the actual runtime object pointer and offset before the store.
    /// </summary>
    public static void DebugStfld(void* objPtr, int offset)
    {
        DebugConsole.Write("[stfld RT] obj=0x");
        DebugConsole.WriteHex((ulong)objPtr);
        DebugConsole.Write(" off=");
        DebugConsole.WriteDecimal((uint)offset);
        DebugConsole.WriteLine();
    }

    // Debug helper pointer for stelem
    private static void* _debugStelemStackPtr;

    /// <summary>
    /// Get the debug stelem function pointer for tracing stack values.
    /// Signature: void DebugStelemStack(void* srcAddr, ulong index, void* array, int elemSize)
    /// </summary>
    public static void* GetDebugStelemStackPtr() => _debugStelemStackPtr;

    /// <summary>
    /// Debug helper called from JIT code to trace stelem stack values.
    /// Shows the actual runtime stack values before stelem executes.
    /// </summary>
    public static void DebugStelemStack(void* srcAddr, ulong index, void* array, int elemSize)
    {
        DebugConsole.Write("[stelem stack] src=0x");
        DebugConsole.WriteHex((ulong)srcAddr);
        DebugConsole.Write(" idx=");
        DebugConsole.WriteHex(index);
        DebugConsole.Write(" arr=0x");
        DebugConsole.WriteHex((ulong)array);
        DebugConsole.Write(" dest=0x");
        DebugConsole.WriteHex((ulong)((byte*)array + 16 + (long)index * elemSize));
        DebugConsole.WriteLine();
    }

    #endregion

    #region Object Allocation (newobj)

    /// <summary>
    /// Allocate a new object.
    /// Signature: void* RhpNewFast(MethodTable* pMT)
    ///
    /// Called by the JIT for the 'newobj' opcode.
    /// Allocates BaseSize bytes and sets the MethodTable pointer.
    /// </summary>
    public static void* RhpNewFast(MethodTable* pMT)
    {
        if (pMT == null)
        {
            DebugConsole.WriteLine("[RhpNewFast] ERROR: null MethodTable!");
            return null;
        }

        DebugConsole.Write("[RhpNewFast] MT=0x");
        DebugConsole.WriteHex((ulong)pMT);
        DebugConsole.Write(" size=");
        DebugConsole.WriteDecimal(pMT->BaseSize);

        byte* result = (byte*)GCHeap.Alloc(pMT->BaseSize);
        if (result == null)
        {
            DebugConsole.WriteLine(" ALLOC FAILED!");
            return null;
        }

        *(MethodTable**)result = pMT;

        DebugConsole.Write(" -> 0x");
        DebugConsole.WriteHex((ulong)result);
        DebugConsole.WriteLine();

        return result;
    }

    #endregion

    #region Single-Dimensional Array Allocation (newarr)

    /// <summary>
    /// Allocate a new single-dimensional, zero-indexed array.
    /// Signature: void* RhpNewArray(MethodTable* pMT, int numElements)
    ///
    /// Called by the JIT for the 'newarr' opcode.
    /// Array layout:
    ///   [0]  MethodTable* (8 bytes)
    ///   [8]  Length (4 bytes, as int)
    ///   [12] Padding (4 bytes on x64)
    ///   [16] Data[0..numElements-1]
    /// </summary>
    public static void* RhpNewArray(MethodTable* pMT, int numElements)
    {
        DebugConsole.Write("[RhpNewArray] MT=0x");
        DebugConsole.WriteHex((ulong)pMT);
        DebugConsole.Write(" count=");
        DebugConsole.WriteDecimal((uint)numElements);

        if (pMT == null || numElements < 0)
        {
            DebugConsole.WriteLine(" INVALID!");
            return null;
        }

        uint totalSize = pMT->BaseSize + (uint)numElements * pMT->ComponentSize;

        DebugConsole.Write(" size=");
        DebugConsole.WriteDecimal(totalSize);

        byte* result = (byte*)GCHeap.Alloc(totalSize);
        if (result == null)
        {
            DebugConsole.WriteLine(" ALLOC FAILED!");
            return null;
        }

        *(MethodTable**)result = pMT;
        *(int*)(result + 8) = numElements;

        DebugConsole.Write(" -> 0x");
        DebugConsole.WriteHex((ulong)result);
        DebugConsole.WriteLine();

        return result;
    }

    #endregion

    #region 2D Multi-Dimensional Array Helpers

    /// <summary>
    /// Allocate a 2D multi-dimensional array.
    /// Signature: void* NewMDArray2D(MethodTable* pMT, int dim0, int dim1)
    /// </summary>
    public static void* NewMDArray2D(MethodTable* pMT, int dim0, int dim1)
    {
        const int Rank = 2;
        const int HeaderSize = 16 + 8 * Rank;  // 32 bytes

        int totalElements = dim0 * dim1;
        int dataSize = totalElements * pMT->ComponentSize;
        uint totalSize = (uint)(HeaderSize + dataSize);

        byte* result = (byte*)GCHeap.Alloc(totalSize);
        if (result == null)
            return null;

        *(MethodTable**)result = pMT;
        *(int*)(result + MDOffsetLength) = totalElements;
        *(int*)(result + MDOffsetRank) = Rank;
        *(int*)(result + MDOffsetBounds + 0) = dim0;
        *(int*)(result + MDOffsetBounds + 4) = dim1;
        *(int*)(result + MDOffsetBounds + 8) = 0;   // loBound0
        *(int*)(result + MDOffsetBounds + 12) = 0;  // loBound1

        return result;
    }

    /// <summary>
    /// Get element from 2D array (int element type).
    /// Signature: int Get2D_Int32(void* array, int i, int j)
    /// </summary>
    public static int Get2D_Int32(void* array, int i, int j)
    {
        byte* arr = (byte*)array;
        int dim1 = *(int*)(arr + MDOffsetBounds + 4);
        int index = i * dim1 + j;
        int offset = 32 + index * 4;  // HeaderSize=32, sizeof(int)=4
        return *(int*)(arr + offset);
    }

    /// <summary>
    /// Set element in 2D array (int element type).
    /// Signature: void Set2D_Int32(void* array, int i, int j, int value)
    /// </summary>
    public static void Set2D_Int32(void* array, int i, int j, int value)
    {
        byte* arr = (byte*)array;
        int dim1 = *(int*)(arr + MDOffsetBounds + 4);
        int index = i * dim1 + j;
        int offset = 32 + index * 4;
        *(int*)(arr + offset) = value;
    }

    /// <summary>
    /// Get address of element in 2D array.
    /// Signature: void* Address2D(void* array, int i, int j, int elemSize)
    /// </summary>
    public static void* Address2D(void* array, int i, int j, int elemSize)
    {
        byte* arr = (byte*)array;
        int dim1 = *(int*)(arr + MDOffsetBounds + 4);
        int index = i * dim1 + j;
        int offset = 32 + index * elemSize;
        return arr + offset;
    }

    #endregion

    #region 3D Multi-Dimensional Array Helpers

    /// <summary>
    /// Allocate a 3D multi-dimensional array.
    /// Signature: void* NewMDArray3D(MethodTable* pMT, int dim0, int dim1, int dim2)
    /// </summary>
    public static void* NewMDArray3D(MethodTable* pMT, int dim0, int dim1, int dim2)
    {
        const int Rank = 3;
        const int HeaderSize = 16 + 8 * Rank;  // 40 bytes

        int totalElements = dim0 * dim1 * dim2;
        int dataSize = totalElements * pMT->ComponentSize;
        uint totalSize = (uint)(HeaderSize + dataSize);

        byte* result = (byte*)GCHeap.Alloc(totalSize);
        if (result == null)
            return null;

        *(MethodTable**)result = pMT;
        *(int*)(result + MDOffsetLength) = totalElements;
        *(int*)(result + MDOffsetRank) = Rank;
        *(int*)(result + MDOffsetBounds + 0) = dim0;
        *(int*)(result + MDOffsetBounds + 4) = dim1;
        *(int*)(result + MDOffsetBounds + 8) = dim2;
        *(int*)(result + MDOffsetBounds + 12) = 0;  // loBound0
        *(int*)(result + MDOffsetBounds + 16) = 0;  // loBound1
        *(int*)(result + MDOffsetBounds + 20) = 0;  // loBound2

        return result;
    }

    /// <summary>
    /// Get element from 3D array (int element type).
    /// Signature: int Get3D_Int32(void* array, int i, int j, int k)
    /// </summary>
    public static int Get3D_Int32(void* array, int i, int j, int k)
    {
        byte* arr = (byte*)array;
        int dim1 = *(int*)(arr + MDOffsetBounds + 4);
        int dim2 = *(int*)(arr + MDOffsetBounds + 8);
        int index = (i * dim1 + j) * dim2 + k;
        int offset = 40 + index * 4;  // HeaderSize=40, sizeof(int)=4
        return *(int*)(arr + offset);
    }

    /// <summary>
    /// Set element in 3D array (int element type).
    /// Signature: void Set3D_Int32(void* array, int i, int j, int k, int value)
    /// </summary>
    public static void Set3D_Int32(void* array, int i, int j, int k, int value)
    {
        byte* arr = (byte*)array;
        int dim1 = *(int*)(arr + MDOffsetBounds + 4);
        int dim2 = *(int*)(arr + MDOffsetBounds + 8);
        int index = (i * dim1 + j) * dim2 + k;
        int offset = 40 + index * 4;
        *(int*)(arr + offset) = value;
    }

    /// <summary>
    /// Get address of element in 3D array.
    /// Signature: void* Address3D(void* array, int i, int j, int k, int elemSize)
    /// </summary>
    public static void* Address3D(void* array, int i, int j, int k, int elemSize)
    {
        byte* arr = (byte*)array;
        int dim1 = *(int*)(arr + MDOffsetBounds + 4);
        int dim2 = *(int*)(arr + MDOffsetBounds + 8);
        int index = (i * dim1 + j) * dim2 + k;
        int offset = 40 + index * elemSize;
        return arr + offset;
    }

    #endregion

    #region MD Array Utility Methods

    /// <summary>
    /// Calculate the header size for an MD array of given rank.
    /// </summary>
    public static int GetMDArrayHeaderSize(int rank) => 16 + 8 * rank;

    /// <summary>
    /// Get the offset to LoBounds array for a given rank.
    /// </summary>
    public static int GetMDArrayLoBoundsOffset(int rank) => MDOffsetBounds + 4 * rank;

    /// <summary>
    /// Get the offset to data for a given rank.
    /// </summary>
    public static int GetMDArrayDataOffset(int rank) => 16 + 8 * rank;

    /// <summary>
    /// Calculate the linear index for an N-dimensional array access.
    /// indices must have exactly 'rank' elements.
    /// </summary>
    public static int CalculateMDArrayIndex(byte* array, int* indices, int rank)
    {
        int index = indices[0];
        for (int d = 1; d < rank; d++)
        {
            int dimSize = *(int*)(array + MDOffsetBounds + d * 4);
            index = index * dimSize + indices[d];
        }
        return index;
    }

    /// <summary>
    /// Get the total length of an MD array.
    /// </summary>
    public static int GetMDArrayLength(void* array) =>
        *(int*)((byte*)array + MDOffsetLength);

    /// <summary>
    /// Get the rank of an MD array.
    /// </summary>
    public static int GetMDArrayRank(void* array) =>
        *(int*)((byte*)array + MDOffsetRank);

    /// <summary>
    /// Get a specific dimension's bound (length).
    /// </summary>
    public static int GetMDArrayDimensionLength(void* array, int dimension) =>
        *(int*)((byte*)array + MDOffsetBounds + dimension * 4);

    /// <summary>
    /// Get a specific dimension's lower bound.
    /// </summary>
    public static int GetMDArrayLowerBound(void* array, int dimension)
    {
        int rank = GetMDArrayRank(array);
        return *(int*)((byte*)array + MDOffsetBounds + rank * 4 + dimension * 4);
    }

    #endregion
}
