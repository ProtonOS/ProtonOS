// ProtonOS System.Runtime - Assembly
// Represents an assembly, which is a reusable, versionable, and self-describing building block of a common language runtime application.

using System.Collections.Generic;
using System.IO;

namespace System.Reflection
{
    /// <summary>
    /// Represents an assembly, which is a reusable, versionable, and self-describing building block of a common language runtime application.
    /// </summary>
    public abstract class Assembly : ICustomAttributeProvider
    {
        /// <summary>Gets the display name of the assembly.</summary>
        public virtual string FullName => string.Empty;

        /// <summary>Gets the location of the assembly as specified originally.</summary>
        public virtual string Location => string.Empty;

        /// <summary>Gets the code base of the assembly as a URL.</summary>
        [Obsolete("Assembly.CodeBase and Assembly.EscapedCodeBase are obsolete.")]
        public virtual string? CodeBase => null;

        /// <summary>Gets a value indicating whether the assembly was loaded from the global assembly cache.</summary>
        [Obsolete("The Global Assembly Cache is not supported.")]
        public virtual bool GlobalAssemblyCache => false;

        /// <summary>Gets the entry point of this assembly.</summary>
        public virtual MethodInfo? EntryPoint => null;

        /// <summary>Gets a Boolean value indicating whether the assembly is loaded for reflection only.</summary>
        [Obsolete("ReflectionOnly loading is not supported.")]
        public virtual bool ReflectionOnly => false;

        /// <summary>Gets a value that indicates which set of security rules the common language runtime enforces for this assembly.</summary>
        public virtual bool IsFullyTrusted => true;

        /// <summary>Gets a value that indicates whether the current assembly was generated dynamically in the current process.</summary>
        public virtual bool IsDynamic => false;

        /// <summary>Gets the host context with which the assembly was loaded.</summary>
        public virtual long HostContext => 0;

        /// <summary>Gets the module that contains the manifest for the current assembly.</summary>
        public virtual Module ManifestModule => throw new NotImplementedException();

        /// <summary>Gets an AssemblyName for this assembly.</summary>
        public virtual AssemblyName GetName()
        {
            return GetName(false);
        }

        /// <summary>Gets an AssemblyName for this assembly, optionally copying the public key token.</summary>
        public virtual AssemblyName GetName(bool copiedName)
        {
            return new AssemblyName(FullName);
        }

        /// <summary>Gets the Type object with the specified name in the assembly instance.</summary>
        public virtual Type? GetType(string name)
        {
            return GetType(name, false, false);
        }

        /// <summary>Gets the Type object with the specified name in the assembly instance, optionally throwing an exception if the type is not found.</summary>
        public virtual Type? GetType(string name, bool throwOnError)
        {
            return GetType(name, throwOnError, false);
        }

        /// <summary>Gets the Type object with the specified name in the assembly instance and optionally throws an exception if the type is not found.</summary>
        public virtual Type? GetType(string name, bool throwOnError, bool ignoreCase)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            return null;
        }

        /// <summary>Gets the types defined in this assembly.</summary>
        public virtual Type[] GetTypes()
        {
            return GetExportedTypes();
        }

        /// <summary>Gets the public types defined in this assembly that are visible outside the assembly.</summary>
        public virtual Type[] GetExportedTypes()
        {
            return Array.Empty<Type>();
        }

        /// <summary>Gets the specified module in this assembly.</summary>
        public virtual Module? GetModule(string name)
        {
            return null;
        }

        /// <summary>Gets all the modules that are part of this assembly.</summary>
        public virtual Module[] GetModules()
        {
            return GetModules(false);
        }

        /// <summary>Gets all the modules that are part of this assembly, specifying whether to include resource modules.</summary>
        public virtual Module[] GetModules(bool getResourceModules)
        {
            return Array.Empty<Module>();
        }

        /// <summary>Gets all the loaded modules that are part of this assembly.</summary>
        public virtual Module[] GetLoadedModules()
        {
            return GetLoadedModules(false);
        }

