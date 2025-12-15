// ProtonOS korlib - ArgIterator for varargs support

using System.Runtime.InteropServices;

namespace System
{
    /// <summary>
    /// Represents an iterator for variable argument lists (varargs).
    /// Used to iterate through arguments passed via __arglist.
    /// </summary>
    /// <remarks>
    /// The varargs are laid out as an array of TypedReference (16 bytes each):
    ///   [TypedReference 0][TypedReference 1]...[Sentinel]
    ///
    /// Sentinel is a TypedReference with Value=0 and Type=0.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe ref struct ArgIterator
    {
        private byte* _current;  // current position in varargs array

        /// <summary>
        /// Initializes a new ArgIterator from a RuntimeArgumentHandle.
        /// </summary>
        /// <param name="arglist">Handle to the argument list from __arglist.</param>
        public ArgIterator(RuntimeArgumentHandle arglist)
        {
            _current = (byte*)arglist._ptr;
        }

        /// <summary>
        /// Initializes a new ArgIterator from a RuntimeArgumentHandle with a specific starting pointer.
        /// </summary>
        /// <param name="arglist">Handle to the argument list.</param>
        /// <param name="ptr">Specific starting pointer (ignored, uses arglist).</param>
        public ArgIterator(RuntimeArgumentHandle arglist, void* ptr)
        {
            // In our implementation, we use the arglist pointer directly
            // The ptr parameter exists for .NET compatibility but we ignore it
            _current = (byte*)arglist._ptr;
        }

        /// <summary>
        /// Gets the next argument as a TypedReference.
        /// </summary>
        /// <returns>A TypedReference to the next argument.</returns>
        public TypedReference GetNextArg()
        {
            // Read the TypedReference at current position
            TypedReference* tr = (TypedReference*)_current;
            TypedReference result = *tr;

            // Advance to next TypedReference (16 bytes)
            _current += 16;

            return result;
        }

        /// <summary>
        /// Gets the next argument as a TypedReference, with expected type verification.
        /// </summary>
        /// <param name="rth">The expected RuntimeTypeHandle of the argument.</param>
        /// <returns>A TypedReference to the next argument.</returns>
        /// <exception cref="InvalidCastException">Thrown if the argument type doesn't match the expected type.</exception>
        public TypedReference GetNextArg(RuntimeTypeHandle rth)
        {
            // Read the TypedReference at current position
            TypedReference* tr = (TypedReference*)_current;
            TypedReference result = *tr;

            // Verify type matches expected
            nint actualType = TypedReference.TargetTypeToken(result).Value;
            nint expectedType = rth.Value;
            if (actualType != expectedType && actualType != 0 && expectedType != 0)
            {
                throw new InvalidCastException();
            }

            // Advance to next TypedReference (16 bytes)
            _current += 16;

            return result;
        }

        /// <summary>
        /// Gets the RuntimeTypeHandle of the next argument without consuming it.
        /// Actually, this does consume the argument in our implementation.
        /// </summary>
        /// <returns>The RuntimeTypeHandle of the next argument.</returns>
        public RuntimeTypeHandle GetNextArgType()
        {
            // Read just the Type field (at offset 8)
            nint type = *(nint*)(_current + 8);

            // Advance to next TypedReference (16 bytes)
            _current += 16;

            return new RuntimeTypeHandle(type);
        }

        /// <summary>
        /// Gets the number of remaining arguments.
        /// Counts TypedReferences until a sentinel (Type == 0) is found.
        /// </summary>
        /// <returns>The number of remaining arguments, or -1 if unknown.</returns>
        public int GetRemainingCount()
        {
            int count = 0;
            byte* p = _current;

            // Count until we hit the sentinel (Type field == 0)
            while (true)
            {
                nint type = *(nint*)(p + 8);  // Read Type field
                if (type == 0)
                    break;
                count++;
                p += 16;  // Next TypedReference
            }

            return count;
        }

        /// <summary>
        /// Ends iteration. No-op in this implementation.
        /// </summary>
        public void End()
        {
            // Nothing to clean up
        }

        public override bool Equals(object? o)
        {
            throw new NotSupportedException();
        }

        public override int GetHashCode()
        {
            return ((nint)_current).GetHashCode();
        }
    }
}
