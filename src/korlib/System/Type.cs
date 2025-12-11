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

using System.Runtime;
using System.Runtime.InteropServices;
using System.Reflection;

namespace System
{
    /// <summary>
    /// Represents type declarations: class types, interface types, array types, value types,
    /// enumeration types, type parameters, generic type definitions, and open or closed
    /// constructed generic types.
    /// </summary>
    public abstract class Type
    {
        /// <summary>
        /// Gets a Type from a RuntimeTypeHandle.
        /// Required by the compiler for typeof() operator.
        /// </summary>
        public static unsafe Type GetTypeFromHandle(RuntimeTypeHandle handle)
        {
            if (handle.Value == IntPtr.Zero)
                return null!;
            return new RuntimeType((MethodTable*)handle.Value);
        }

        /// <summary>
        /// Gets the fully qualified name of the type.
        /// </summary>
        public virtual string? FullName => null;

        /// <summary>
        /// Gets the name of the current type.
        /// </summary>
        public virtual string? Name => null;

        /// <summary>
        /// Gets the namespace of the Type.
        /// </summary>
        public virtual string? Namespace => null;

        /// <summary>
        /// Gets the RuntimeTypeHandle for this type.
        /// </summary>
        public virtual RuntimeTypeHandle TypeHandle => default;

        /// <summary>
        /// Gets all public methods of the current type.
        /// </summary>
        public virtual MethodInfo[] GetMethods() => GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);

        /// <summary>
        /// Gets the methods of the current type with the specified binding flags.
        /// </summary>
        public virtual MethodInfo[] GetMethods(BindingFlags bindingAttr) => new MethodInfo[0];