        /// <summary>Gets all the loaded modules that are part of this assembly, specifying whether to include resource modules.</summary>
        public virtual Module[] GetLoadedModules(bool getResourceModules)
        {
            return GetModules(getResourceModules);
        }

        /// <summary>Gets the AssemblyName objects for all the assemblies referenced by this assembly.</summary>
        public virtual AssemblyName[] GetReferencedAssemblies()
        {
            return Array.Empty<AssemblyName>();
        }

        /// <summary>Gets a collection of custom attributes.</summary>
        public virtual object[] GetCustomAttributes(bool inherit)
        {
            return Array.Empty<object>();
        }

        /// <summary>Gets a collection of custom attributes of the specified type.</summary>
        public virtual object[] GetCustomAttributes(Type attributeType, bool inherit)
        {
            if (attributeType == null) throw new ArgumentNullException(nameof(attributeType));
            return Array.Empty<object>();
        }

        /// <summary>Determines whether custom attributes of the specified type are applied to this assembly.</summary>
        public virtual bool IsDefined(Type attributeType, bool inherit)
        {
            if (attributeType == null) throw new ArgumentNullException(nameof(attributeType));
            return false;
        }

        /// <summary>Gets the satellite assembly for the specified culture.</summary>
        public virtual Assembly? GetSatelliteAssembly(System.Globalization.CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        /// <summary>Gets the satellite assembly for the specified culture and version.</summary>
        public virtual Assembly? GetSatelliteAssembly(System.Globalization.CultureInfo culture, Version? version)
        {
            throw new NotSupportedException();
        }

        /// <summary>Locates the specified type from this assembly and creates an instance of it using the system activator.</summary>
        public object? CreateInstance(string typeName)
        {
            return CreateInstance(typeName, false);
        }

        /// <summary>Locates the specified type from this assembly and creates an instance of it using the system activator, with optional case-insensitive search.</summary>
        public object? CreateInstance(string typeName, bool ignoreCase)
        {
            var type = GetType(typeName, false, ignoreCase);
            if (type == null) return null;
            return Activator.CreateInstance(type);
        }

        /// <summary>Returns the names of all the resources in this assembly.</summary>
        public virtual string[] GetManifestResourceNames()
        {
            return Array.Empty<string>();
        }

        /// <summary>Loads the specified manifest resource from this assembly.</summary>
        public virtual Stream? GetManifestResourceStream(string name)
        {
            return null;
        }

        /// <summary>Loads the specified manifest resource, scoped by the namespace of the specified type, from this assembly.</summary>
        public virtual Stream? GetManifestResourceStream(Type type, string name)
        {
            return GetManifestResourceStream(type.Namespace + "." + name);
        }

        /// <summary>Returns information about how the given resource has been persisted.</summary>
        public virtual ManifestResourceInfo? GetManifestResourceInfo(string resourceName)
        {
            return null;
        }

        /// <summary>Gets the currently loaded assembly in which the specified type is defined.</summary>
        public static Assembly? GetAssembly(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            // This requires runtime support to map types to our Assembly implementation
            return null;
        }

        /// <summary>Gets the assembly that contains the code that is currently executing.</summary>
        public static Assembly GetExecutingAssembly()
        {
            throw new NotImplementedException();
        }

        /// <summary>Returns the Assembly of the method that invoked the currently executing method.</summary>
        public static Assembly GetCallingAssembly()
        {
            throw new NotImplementedException();
        }

        /// <summary>Gets the process executable in the default application domain.</summary>
        public static Assembly? GetEntryAssembly()
        {
            return null;
        }

        /// <summary>Loads an assembly given its AssemblyName.</summary>
        public static Assembly Load(AssemblyName assemblyRef)
        {
            throw new NotImplementedException();
        }

        /// <summary>Loads an assembly given its display name.</summary>
        public static Assembly Load(string assemblyString)
        {
            throw new NotImplementedException();
        }

        /// <summary>Loads the assembly with a common object file format (COFF) based image containing an emitted assembly.</summary>
        public static Assembly Load(byte[] rawAssembly)
        {
            throw new NotImplementedException();
        }

        /// <summary>Loads the assembly with a common object file format (COFF) based image containing an emitted assembly, optionally including symbols for the assembly.</summary>
        public static Assembly Load(byte[] rawAssembly, byte[]? rawSymbolStore)
        {
            throw new NotImplementedException();
        }

        /// <summary>Loads an assembly given its file name or path.</summary>
        public static Assembly LoadFrom(string assemblyFile)
        {
            throw new NotImplementedException();
        }

        /// <summary>Loads an assembly given its path.</summary>
        public static Assembly LoadFile(string path)
        {
            throw new NotImplementedException();
        }

        /// <summary>Returns a string representation of this object.</summary>
        public override string ToString()
        {
            return FullName;
        }

        /// <summary>Determines whether this assembly and the specified object are equal.</summary>
        public override bool Equals(object? o)
        {
            return o is Assembly asm && asm.FullName == FullName;
        }

        /// <summary>Returns the hash code for this instance.</summary>
        public override int GetHashCode()
        {
            return FullName.GetHashCode();
        }

        /// <summary>Indicates whether two Assembly objects are equal.</summary>
        public static bool operator ==(Assembly? left, Assembly? right)
        {
            if (left is null) return right is null;
            return left.Equals(right);
        }

        /// <summary>Indicates whether two Assembly objects are not equal.</summary>
        public static bool operator !=(Assembly? left, Assembly? right)
        {
            return !(left == right);
        }
    }

