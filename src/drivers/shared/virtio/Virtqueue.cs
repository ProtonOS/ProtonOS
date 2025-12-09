// ProtonOS Virtio Driver - Virtqueue Implementation
// Split virtqueue (classic) implementation per VirtIO 1.2 spec

using System;
using System.Runtime.InteropServices;
using ProtonOS.DDK.Platform;

namespace ProtonOS.Drivers.Virtio;

/// <summary>
/// Virtqueue descriptor flags.
/// </summary>
[Flags]
public enum VirtqDescFlags : ushort
{
    None = 0,

    /// <summary>Buffer continues in next descriptor.</summary>
    Next = 1,

    /// <summary>Buffer is write-only (for device).</summary>
    Write = 2,

    /// <summary>Buffer contains a list of indirect descriptors.</summary>
    Indirect = 4,
}

/// <summary>
/// Virtqueue descriptor entry.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct VirtqDesc
{
    /// <summary>Physical address of buffer.</summary>
    public ulong Addr;

    /// <summary>Length of buffer.</summary>
    public uint Len;

    /// <summary>Flags (VirtqDescFlags).</summary>
    public ushort Flags;

    /// <summary>Index of next descriptor if NEXT flag set.</summary>
    public ushort Next;
}

/// <summary>
/// Available ring flags.
/// </summary>
[Flags]
public enum VirtqAvailFlags : ushort
{
    None = 0,

    /// <summary>Don't interrupt when buffer consumed.</summary>
    NoInterrupt = 1,
}

/// <summary>
/// Used ring flags.
/// </summary>
[Flags]
public enum VirtqUsedFlags : ushort
{
    None = 0,

    /// <summary>Don't notify when buffer added.</summary>
    NoNotify = 1,
}

/// <summary>
/// Used ring element.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct VirtqUsedElem
{
    /// <summary>Index of start of used descriptor chain.</summary>
    public uint Id;

    /// <summary>Total length written to descriptors.</summary>
    public uint Len;
}

/// <summary>
/// Split virtqueue implementation.
/// </summary>
public unsafe class Virtqueue : IDisposable
{
    // Queue index
    private readonly ushort _queueIndex;

    // Queue size (number of descriptors)
    private readonly ushort _queueSize;

    // DMA buffers for queue structures
    private DMABuffer _descBuffer;
    private DMABuffer _availBuffer;
    private DMABuffer _usedBuffer;

    // Pointers to queue structures
    private VirtqDesc* _desc;
    private ushort* _availFlags;
    private ushort* _availIdx;
    private ushort* _availRing;
    private ushort* _usedFlags;
    private ushort* _usedIdx;
    private VirtqUsedElem* _usedRing;

    // Free descriptor tracking
    private ushort _freeHead;
    private ushort _numFree;

    // Last seen used index (for processing completions)
    private ushort _lastUsedIdx;

    // Callback data storage (one per descriptor)
    private void*[] _descData;

    /// <summary>
    /// Queue index.
    /// </summary>
    public ushort QueueIndex => _queueIndex;

    /// <summary>
    /// Queue size.
    /// </summary>
    public ushort QueueSize => _queueSize;

    /// <summary>
    /// Physical address of descriptor table.
    /// </summary>
    public ulong DescPhysAddr => _descBuffer.PhysicalAddress;

    /// <summary>
    /// Physical address of available ring.
    /// </summary>
    public ulong AvailPhysAddr => _availBuffer.PhysicalAddress;

    /// <summary>
    /// Physical address of used ring.
    /// </summary>
    public ulong UsedPhysAddr => _usedBuffer.PhysicalAddress;

    /// <summary>
    /// Number of free descriptors.
    /// </summary>
    public int FreeDescriptorCount => _numFree;

    /// <summary>
    /// Create a new virtqueue.
    /// </summary>
    public Virtqueue(ushort queueIndex, ushort queueSize)
    {
        ProtonOS.DDK.Kernel.Debug.WriteHex(0xAAA00001u); // Virtqueue ctor entry
        _queueIndex = queueIndex;
        _queueSize = queueSize;
        ProtonOS.DDK.Kernel.Debug.WriteHex(0xAAA00002u); // Before newarr
        _descData = new void*[queueSize];
        ProtonOS.DDK.Kernel.Debug.WriteHex(0xAAA00003u); // After newarr

        AllocateBuffers();
        ProtonOS.DDK.Kernel.Debug.WriteHex(0xAAA00004u); // After AllocateBuffers
        InitializeFreeList();
        ProtonOS.DDK.Kernel.Debug.WriteHex(0xAAA00005u); // After InitializeFreeList (ctor done)
    }