        /// <summary>
        /// Gets all public fields of the current type.
        /// </summary>
        public virtual FieldInfo[] GetFields() => GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);

        /// <summary>
        /// Gets the fields of the current type with the specified binding flags.
        /// </summary>
        public virtual FieldInfo[] GetFields(BindingFlags bindingAttr) => new FieldInfo[0];

        /// <summary>
        /// Gets all public properties of the current type.
        /// </summary>
        public virtual PropertyInfo[] GetProperties() => GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);

        /// <summary>
        /// Gets the properties of the current type with the specified binding flags.
        /// </summary>
        public virtual PropertyInfo[] GetProperties(BindingFlags bindingAttr) => new PropertyInfo[0];

        /// <summary>
        /// Gets all public constructors of the current type.
        /// </summary>
        public virtual ConstructorInfo[] GetConstructors() => GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        /// <summary>
        /// Gets the constructors of the current type with the specified binding flags.
        /// </summary>
        public virtual ConstructorInfo[] GetConstructors(BindingFlags bindingAttr) => new ConstructorInfo[0];

        /// <summary>
        /// Gets a specific method by name.
        /// </summary>
        public virtual MethodInfo? GetMethod(string name) => GetMethod(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);

        /// <summary>
        /// Gets a specific method by name and binding flags.
        /// </summary>
        public virtual MethodInfo? GetMethod(string name, BindingFlags bindingAttr)
        {
            var methods = GetMethods(bindingAttr);
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name == name)
                    return methods[i];
            }
            return null;
        }

        /// <summary>
        /// Gets a specific field by name.
        /// </summary>
        public virtual FieldInfo? GetField(string name) => GetField(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);

        /// <summary>
        /// Gets a specific field by name and binding flags.
        /// </summary>
        public virtual FieldInfo? GetField(string name, BindingFlags bindingAttr)
        {
            var fields = GetFields(bindingAttr);
            for (int i = 0; i < fields.Length; i++)
            {
                if (fields[i].Name == name)
                    return fields[i];
            }
            return null;
        }

        /// <summary>
        /// Gets a specific property by name.
        /// </summary>
        public virtual PropertyInfo? GetProperty(string name) => GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);

        /// <summary>
        /// Gets a specific property by name and binding flags.
        /// </summary>
        public virtual PropertyInfo? GetProperty(string name, BindingFlags bindingAttr)
        {
            var properties = GetProperties(bindingAttr);
            for (int i = 0; i < properties.Length; i++)
            {
                if (properties[i].Name == name)
                    return properties[i];
            }
            return null;
        }
    }

    /// <summary>
    /// Represents a type declaration at runtime.
    /// </summary>
    public unsafe class RuntimeType : Type
    {
        private readonly MethodTable* _pMethodTable;

        // Cached reflection metadata (lazy loaded from kernel)
        private uint _assemblyId;
        private uint _typeDefToken;
        private bool _metadataLoaded;
        private string? _name;
        private string? _namespace;

        internal RuntimeType(MethodTable* pMethodTable)
        {
            _pMethodTable = pMethodTable;
        }

        /// <summary>
        /// Constructor with explicit assembly/token info (for reflection-created Types).
        /// </summary>
        internal RuntimeType(MethodTable* pMethodTable, uint assemblyId, uint typeDefToken)
        {
            _pMethodTable = pMethodTable;
            _assemblyId = assemblyId;
            _typeDefToken = typeDefToken;
            _metadataLoaded = true;
        }

        /// <summary>
        /// Ensure we have loaded the reflection metadata from the kernel.
        /// </summary>
        private void EnsureMetadataLoaded()
        {
            if (_metadataLoaded)
                return;

            // Query kernel for type info based on MethodTable
            uint asmId = 0, token = 0;
            Reflection_GetTypeInfo(_pMethodTable, &asmId, &token);
            _assemblyId = asmId;
            _typeDefToken = token;
            _metadataLoaded = true;
        }

        public override RuntimeTypeHandle TypeHandle => new RuntimeTypeHandle((nint)_pMethodTable);

        public override string? Name
        {
            get
            {
                if (_name == null)
                {
                    EnsureMetadataLoaded();
                    if (_typeDefToken != 0)
                    {
                        byte* namePtr = Reflection_GetTypeName(_assemblyId, _typeDefToken);
                        if (namePtr != null)
                            _name = BytePtrToString(namePtr);
                    }
                    if (_name == null)
                        _name = "RuntimeType";
                }
                return _name;
            }
        }

        public override string? Namespace
        {
            get
            {
                if (_namespace == null)
                {
                    EnsureMetadataLoaded();
                    if (_typeDefToken != 0)
                    {
                        byte* nsPtr = Reflection_GetTypeNamespace(_assemblyId, _typeDefToken);
                        if (nsPtr != null && *nsPtr != 0)
                            _namespace = BytePtrToString(nsPtr);
                    }
                }
                return _namespace;
            }
        }

        public override string? FullName
        {
            get
            {
                string? ns = Namespace;
                string? name = Name;
                if (string.IsNullOrEmpty(ns))
                    return name;
                return ns + "." + name;
            }
        }

        public override MethodInfo[] GetMethods(BindingFlags bindingAttr)
        {
            EnsureMetadataLoaded();
            if (_typeDefToken == 0)
                return new MethodInfo[0];

            uint count = Reflection_GetMethodCount(_assemblyId, _typeDefToken);
            if (count == 0)
                return new MethodInfo[0];

            var methods = new MethodInfo[count];
            for (uint i = 0; i < count; i++)
            {
                uint methodToken = Reflection_GetMethodToken(_assemblyId, _typeDefToken, i);
                if (methodToken != 0)
                {
                    methods[i] = new RuntimeMethodInfo(_assemblyId, methodToken, this);
                }
            }

            // Filter by binding flags (simplified - just check static vs instance)
            int actualCount = 0;
            for (uint i = 0; i < count; i++)
            {
                if (methods[i] != null)
                {
                    var rmi = (RuntimeMethodInfo)methods[i];
                    bool isStatic = rmi.IsStatic;
                    bool includeStatic = (bindingAttr & BindingFlags.Static) != 0;
                    bool includeInstance = (bindingAttr & BindingFlags.Instance) != 0;

                    if ((isStatic && includeStatic) || (!isStatic && includeInstance))
                        actualCount++;
                }
            }

            if (actualCount == (int)count)
                return methods;

            // Filter to matching methods
            var filtered = new MethodInfo[actualCount];
            int idx = 0;
            for (uint i = 0; i < count; i++)
            {
                if (methods[i] != null)
                {
                    var rmi = (RuntimeMethodInfo)methods[i];
                    bool isStatic = rmi.IsStatic;
                    bool includeStatic = (bindingAttr & BindingFlags.Static) != 0;
                    bool includeInstance = (bindingAttr & BindingFlags.Instance) != 0;

                    if ((isStatic && includeStatic) || (!isStatic && includeInstance))
                        filtered[idx++] = methods[i];
                }
            }

            return filtered;
        }

        public override FieldInfo[] GetFields(BindingFlags bindingAttr)
        {
            EnsureMetadataLoaded();
            if (_typeDefToken == 0)
                return new FieldInfo[0];

            uint count = Reflection_GetFieldCount(_assemblyId, _typeDefToken);
            if (count == 0)
                return new FieldInfo[0];

            var fields = new FieldInfo[count];
            for (uint i = 0; i < count; i++)
            {
                uint fieldToken = Reflection_GetFieldToken(_assemblyId, _typeDefToken, i);
                if (fieldToken != 0)
                {
                    // For now, create with default offset/size - would need metadata to calculate
                    fields[i] = new RuntimeFieldInfo(_assemblyId, fieldToken, this, 8, 8, false);
                }
            }

            return fields;
        }

        public override ConstructorInfo[] GetConstructors(BindingFlags bindingAttr)
        {
            EnsureMetadataLoaded();
            if (_typeDefToken == 0)
                return new ConstructorInfo[0];

            // Look for .ctor methods
            uint methodCount = Reflection_GetMethodCount(_assemblyId, _typeDefToken);
            if (methodCount == 0)
                return new ConstructorInfo[0];

            // First pass: count constructors
            int ctorCount = 0;
            for (uint i = 0; i < methodCount; i++)
            {
                uint methodToken = Reflection_GetMethodToken(_assemblyId, _typeDefToken, i);
                if (methodToken != 0)
                {
                    byte* namePtr = Reflection_GetMethodName(_assemblyId, methodToken);
                    if (namePtr != null && IsCtorName(namePtr))
                        ctorCount++;
                }
            }

            if (ctorCount == 0)
                return new ConstructorInfo[0];

            // Second pass: collect constructors
            var ctors = new ConstructorInfo[ctorCount];
            int idx = 0;
            for (uint i = 0; i < methodCount; i++)
            {
                uint methodToken = Reflection_GetMethodToken(_assemblyId, _typeDefToken, i);
                if (methodToken != 0)
                {
                    byte* namePtr = Reflection_GetMethodName(_assemblyId, methodToken);
                    if (namePtr != null && IsCtorName(namePtr))
                    {
                        ctors[idx++] = new RuntimeConstructorInfo(_assemblyId, methodToken, this);
                    }
                }
            }

            return ctors;
        }

        private static bool IsCtorName(byte* name)
        {
            // Check for ".ctor"
            return name[0] == '.' && name[1] == 'c' && name[2] == 't' && name[3] == 'o' && name[4] == 'r' && name[5] == 0;
        }

        private static string BytePtrToString(byte* ptr)
        {
            if (ptr == null)
                return string.Empty;

            int len = 0;
            while (ptr[len] != 0)
                len++;

            if (len == 0)
                return string.Empty;

            char* chars = stackalloc char[len];
            for (int i = 0; i < len; i++)
                chars[i] = (char)ptr[i];

            return new string(chars, 0, len);
        }

        // Import kernel reflection APIs
        [DllImport("*", EntryPoint = "Reflection_GetTypeInfo", CallingConvention = CallingConvention.Cdecl)]
        private static extern void Reflection_GetTypeInfo(void* methodTable, uint* outAssemblyId, uint* outTypeDefToken);

        [DllImport("*", EntryPoint = "Reflection_GetTypeName", CallingConvention = CallingConvention.Cdecl)]
        private static extern byte* Reflection_GetTypeName(uint assemblyId, uint typeDefToken);

        [DllImport("*", EntryPoint = "Reflection_GetTypeNamespace", CallingConvention = CallingConvention.Cdecl)]
        private static extern byte* Reflection_GetTypeNamespace(uint assemblyId, uint typeDefToken);

        [DllImport("*", EntryPoint = "Reflection_GetMethodCount", CallingConvention = CallingConvention.Cdecl)]
        private static extern uint Reflection_GetMethodCount(uint assemblyId, uint typeDefToken);

        [DllImport("*", EntryPoint = "Reflection_GetMethodToken", CallingConvention = CallingConvention.Cdecl)]
        private static extern uint Reflection_GetMethodToken(uint assemblyId, uint typeDefToken, uint index);

        [DllImport("*", EntryPoint = "Reflection_GetFieldCount", CallingConvention = CallingConvention.Cdecl)]
        private static extern uint Reflection_GetFieldCount(uint assemblyId, uint typeDefToken);

        [DllImport("*", EntryPoint = "Reflection_GetFieldToken", CallingConvention = CallingConvention.Cdecl)]
        private static extern uint Reflection_GetFieldToken(uint assemblyId, uint typeDefToken, uint index);

        [DllImport("*", EntryPoint = "Reflection_GetMethodName", CallingConvention = CallingConvention.Cdecl)]
        private static extern byte* Reflection_GetMethodName(uint assemblyId, uint methodToken);
    }
}