    /// <summary>
    /// Describes an assembly's unique identity in full.
    /// </summary>
    public sealed class AssemblyName : ICloneable
    {
        private string? _name;
        private Version? _version;
        private System.Globalization.CultureInfo? _cultureInfo;
        private byte[]? _publicKeyToken;
        private string? _codeBase;
        private AssemblyNameFlags _flags;

        /// <summary>Initializes a new instance of the AssemblyName class.</summary>
        public AssemblyName()
        {
        }

        /// <summary>Initializes a new instance of the AssemblyName class with the specified display name.</summary>
        public AssemblyName(string assemblyName)
        {
            if (assemblyName == null) throw new ArgumentNullException(nameof(assemblyName));
            _name = assemblyName;
        }

        /// <summary>Gets or sets the simple name of the assembly.</summary>
        public string? Name
        {
            get => _name;
            set => _name = value;
        }

        /// <summary>Gets or sets the major, minor, build, and revision numbers of the assembly.</summary>
        public Version? Version
        {
            get => _version;
            set => _version = value;
        }

        /// <summary>Gets or sets the culture supported by the assembly.</summary>
        public System.Globalization.CultureInfo? CultureInfo
        {
            get => _cultureInfo;
            set => _cultureInfo = value;
        }

        /// <summary>Gets or sets the name of the culture associated with the assembly.</summary>
        public string? CultureName
        {
            get => _cultureInfo?.Name;
            set => _cultureInfo = value == null ? null : new System.Globalization.CultureInfo(value);
        }

        /// <summary>Gets or sets the location of the assembly as a URL.</summary>
        public string? CodeBase
        {
            get => _codeBase;
            set => _codeBase = value;
        }

        /// <summary>Gets or sets the attributes of the assembly.</summary>
        public AssemblyNameFlags Flags
        {
            get => _flags;
            set => _flags = value;
        }

        /// <summary>Gets the full name of the assembly.</summary>
        public string FullName
        {
            get
            {
                if (_name == null) return string.Empty;
                var sb = new System.Text.StringBuilder(_name);
                if (_version != null)
                {
                    sb.Append(", Version=");
                    sb.Append(_version.ToString());
                }
                return sb.ToString();
            }
        }

        /// <summary>Gets the public key token, which is the last 8 bytes of the SHA-1 hash of the public key under which the assembly is signed.</summary>
        public byte[]? GetPublicKeyToken()
        {
            return _publicKeyToken;
        }

