// bflat minimal runtime library
// Copyright (C) 2021-2022 Michal Strehovsky
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as published
// by the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.
//
// You should have received a copy of the GNU Affero General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

namespace System
{
    public abstract class Delegate
    {
        // Field names must match what NativeAOT expects (underscore prefix, not m_ prefix)
        internal object _firstParameter;
        internal object _helperObject;
        internal nint _extraFunctionPointerOrData;
        internal IntPtr _functionPointer;

        private void InitializeClosedStaticThunk(object firstParameter, IntPtr functionPointer, IntPtr functionPointerThunk)
        {
            _extraFunctionPointerOrData = functionPointer;
            _helperObject = firstParameter;
            _functionPointer = functionPointerThunk;
            _firstParameter = this;
        }

        private void InitializeOpenStaticThunk(object firstParameter, IntPtr functionPointer, IntPtr functionPointerThunk)
        {
            _firstParameter = this;
            _functionPointer = functionPointerThunk;
            _extraFunctionPointerOrData = functionPointer;
        }

        private void InitializeClosedInstance(object firstParameter, IntPtr functionPointer)
        {
            _functionPointer = functionPointer;
            _firstParameter = firstParameter;
        }

        /// <summary>
        /// Combines two delegates into a multicast delegate.
        /// </summary>
        public static Delegate? Combine(Delegate? a, Delegate? b)
        {
            if (a == null) return b;
            if (b == null) return a;
            // All delegates are MulticastDelegate in practice
            // Use Unsafe.As to avoid virtual dispatch in cast (NativeAOT uses different vtable layout)
            var mcd = System.Runtime.CompilerServices.Unsafe.As<Delegate, MulticastDelegate>(ref a);
            return mcd.InvokeCombineImpl(b);
        }

        /// <summary>
        /// Removes the last occurrence of a delegate from a multicast delegate.
        /// </summary>
        public static Delegate? Remove(Delegate? source, Delegate? value)
        {
            if (source == null) return null;
            if (value == null) return source;
            // All delegates are MulticastDelegate in practice
            // Use Unsafe.As to avoid virtual dispatch in cast (NativeAOT uses different vtable layout)
            return System.Runtime.CompilerServices.Unsafe.As<Delegate, MulticastDelegate>(ref source).InvokeRemoveImpl(value);
        }

        /// <summary>
        /// Virtual method for derived classes to implement combining.
        /// </summary>
        protected virtual Delegate? CombineImpl(Delegate? d)
        {
            // Base Delegate doesn't support combining - just return the second delegate
            return d;
        }

        /// <summary>
        /// Virtual method for derived classes to implement removal.
        /// </summary>
        protected virtual Delegate? RemoveImpl(Delegate d)
        {
            // Simple: if function pointers match, return null (removed)
            if (_functionPointer == d._functionPointer) return null;
            return this;
        }

        /// <summary>
        /// Checks if two delegates are equal (same target and method).
        /// Uses reference equality on _firstParameter and value equality on _functionPointer.
        /// </summary>
        public override bool Equals(object? obj)
        {
            if (obj == null) return false;
            // Use reference equality check instead of 'as' cast to avoid pulling in complex code
            if (obj.GetType() != GetType()) return false;
            Delegate other = (Delegate)obj;
            return ReferenceEquals(_firstParameter, other._firstParameter) &&
                   _functionPointer == other._functionPointer;
        }

        public override int GetHashCode()
        {
            // Return a simple hash based on the function pointer
            return (int)_functionPointer;
        }

        private static bool ReferenceEquals(object? a, object? b)
        {
            return (object?)a == (object?)b;
        }
    }

    public abstract class MulticastDelegate : Delegate
    {
        // Invocation list for multicast delegates
        // null = single delegate (just call _functionPointer)
        // Delegate[] = multiple delegates to invoke in order
        internal Delegate[]? _invocationList;
        internal int _invocationCount;

