// ProtonOS kernel - PAL XState APIs
// Win32-compatible XSAVE/XState management for PAL compatibility.
// These manage extended processor state (SSE, AVX, etc.) in context structures.

using System.Runtime.InteropServices;

namespace ProtonOS.PAL;

/// <summary>
/// XSAVE feature flags returned by GetEnabledXStateFeatures.
/// </summary>
public static class XStateFeatures
{
    public const ulong XSTATE_MASK_LEGACY_FLOATING_POINT = 0x00000001;
    public const ulong XSTATE_MASK_LEGACY_SSE = 0x00000002;
    public const ulong XSTATE_MASK_LEGACY = XSTATE_MASK_LEGACY_FLOATING_POINT | XSTATE_MASK_LEGACY_SSE;
    public const ulong XSTATE_MASK_GSSE = 0x00000004;  // AVX
    public const ulong XSTATE_MASK_AVX = XSTATE_MASK_GSSE;
    public const ulong XSTATE_MASK_AVX512 = 0x000000E0;  // AVX-512
}

/// <summary>
/// CONTEXT flags for extended state.
/// </summary>
public static class ContextXStateFlags
{
    public const uint CONTEXT_XSTATE = 0x00100040;  // CONTEXT_AMD64 | XSTATE
}

/// <summary>
/// PAL XState APIs - Extended processor state management.
/// These stubs indicate SSE-only support (no AVX).
/// </summary>
public static unsafe class XState
{
    /// <summary>
    /// Get the mask of enabled XSAVE features.
    /// Returns the features that are available and enabled on the current processor.
    /// </summary>
    /// <returns>Bitmask of enabled features (0 if XSAVE not supported)</returns>
    public static ulong GetEnabledXStateFeatures()
    {
        // For now, we only claim to support SSE (legacy floating point + SSE)
        // A full implementation would:
        // 1. Check CPUID for XSAVE support
        // 2. Read XCR0 to see what features are enabled
        // 3. Return the appropriate mask

        // Return SSE support only (most compatible)
        return XStateFeatures.XSTATE_MASK_LEGACY;
    }

    /// <summary>
    /// Initialize a CONTEXT structure for use with extended state.
    /// </summary>
    /// <param name="buffer">Buffer to initialize</param>
    /// <param name="contextFlags">Desired context flags</param>
    /// <param name="context">Receives pointer to initialized CONTEXT</param>
    /// <param name="contextLength">Size of buffer / receives required size</param>
    /// <returns>True on success</returns>
    public static bool InitializeContext(
        void* buffer,
        uint contextFlags,
        void** context,
        uint* contextLength)
    {
        // For SSE-only support, we use a standard CONTEXT structure
        // A full implementation would calculate size based on XSAVE area requirements

        const uint basicContextSize = 1232;  // Size of basic x64 CONTEXT with XMM

        if (contextLength == null)
            return false;

        // If buffer is null, return required size
        if (buffer == null)
        {
            *contextLength = basicContextSize;
            if (context != null)
                *context = null;
            return true;
        }

        // Check if buffer is large enough
        if (*contextLength < basicContextSize)
        {
            *contextLength = basicContextSize;
            return false;
        }

        // Initialize the context
        if (context != null)
        {
            // Zero the buffer
            byte* p = (byte*)buffer;
            for (uint i = 0; i < basicContextSize; i++)
                p[i] = 0;

            *context = buffer;

            // Set ContextFlags in the structure
            // CONTEXT structure has ContextFlags at offset 48 on x64
            *((uint*)((byte*)buffer + 48)) = contextFlags;
        }

        *contextLength = basicContextSize;
        return true;
    }

    /// <summary>
    /// Initialize a CONTEXT structure with compaction support (Windows 10+).
    /// </summary>
    public static bool InitializeContext2(
        void* buffer,
        uint contextFlags,
        void** context,
        uint* contextLength,
        ulong xstateCompactionMask)
    {
        // For now, ignore compaction mask and use regular InitializeContext
        return InitializeContext(buffer, contextFlags, context, contextLength);
    }

    /// <summary>
    /// Set the XSTATE features mask in a CONTEXT structure.
    /// Specifies which extended state features should be saved/restored.
    /// </summary>
    /// <param name="context">CONTEXT structure</param>
    /// <param name="featureMask">Bitmask of features to enable</param>
    /// <returns>True on success</returns>
    public static bool SetXStateFeaturesMask(void* context, ulong featureMask)
    {
        if (context == null)
            return false;

        // For SSE-only, we accept the request but effectively support only SSE
        // A full implementation would set up the CONTEXT_EX area

        return true;
    }

    /// <summary>
    /// Locate a specific XSTATE feature in a CONTEXT structure.
    /// Returns a pointer to the feature data within the context.
    /// </summary>
    /// <param name="context">CONTEXT structure</param>
    /// <param name="featureId">Feature ID to locate (XSTATE_* constant)</param>
    /// <param name="length">Receives size of the feature data</param>
    /// <returns>Pointer to feature data, or null if not present</returns>
    public static void* LocateXStateFeature(void* context, uint featureId, uint* length)
    {
        if (context == null)
            return null;

        if (length != null)
            *length = 0;

        // Only SSE (XMM registers) is supported
        // XMM registers are at a fixed offset in the CONTEXT structure
        if (featureId == 0 || featureId == 1)  // Legacy FP or SSE
        {
            // CONTEXT.FltSave is at offset 256 on x64
            // This contains x87 FPU state and XMM0-15
            if (length != null)
                *length = 512;  // Size of XSAVE legacy area

            return (byte*)context + 256;
        }

        // AVX and other features not supported
        return null;
    }

    /// <summary>
    /// Copy extended state from one context to another.
    /// </summary>
    public static bool CopyContext(
        void* destination,
        uint contextFlags,
        void* source)
    {
        if (destination == null || source == null)
            return false;

        // For basic CONTEXT, just copy the structure
        // A full implementation would handle XSTATE areas

        // Basic CONTEXT size
        const uint contextSize = 1232;

        byte* src = (byte*)source;
        byte* dst = (byte*)destination;
        for (uint i = 0; i < contextSize; i++)
            dst[i] = src[i];

        return true;
    }
}