        /// <summary>Sets the public key token, which is the last 8 bytes of the SHA-1 hash of the public key under which the assembly is signed.</summary>
        public void SetPublicKeyToken(byte[]? publicKeyToken)
        {
            _publicKeyToken = publicKeyToken;
        }

        /// <summary>Gets the public key of the assembly.</summary>
        public byte[]? GetPublicKey()
        {
            return null;
        }

        /// <summary>Sets the public key identifying the assembly.</summary>
        public void SetPublicKey(byte[]? publicKey)
        {
        }

        /// <summary>Makes a copy of this AssemblyName object.</summary>
        public object Clone()
        {
            var clone = new AssemblyName();
            clone._name = _name;
            clone._version = _version;
            clone._cultureInfo = _cultureInfo;
            clone._publicKeyToken = _publicKeyToken;
            clone._codeBase = _codeBase;
            clone._flags = _flags;
            return clone;
        }

        /// <summary>Returns the full name of the assembly.</summary>
        public override string ToString()
        {
            return FullName;
        }

        /// <summary>Gets the AssemblyName for a given file.</summary>
        public static AssemblyName GetAssemblyName(string assemblyFile)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Provides information about an assembly's manifest resource.
    /// </summary>
    public class ManifestResourceInfo
    {
        /// <summary>Gets the containing assembly for the manifest resource.</summary>
        public virtual Assembly? ReferencedAssembly => null;

        /// <summary>Gets the name of the file that contains the manifest resource.</summary>
        public virtual string? FileName => null;

        /// <summary>Gets the manifest resource's location.</summary>
        public virtual ResourceLocation ResourceLocation => ResourceLocation.ContainedInManifestFile;
    }

    /// <summary>
    /// Specifies the locations of a manifest resource.</summary>
    [Flags]
    public enum ResourceLocation
    {
        /// <summary>Specifies that the resource is contained in a manifest file.</summary>
        ContainedInManifestFile = 2,

        /// <summary>Specifies that the resource is contained in another assembly.</summary>
        ContainedInAnotherAssembly = 1,

        /// <summary>Specifies an embedded (that is, non-linked) resource.</summary>
        Embedded = 4,
    }

    /// <summary>
    /// Provides information about the attributes that have been applied to an assembly.
    /// </summary>
    [Flags]
    public enum AssemblyNameFlags
    {
        /// <summary>Specifies that no flags are in effect.</summary>
        None = 0,

        /// <summary>Specifies that a public key is formed from the full public key rather than the public key token.</summary>
        PublicKey = 1,

        /// <summary>Specifies that just-in-time (JIT) compiler optimization is disabled for the assembly.</summary>
        EnableJITcompileOptimizer = 16384,

        /// <summary>Specifies that just-in-time (JIT) compiler tracking is enabled for the assembly.</summary>
        EnableJITcompileTracking = 32768,

        /// <summary>Specifies that the assembly can be retargeted at runtime to an assembly from a different publisher.</summary>
        Retargetable = 256,
    }

    /// <summary>
    /// Performs reflection on a module.
    /// </summary>
    public abstract class Module : ICustomAttributeProvider
    {
        /// <summary>Gets the appropriate Assembly for this instance of Module.</summary>
        public virtual Assembly Assembly => throw new NotImplementedException();

        /// <summary>Gets a String representing the fully qualified name and path to this module.</summary>
        public virtual string FullyQualifiedName => string.Empty;

        /// <summary>Gets a string representing the name of the module with the path removed.</summary>
        public virtual string Name => string.Empty;

        /// <summary>Gets a token that identifies the module in metadata.</summary>
        public virtual int MetadataToken => 0;

        /// <summary>Gets the GUID for this module.</summary>
        public virtual Guid ModuleVersionId => Guid.Empty;