    private void AllocateBuffers()
    {
        ProtonOS.DDK.Kernel.Debug.WriteHex(0xBBB00001u); // AllocateBuffers entry
        // Calculate sizes
        // Descriptor table: 16 bytes per entry
        ProtonOS.DDK.Kernel.Debug.WriteHex(0xBBB00002u); // Before reading _queueSize
        ushort qs = _queueSize;
        ProtonOS.DDK.Kernel.Debug.WriteHex(0xBBB00003u); // After reading _queueSize
        ProtonOS.DDK.Kernel.Debug.WriteHex((uint)qs);
        ulong descSize = (ulong)qs * 16;
        // Available ring: 4 bytes header + 2 bytes per entry + 2 bytes used_event
        ulong availSize = 4 + (ulong)_queueSize * 2 + 2;
        // Used ring: 4 bytes header + 8 bytes per entry + 2 bytes avail_event
        ulong usedSize = 4 + (ulong)_queueSize * 8 + 2;

        // Allocate aligned buffers
        ProtonOS.DDK.Kernel.Debug.WriteHex(0xBBB10001u); // Before DMA.Allocate for desc
        DMABuffer descBuf = DMA.Allocate(descSize);
        ProtonOS.DDK.Kernel.Debug.WriteHex(0xBBB10002u); // After DMA.Allocate for desc
        ProtonOS.DDK.Kernel.Debug.WriteHex((uint)(descBuf.PhysicalAddress >> 32));
        ProtonOS.DDK.Kernel.Debug.WriteHex((uint)descBuf.PhysicalAddress);
        ProtonOS.DDK.Kernel.Debug.WriteHex((uint)((ulong)descBuf.VirtualAddress >> 32));
        ProtonOS.DDK.Kernel.Debug.WriteHex((uint)(ulong)descBuf.VirtualAddress);

        ProtonOS.DDK.Kernel.Debug.WriteHex(0xBBB10003u); // Before stfld _descBuffer
        _descBuffer = descBuf;
        ProtonOS.DDK.Kernel.Debug.WriteHex(0xBBB10004u); // After stfld _descBuffer

        // Re-read to verify
        ProtonOS.DDK.Kernel.Debug.WriteHex(0xBBB10005u); // Reading back _descBuffer
        DMABuffer check = _descBuffer;
        ProtonOS.DDK.Kernel.Debug.WriteHex((uint)(check.PhysicalAddress >> 32));
        ProtonOS.DDK.Kernel.Debug.WriteHex((uint)check.PhysicalAddress);
        ProtonOS.DDK.Kernel.Debug.WriteHex((uint)((ulong)check.VirtualAddress >> 32));
        ProtonOS.DDK.Kernel.Debug.WriteHex((uint)(ulong)check.VirtualAddress);

        _availBuffer = DMA.Allocate(availSize);
        _usedBuffer = DMA.Allocate(usedSize);

        // Zero buffers
        DMA.Zero(_descBuffer);
        DMA.Zero(_availBuffer);
        DMA.Zero(_usedBuffer);

        // Set up pointers
        ProtonOS.DDK.Kernel.Debug.WriteHex(0xBBB20001u); // Before setting _desc
        _desc = (VirtqDesc*)_descBuffer.VirtualAddress;
        ProtonOS.DDK.Kernel.Debug.WriteHex(0xBBB20002u); // After setting _desc

        // Read back _desc to verify
        ProtonOS.DDK.Kernel.Debug.WriteHex(0xBBB20003u); // Reading back _desc
        VirtqDesc* descPtr = _desc;
        ProtonOS.DDK.Kernel.Debug.WriteHex((uint)((ulong)descPtr >> 32));
        ProtonOS.DDK.Kernel.Debug.WriteHex((uint)(ulong)descPtr);

        byte* avail = (byte*)_availBuffer.VirtualAddress;
        _availFlags = (ushort*)avail;
        _availIdx = (ushort*)(avail + 2);
        _availRing = (ushort*)(avail + 4);

        byte* used = (byte*)_usedBuffer.VirtualAddress;
        _usedFlags = (ushort*)used;
        _usedIdx = (ushort*)(used + 2);
        _usedRing = (VirtqUsedElem*)(used + 4);
    }

    private void InitializeFreeList()
    {
        ProtonOS.DDK.Kernel.Debug.WriteHex(0xCCC00001u); // InitializeFreeList entry
        // Initialize free list as a linked list through descriptors
        ProtonOS.DDK.Kernel.Debug.WriteHex(0xCCC00002u); // Before reading _queueSize
        ushort qs = _queueSize;
        ProtonOS.DDK.Kernel.Debug.WriteHex(0xCCC00003u); // After reading _queueSize
        ProtonOS.DDK.Kernel.Debug.WriteHex((uint)qs);

        ProtonOS.DDK.Kernel.Debug.WriteHex(0xCCC00004u); // Before reading _desc
        VirtqDesc* desc = _desc;
        ProtonOS.DDK.Kernel.Debug.WriteHex(0xCCC00005u); // After reading _desc
        ProtonOS.DDK.Kernel.Debug.WriteHex((uint)((ulong)desc >> 32));
        ProtonOS.DDK.Kernel.Debug.WriteHex((uint)(ulong)desc);

        ProtonOS.DDK.Kernel.Debug.WriteHex(0xCCC00010u); // Before loop
        for (ushort i = 0; i < qs; i++)
        {
            desc[i].Next = (ushort)(i + 1);
        }
        ProtonOS.DDK.Kernel.Debug.WriteHex(0xCCC00006u); // After loop

        // Re-read desc to see if it changed
        ProtonOS.DDK.Kernel.Debug.WriteHex(0xCCC00011u); // Re-reading _desc
        VirtqDesc* desc2 = _desc;
        ProtonOS.DDK.Kernel.Debug.WriteHex((uint)((ulong)desc2 >> 32));
        ProtonOS.DDK.Kernel.Debug.WriteHex((uint)(ulong)desc2);

        desc2[qs - 1].Next = 0xFFFF; // End of list

        _freeHead = 0;
        _numFree = qs;
        _lastUsedIdx = 0;
        ProtonOS.DDK.Kernel.Debug.WriteHex(0xCCC00007u); // InitializeFreeList done
    }