        /// <summary>
        /// Internal non-virtual implementation for combining delegates.
        /// Called directly from Delegate.Combine to avoid virtual dispatch.
        /// Creates a NEW delegate - does not mutate the original (important for delegate caching).
        /// </summary>
        internal Delegate? InvokeCombineImpl(Delegate? follow)
        {
            if (follow == null) return this;

            // Use direct pointer access to handle ABI differences between AOT and JIT delegate layouts
            // JIT delegate layout: MT(0), _firstParameter(8), _helperObject(16), _extraFunctionPointerOrData(24),
            //                      _functionPointer(32), _invocationList(40), _invocationCount(48)
            unsafe
            {
                // Get pointers to both delegate objects using the managed ref conversion
                MulticastDelegate self = this;
                nint thisPtr = *(nint*)System.Runtime.CompilerServices.Unsafe.AsPointer(ref self);
                nint followPtr = *(nint*)System.Runtime.CompilerServices.Unsafe.AsPointer(ref follow);

                // Read invocation list and count using explicit offsets
                // These offsets match the JIT-created delegate layout
                const int InvocationListOffset = 40;
                const int InvocationCountOffset = 48;
                const int FunctionPointerOffset = 32;
                const int FirstParameterOffset = 8;
                const int HelperObjectOffset = 16;
                const int ExtraFuncPtrOffset = 24;

                nint thisInvListPtr = *(nint*)(thisPtr + InvocationListOffset);
                int thisInvCount = *(int*)(thisPtr + InvocationCountOffset);
                nint followInvListPtr = *(nint*)(followPtr + InvocationListOffset);
                int followInvCount = *(int*)(followPtr + InvocationCountOffset);

                // For single delegates without invocation list, count is 1
                int thisCount = thisInvListPtr != 0 ? thisInvCount : 1;
                int followCount = followInvListPtr != 0 ? followInvCount : 1;
                int totalCount = thisCount + followCount;

                // Create new invocation list using object[] to avoid Delegate type issues
                object[] newListObj = new object[totalCount];
                nint newListPtr = *(nint*)System.Runtime.CompilerServices.Unsafe.AsPointer(ref newListObj);

                // Copy from this
                if (thisInvListPtr != 0)
                {
                    // Copy array elements by pointer
                    for (int i = 0; i < thisInvCount; i++)
                    {
                        // Array elements start at offset 16 (after MT and length)
                        *(nint*)(newListPtr + 16 + (nint)i * 8) = *(nint*)(thisInvListPtr + 16 + (nint)i * 8);
                    }
                }
                else
                {
                    // Single delegate - store this
                    *(nint*)(newListPtr + 16) = thisPtr;
                }

                // Copy from follow
                if (followInvListPtr != 0)
                {
                    for (int i = 0; i < followInvCount; i++)
                    {
                        *(nint*)(newListPtr + 16 + (nint)(thisCount + i) * 8) = *(nint*)(followInvListPtr + 16 + (nint)i * 8);
                    }
                }
                else
                {
                    // Single delegate - store follow
                    *(nint*)(newListPtr + 16 + (nint)thisCount * 8) = followPtr;
                }

                // Allocate a NEW delegate object of the same type (don't mutate original!)
                // Get MethodTable from this delegate and allocate new object
                nint mtPtr = *(nint*)thisPtr;

                // Allocate new object: we need to call GC.Alloc with the MethodTable
                // Delegate objects are 56 bytes: MT(8) + 4 fields(32) + invocationList(8) + invocationCount(8)
                const int DelegateSize = 56;
                nint newDelegatePtr = AllocateObject(mtPtr, DelegateSize);

                if (newDelegatePtr == 0) return this; // Allocation failed, fall back to original

                // Get the last delegate for function pointer and first parameter
                nint lastDelegatePtr = *(nint*)(newListPtr + 16 + (nint)(totalCount - 1) * 8);

                // Set up the new delegate's fields
                *(nint*)(newDelegatePtr + FirstParameterOffset) = *(nint*)(lastDelegatePtr + FirstParameterOffset);
                *(nint*)(newDelegatePtr + HelperObjectOffset) = *(nint*)(lastDelegatePtr + HelperObjectOffset);
                *(nint*)(newDelegatePtr + ExtraFuncPtrOffset) = *(nint*)(lastDelegatePtr + ExtraFuncPtrOffset);
                *(IntPtr*)(newDelegatePtr + FunctionPointerOffset) = *(IntPtr*)(lastDelegatePtr + FunctionPointerOffset);
                *(nint*)(newDelegatePtr + InvocationListOffset) = newListPtr;
                *(int*)(newDelegatePtr + InvocationCountOffset) = totalCount;

                // Return the new delegate as a managed reference
                return System.Runtime.CompilerServices.Unsafe.As<nint, Delegate>(ref newDelegatePtr);
            }
        }

