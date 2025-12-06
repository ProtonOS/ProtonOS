// ProtonOS DDK - DMA Buffer Management
// Helpers for allocating and managing DMA-capable buffers.

using System;
using ProtonOS.DDK.Kernel;

namespace ProtonOS.DDK.Platform;

/// <summary>
/// DMA buffer for device I/O.
/// Provides both physical address (for hardware) and virtual address (for software).
/// </summary>
public unsafe struct DMABuffer : IDisposable
{
    /// <summary>Physical address for hardware programming.</summary>
    public ulong PhysicalAddress;

    /// <summary>Virtual address for software access.</summary>
    public void* VirtualAddress;

    /// <summary>Size in bytes.</summary>
    public ulong Size;

    /// <summary>Number of pages.</summary>
    public ulong PageCount;

    /// <summary>True if this buffer is valid.</summary>
    public bool IsValid => VirtualAddress != null;

    /// <summary>
    /// Access buffer as byte pointer.
    /// </summary>
    public byte* AsBytes => (byte*)VirtualAddress;

    /// <summary>
    /// Free the DMA buffer.
    /// </summary>
    public void Dispose()
    {
        if (VirtualAddress != null)
        {
            Memory.FreePages(PhysicalAddress, PageCount);
            VirtualAddress = null;
            PhysicalAddress = 0;
            Size = 0;
            PageCount = 0;
        }
    }
}

/// <summary>
/// DMA buffer allocation and management.
/// </summary>
public static unsafe class DMA
{
    /// <summary>
    /// Page size (4KB).
    /// </summary>
    public const ulong PageSize = 4096;

    /// <summary>
    /// Allocate a DMA buffer of the specified size.
    /// </summary>
    /// <param name="size">Size in bytes</param>
    /// <returns>DMA buffer, or invalid buffer on failure</returns>
    public static DMABuffer Allocate(ulong size)
    {
        if (size == 0)
            return default;

        ulong pageCount = (size + PageSize - 1) / PageSize;
        ulong physAddr = Memory.AllocatePages(pageCount);

        if (physAddr == 0)
            return default;

        ulong virtAddr = Memory.PhysToVirt(physAddr);

        return new DMABuffer
        {
            PhysicalAddress = physAddr,
            VirtualAddress = (void*)virtAddr,
            Size = pageCount * PageSize,
            PageCount = pageCount,
        };
    }

    /// <summary>
    /// Allocate a single-page DMA buffer.
    /// </summary>
    public static DMABuffer AllocatePage()
    {
        return Allocate(PageSize);
    }

    /// <summary>
    /// Allocate a DMA buffer from a specific NUMA node.
    /// </summary>
    public static DMABuffer AllocateFromNode(ulong size, uint numaNode)
    {
        if (size == 0)
            return default;

        ulong pageCount = (size + PageSize - 1) / PageSize;
        ulong physAddr = NUMA.AllocatePagesFromNode(pageCount, numaNode);

        if (physAddr == 0)
            return default;

        ulong virtAddr = Memory.PhysToVirt(physAddr);

        return new DMABuffer
        {
            PhysicalAddress = physAddr,
            VirtualAddress = (void*)virtAddr,
            Size = pageCount * PageSize,
            PageCount = pageCount,
        };
    }

    /// <summary>
    /// Allocate a DMA buffer from the current CPU's NUMA node.
    /// </summary>
    public static DMABuffer AllocateLocal(ulong size)
    {
        if (size == 0)
            return default;

        if (!NUMA.IsAvailable())
            return Allocate(size); // Fall back to regular allocation

        return AllocateFromNode(size, NUMA.GetCurrentNode());
    }

    /// <summary>
    /// Free a DMA buffer.
    /// </summary>
    public static void Free(ref DMABuffer buffer)
    {
        buffer.Dispose();
    }

    /// <summary>
    /// Zero a DMA buffer.
    /// </summary>
    public static void Zero(DMABuffer buffer)
    {
        if (!buffer.IsValid)
            return;

        // Use native memory zero
        byte* ptr = buffer.AsBytes;
        ulong size = buffer.Size;

        // Zero in 8-byte chunks for efficiency
        ulong* p64 = (ulong*)ptr;
        ulong count64 = size / 8;
        for (ulong i = 0; i < count64; i++)
            p64[i] = 0;

        // Zero remaining bytes
        ulong remaining = size % 8;
        byte* tail = ptr + (count64 * 8);
        for (ulong i = 0; i < remaining; i++)
            tail[i] = 0;
    }

    /// <summary>
    /// Copy data into a DMA buffer.
    /// </summary>
    public static void CopyTo(DMABuffer buffer, void* source, ulong length)
    {
        if (!buffer.IsValid || source == null || length == 0)
            return;

        if (length > buffer.Size)
            length = buffer.Size;

        byte* dst = buffer.AsBytes;
        byte* src = (byte*)source;

        // Copy in 8-byte chunks
        ulong count64 = length / 8;
        ulong* d64 = (ulong*)dst;
        ulong* s64 = (ulong*)src;
        for (ulong i = 0; i < count64; i++)
            d64[i] = s64[i];

        // Copy remaining bytes
        ulong remaining = length % 8;
        byte* dTail = dst + (count64 * 8);
        byte* sTail = src + (count64 * 8);
        for (ulong i = 0; i < remaining; i++)
            dTail[i] = sTail[i];
    }

    /// <summary>
    /// Copy data from a DMA buffer.
    /// </summary>
    public static void CopyFrom(DMABuffer buffer, void* destination, ulong length)
    {
        if (!buffer.IsValid || destination == null || length == 0)
            return;

        if (length > buffer.Size)
            length = buffer.Size;

        byte* src = buffer.AsBytes;
        byte* dst = (byte*)destination;

        // Copy in 8-byte chunks
        ulong count64 = length / 8;
        ulong* d64 = (ulong*)dst;
        ulong* s64 = (ulong*)src;
        for (ulong i = 0; i < count64; i++)
            d64[i] = s64[i];

        // Copy remaining bytes
        ulong remaining = length % 8;
        byte* dTail = dst + (count64 * 8);
        byte* sTail = src + (count64 * 8);
        for (ulong i = 0; i < remaining; i++)
            dTail[i] = sTail[i];
    }
}
