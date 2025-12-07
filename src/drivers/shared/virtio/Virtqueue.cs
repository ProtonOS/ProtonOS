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
        _queueIndex = queueIndex;
        _queueSize = queueSize;
        _descData = new void*[queueSize];

        AllocateBuffers();
        InitializeFreeList();
    }

    private void AllocateBuffers()
    {
        // Calculate sizes
        // Descriptor table: 16 bytes per entry
        ulong descSize = (ulong)_queueSize * 16;
        // Available ring: 4 bytes header + 2 bytes per entry + 2 bytes used_event
        ulong availSize = 4 + (ulong)_queueSize * 2 + 2;
        // Used ring: 4 bytes header + 8 bytes per entry + 2 bytes avail_event
        ulong usedSize = 4 + (ulong)_queueSize * 8 + 2;

        // Allocate aligned buffers
        _descBuffer = DMA.Allocate(descSize);
        _availBuffer = DMA.Allocate(availSize);
        _usedBuffer = DMA.Allocate(usedSize);

        // Zero buffers
        DMA.Zero(_descBuffer);
        DMA.Zero(_availBuffer);
        DMA.Zero(_usedBuffer);

        // Set up pointers
        _desc = (VirtqDesc*)_descBuffer.VirtualAddress;

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
        // Initialize free list as a linked list through descriptors
        for (ushort i = 0; i < _queueSize; i++)
        {
            _desc[i].Next = (ushort)(i + 1);
        }
        _desc[_queueSize - 1].Next = 0xFFFF; // End of list

        _freeHead = 0;
        _numFree = _queueSize;
        _lastUsedIdx = 0;
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

        // Memory barrier before updating index
        // In C# we rely on volatile semantics
        System.Threading.Thread.MemoryBarrier();

        *_availIdx = (ushort)(idx + 1);
    }

    /// <summary>
    /// Check if there are completed buffers in the used ring.
    /// </summary>
    public bool HasUsedBuffers()
    {
        // Memory barrier to ensure we see latest used_idx
        System.Threading.Thread.MemoryBarrier();
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
        System.Threading.Thread.MemoryBarrier();
        return (*_usedFlags & (ushort)VirtqUsedFlags.NoNotify) == 0;
    }

    public void Dispose()
    {
        DMA.Free(ref _descBuffer);
        DMA.Free(ref _availBuffer);
        DMA.Free(ref _usedBuffer);
    }
}