    /// <summary>
    /// Allocate a descriptor chain.
    /// </summary>
    /// <param name="count">Number of descriptors needed</param>
    /// <returns>First descriptor index, or -1 if not enough</returns>
    public int AllocateDescriptors(int count)
    {
        if (count <= 0 || count > _numFree)
            return -1;

        int head = _freeHead;
        int current = _freeHead;

        for (int i = 0; i < count; i++)
        {
            if (current == 0xFFFF)
                return -1; // Shouldn't happen if _numFree is accurate

            int next = _desc[current].Next;
            if (i == count - 1)
            {
                // Last descriptor in chain
                _freeHead = (ushort)next;
            }
            current = next;
        }

        _numFree -= (ushort)count;
        return head;
    }

    /// <summary>
    /// Free a descriptor chain.
    /// </summary>
    public void FreeDescriptors(int head)
    {
        if (head < 0 || head >= _queueSize)
            return;

        // Walk the chain to find the end and count
        int current = head;
        int count = 0;

        while (current != 0xFFFF && current < _queueSize)
        {
            count++;
            _descData[current] = null;

            if ((_desc[current].Flags & (ushort)VirtqDescFlags.Next) == 0)
            {
                // End of chain - link to free list
                _desc[current].Next = _freeHead;
                break;
            }
            current = _desc[current].Next;
        }

        _freeHead = (ushort)head;
        _numFree += (ushort)count;
    }

    /// <summary>
    /// Set up a descriptor.
    /// </summary>
    public void SetDescriptor(int index, ulong physAddr, uint len, VirtqDescFlags flags, ushort next)
    {
        if (index < 0 || index >= _queueSize)
            return;

        _desc[index].Addr = physAddr;
        _desc[index].Len = len;
        _desc[index].Flags = (ushort)flags;
        _desc[index].Next = next;
    }

    /// <summary>
    /// Associate data with a descriptor (for callback lookup).
    /// </summary>
    public void SetDescriptorData(int index, void* data)
    {
        if (index >= 0 && index < _queueSize)
            _descData[index] = data;
    }

    /// <summary>
    /// Get data associated with a descriptor.
    /// </summary>
    public void* GetDescriptorData(int index)
    {
        if (index >= 0 && index < _queueSize)
            return _descData[index];
        return null;
    }

    /// <summary>
    /// Submit a descriptor chain to the available ring.
    /// </summary>
    public void SubmitAvailable(int head)
    {
        if (head < 0 || head >= _queueSize)
            return;

        ushort idx = *_availIdx;
        _availRing[idx % _queueSize] = (ushort)head;

        // On x86-64, stores have release semantics so explicit barrier not needed
        // The volatile write ensures ordering for the index update
        *_availIdx = (ushort)(idx + 1);
    }

    /// <summary>
    /// Check if there are completed buffers in the used ring.
    /// </summary>
    public bool HasUsedBuffers()
    {
        // On x86-64, loads have acquire semantics so barrier not strictly needed
        // Reading through pointer already provides necessary visibility
        return *_usedIdx != _lastUsedIdx;
    }

    /// <summary>
    /// Get the next completed buffer from the used ring.
    /// </summary>
    /// <param name="length">Output: total length written by device</param>
    /// <returns>Descriptor head index, or -1 if none available</returns>
    public int PopUsed(out uint length)
    {
        length = 0;

        if (!HasUsedBuffers())
            return -1;

        int idx = _lastUsedIdx % _queueSize;
        VirtqUsedElem elem = _usedRing[idx];

        _lastUsedIdx++;
        length = elem.Len;

        return (int)elem.Id;
    }

    /// <summary>
    /// Disable interrupts for this queue.
    /// </summary>
    public void DisableInterrupts()
    {
        *_availFlags = (ushort)VirtqAvailFlags.NoInterrupt;
    }

    /// <summary>
    /// Enable interrupts for this queue.
    /// </summary>
    public void EnableInterrupts()
    {
        *_availFlags = 0;
    }

    /// <summary>
    /// Check if device wants notifications.
    /// </summary>
    public bool NeedsNotification()
    {
        // On x86-64, loads have acquire semantics
        return (*_usedFlags & (ushort)VirtqUsedFlags.NoNotify) == 0;
    }

    public void Dispose()
    {
        DMA.Free(ref _descBuffer);
        DMA.Free(ref _availBuffer);
        DMA.Free(ref _usedBuffer);
    }
}
