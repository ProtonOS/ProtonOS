// ProtonOS korlib - RuntimeArgumentHandle for varargs support

using System.Runtime.InteropServices;

namespace System
{
    /// <summary>
    /// Represents a variable argument list (varargs).
    /// This is the handle returned by the IL 'arglist' instruction.
    /// </summary>
    /// <remarks>
    /// The arglist instruction returns a pointer to where the varargs
    /// begin on the stack. For x64 calling convention:
    ///   handle = RBP + 16 + (declaredArgCount * 8)
    ///
    /// Each vararg is a TypedReference (16 bytes):
    ///   +0: nint Value (pointer to data or data itself for small types)
    ///   +8: nint Type  (MethodTable*)
    ///
    /// A sentinel TypedReference (Value=0, Type=0) marks the end.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    public ref struct RuntimeArgumentHandle
    {
        internal readonly nint _ptr;  // pointer to first TypedReference on stack

        /// <summary>
        /// Gets the raw pointer value (for debugging).
        /// </summary>
        public nint Value => _ptr;
    }
}