        /// <summary>Gets a pair of values indicating the nature of the code in a module and the platform targeted by the module.</summary>
        public virtual void GetPEKind(out PortableExecutableKinds peKind, out ImageFileMachine machine)
        {
            peKind = PortableExecutableKinds.ILOnly;
            machine = ImageFileMachine.I386;
        }

        /// <summary>Gets a value indicating whether the object is a resource.</summary>
        public virtual bool IsResource() => false;

        /// <summary>Returns the specified type.</summary>
        public virtual Type? GetType(string className)
        {
            return GetType(className, false, false);
        }

        /// <summary>Returns the specified type, searching the module with the specified case sensitivity.</summary>
        public virtual Type? GetType(string className, bool ignoreCase)
        {
            return GetType(className, false, ignoreCase);
        }

        /// <summary>Returns the specified type, specifying whether to throw an exception if the type is not found.</summary>
        public virtual Type? GetType(string className, bool throwOnError, bool ignoreCase)
        {
            if (className == null) throw new ArgumentNullException(nameof(className));
            return null;
        }

        /// <summary>Returns all the types defined within this module.</summary>
        public virtual Type[] GetTypes()
        {
            return Array.Empty<Type>();
        }

        /// <summary>Returns an array of classes accepted by the given filter and filter criteria.</summary>
        public virtual Type[] FindTypes(TypeFilter? filter, object? filterCriteria)
        {
            var types = GetTypes();
            if (filter == null) return types;

            var result = new List<Type>();
            foreach (var type in types)
            {
                if (filter(type, filterCriteria))
                    result.Add(type);
            }
            return result.ToArray();
        }

        /// <summary>Gets a collection of custom attributes.</summary>
        public virtual object[] GetCustomAttributes(bool inherit)
        {
            return Array.Empty<object>();
        }

        /// <summary>Gets a collection of custom attributes of the specified type.</summary>
        public virtual object[] GetCustomAttributes(Type attributeType, bool inherit)
        {
            if (attributeType == null) throw new ArgumentNullException(nameof(attributeType));
            return Array.Empty<object>();
        }

        /// <summary>Determines whether custom attributes of the specified type are applied to this module.</summary>
        public virtual bool IsDefined(Type attributeType, bool inherit)
        {
            if (attributeType == null) throw new ArgumentNullException(nameof(attributeType));
            return false;
        }

        /// <summary>Returns a string representation of this object.</summary>
        public override string ToString()
        {
            return Name;
        }
    }

    /// <summary>
    /// Represents a filter to find types in a module.
    /// </summary>
    public delegate bool TypeFilter(Type m, object? filterCriteria);

    /// <summary>
    /// Specifies the nature of the code in an executable file.
    /// </summary>
    [Flags]
    public enum PortableExecutableKinds
    {
        /// <summary>The executable is not in the portable executable (PE) file format.</summary>
        NotAPortableExecutableImage = 0,

        /// <summary>The executable contains only MSIL, and is therefore neutral with respect to 32-bit or 64-bit platforms.</summary>
        ILOnly = 1,

        /// <summary>The executable can be run only on a 32-bit platform, or in the 32-bit Windows on Windows (WoW) environment on a 64-bit platform.</summary>
        Required32Bit = 2,

        /// <summary>The executable requires a 64-bit platform.</summary>
        PE32Plus = 4,

        /// <summary>The executable contains pure unmanaged code.</summary>
        Unmanaged32Bit = 8,

        /// <summary>The executable is platform-agnostic but should be run on a 32-bit platform whenever possible.</summary>
        Preferred32Bit = 16,
    }

    /// <summary>
    /// Identifies the platform targeted by an executable.
    /// </summary>
    public enum ImageFileMachine
    {
        /// <summary>Targets a 32-bit Intel processor.</summary>
        I386 = 332,

        /// <summary>Targets a 64-bit AMD processor.</summary>
        AMD64 = 34404,

        /// <summary>Targets a 64-bit Intel Itanium processor.</summary>
        IA64 = 512,

        /// <summary>Targets an ARM processor.</summary>
        ARM = 452,
    }
}