        /// <summary>
        /// Allocate a new object on the GC heap.
        /// </summary>
        private static unsafe nint AllocateObject(nint methodTable, int size)
        {
            // Use PalAllocObject from kernel PAL (same as RuntimeExports uses)
            nint result = (nint)PalAllocObject((uint)size);
            if (result == 0) return 0;
            // Set the MethodTable pointer
            *(nint*)result = methodTable;
            return result;
        }

        [System.Runtime.InteropServices.DllImport("*", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern unsafe void* PalAllocObject(uint size);

        /// <summary>
        /// Internal non-virtual implementation for removing delegates.
        /// Called directly from Delegate.Remove to avoid virtual dispatch.
        /// Uses pointer-based field access to handle ABI differences between AOT and JIT.
        /// </summary>
        internal Delegate? InvokeRemoveImpl(Delegate value)
        {
            unsafe
            {
                // Get pointers to delegate objects
                MulticastDelegate self = this;
                nint thisPtr = *(nint*)System.Runtime.CompilerServices.Unsafe.AsPointer(ref self);
                nint valuePtr = *(nint*)System.Runtime.CompilerServices.Unsafe.AsPointer(ref value);

                // Field offsets for JIT delegate layout
                const int InvocationListOffset = 40;
                const int InvocationCountOffset = 48;
                const int FunctionPointerOffset = 32;
                const int FirstParameterOffset = 8;

                nint thisInvListPtr = *(nint*)(thisPtr + InvocationListOffset);
                int thisInvCount = *(int*)(thisPtr + InvocationCountOffset);
                nint valueFuncPtr = *(nint*)(valuePtr + FunctionPointerOffset);

                if (thisInvListPtr == 0)
                {
                    // Single delegate - remove if function pointers match
                    nint thisFuncPtr = *(nint*)(thisPtr + FunctionPointerOffset);
                    if (thisFuncPtr == valueFuncPtr) return null;
                    return this;
                }

                // Find the delegate to remove (search from end)
                int removeIndex = -1;
                for (int i = thisInvCount - 1; i >= 0; i--)
                {
                    // Array elements start at offset 16
                    nint delegatePtr = *(nint*)(thisInvListPtr + 16 + (nint)i * 8);
                    nint delegateFuncPtr = *(nint*)(delegatePtr + FunctionPointerOffset);
                    if (delegateFuncPtr == valueFuncPtr)
                    {
                        removeIndex = i;
                        break;
                    }
                }

                if (removeIndex < 0)
                {
                    // Not found, return unchanged
                    return this;
                }

                int newCount = thisInvCount - 1;
                if (newCount == 0)
                {
                    // No delegates left
                    return null;
                }
                if (newCount == 1)
                {
                    // Only one delegate left - return it directly
                    int remainingIndex = removeIndex == 0 ? 1 : 0;
                    nint remainingPtr = *(nint*)(thisInvListPtr + 16 + (nint)remainingIndex * 8);
                    // Convert pointer back to managed reference
                    return System.Runtime.CompilerServices.Unsafe.As<nint, Delegate>(ref remainingPtr);
                }

                // Create new list without the removed delegate
                object[] newListObj = new object[newCount];
                nint newListPtr = *(nint*)System.Runtime.CompilerServices.Unsafe.AsPointer(ref newListObj);

                int j = 0;
                for (int i = 0; i < thisInvCount; i++)
                {
                    if (i != removeIndex)
                    {
                        nint delegatePtr = *(nint*)(thisInvListPtr + 16 + (nint)i * 8);
                        *(nint*)(newListPtr + 16 + (nint)j * 8) = delegatePtr;
                        j++;
                    }
                }

                // Update this delegate using pointer offsets
                *(nint*)(thisPtr + InvocationListOffset) = newListPtr;
                *(int*)(thisPtr + InvocationCountOffset) = newCount;

                // Get the last delegate's function pointer and first parameter
                nint lastDelegatePtr = *(nint*)(newListPtr + 16 + (nint)(newCount - 1) * 8);
                *(IntPtr*)(thisPtr + FunctionPointerOffset) = *(IntPtr*)(lastDelegatePtr + FunctionPointerOffset);
                *(nint*)(thisPtr + FirstParameterOffset) = *(nint*)(lastDelegatePtr + FirstParameterOffset);

                return this;
            }
        }

        /// <summary>
        /// Combines this delegate with another.
        /// For simplicity, we just store the invocation list and let the JIT handle iteration.
        /// </summary>
        protected override Delegate? CombineImpl(Delegate? follow)
        {
            if (follow == null) return this;

            // Use Unsafe.As to avoid virtual dispatch in cast (NativeAOT uses different vtable layout)
            MulticastDelegate dFollow = System.Runtime.CompilerServices.Unsafe.As<Delegate, MulticastDelegate>(ref follow);

            // Count total delegates needed
            int thisCount = _invocationList != null ? _invocationCount : 1;
            int followCount = dFollow._invocationList != null ? dFollow._invocationCount : 1;
            int totalCount = thisCount + followCount;

            // Create new invocation list
            Delegate[] newList = new Delegate[totalCount];

            // Copy from this
            if (_invocationList != null)
            {
                for (int i = 0; i < _invocationCount; i++)
                    newList[i] = _invocationList[i];
            }
            else
            {
                newList[0] = this;
            }

            // Copy from follow
            if (dFollow._invocationList != null)
            {
                for (int i = 0; i < dFollow._invocationCount; i++)
                    newList[thisCount + i] = dFollow._invocationList[i];
            }
            else
            {
                newList[thisCount] = dFollow;
            }

            // Mutate this delegate (simpler than cloning)
            _invocationList = newList;
            _invocationCount = totalCount;
            _functionPointer = newList[totalCount - 1]._functionPointer;
            _firstParameter = newList[totalCount - 1]._firstParameter;  // _firstParameter is on Delegate base
            return this;
        }

        /// <summary>
        /// Removes a delegate from this multicast delegate.
        /// </summary>
        protected override Delegate? RemoveImpl(Delegate value)
        {
            if (_invocationList == null)
            {
                // Single delegate - remove if function pointers match
                if (_functionPointer == value._functionPointer) return null;
                return this;
            }

            // Find the delegate to remove (search from end)
            int removeIndex = -1;
            for (int i = _invocationCount - 1; i >= 0; i--)
            {
                if (_invocationList[i]._functionPointer == value._functionPointer)
                {
                    removeIndex = i;
                    break;
                }
            }

            if (removeIndex < 0)
            {
                // Not found, return unchanged
                return this;
            }

            int newCount = _invocationCount - 1;
            if (newCount == 0)
            {
                // No delegates left
                return null;
            }
            if (newCount == 1)
            {
                // Only one delegate left - return it directly
                return removeIndex == 0 ? _invocationList[1] : _invocationList[0];
            }

            // Create new list without the removed delegate
            Delegate[] newList = new Delegate[newCount];
            int j = 0;
            for (int i = 0; i < _invocationCount; i++)
            {
                if (i != removeIndex)
                    newList[j++] = _invocationList[i];
            }

            // Mutate this delegate
            _invocationList = newList;
            _invocationCount = newCount;
            _functionPointer = newList[newCount - 1]._functionPointer;
            _firstParameter = newList[newCount - 1]._firstParameter;  // _firstParameter is on Delegate base
            return this;
        }

        public override bool Equals(object? obj)
        {
            if (obj == null) return false;
            if (obj.GetType() != GetType()) return false;
            // Use Unsafe.As to avoid virtual dispatch in cast (NativeAOT uses different vtable layout)
            MulticastDelegate other = System.Runtime.CompilerServices.Unsafe.As<object, MulticastDelegate>(ref obj!);

            // If both have no invocation list, compare directly
            if (_invocationList == null && other._invocationList == null)
            {
                return base.Equals(obj);
            }

            // Compare invocation list counts
            int thisCount = _invocationList != null ? _invocationCount : 1;
            int otherCount = other._invocationList != null ? other._invocationCount : 1;
            if (thisCount != otherCount) return false;

            // Compare each delegate by function pointer
            for (int i = 0; i < thisCount; i++)
            {
                Delegate thisD = _invocationList != null ? _invocationList[i] : this;
                Delegate otherD = other._invocationList != null ? other._invocationList[i] : other;
                if (thisD._functionPointer != otherD._functionPointer)
                    return false;
            }
            return true;
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }
    }
}
