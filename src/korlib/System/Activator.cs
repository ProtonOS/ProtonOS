// ProtonOS korlib - Activator
// Contains methods to create types of objects locally.

using System.Reflection;
using System.Globalization;

namespace System
{
    /// <summary>
    /// Contains methods to create types of objects locally.
    /// </summary>
    public static class Activator
    {
        /// <summary>Creates an instance of the specified type using that type's parameterless constructor.</summary>
        public static object? CreateInstance(Type type)
        {
            return CreateInstance(type, false);
        }

        /// <summary>Creates an instance of the specified type using that type's parameterless constructor.</summary>
        public static object? CreateInstance(Type type, bool nonPublic)
        {
            if (type is null) throw new ArgumentNullException(nameof(type));

            // This is a stub - real implementation would use JIT/runtime to instantiate
            throw new NotSupportedException("Activator.CreateInstance requires runtime support.");
        }

        /// <summary>Creates an instance of the specified type using the constructor that best matches the specified parameters.</summary>
        public static object? CreateInstance(Type type, params object?[]? args)
        {
            return CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.CreateInstance, null, args, null);
        }

        /// <summary>Creates an instance of the specified type using the constructor that best matches the specified parameters.</summary>
        public static object? CreateInstance(Type type, object?[]? args, object?[]? activationAttributes)
        {
            return CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.CreateInstance, null, args, null, activationAttributes);
        }

        /// <summary>Creates an instance of the specified type using the constructor that best matches the specified parameters.</summary>
        public static object? CreateInstance(Type type, BindingFlags bindingAttr, Binder? binder, object?[]? args, CultureInfo? culture)
        {
            return CreateInstance(type, bindingAttr, binder, args, culture, null);
        }

        /// <summary>Creates an instance of the specified type using the constructor that best matches the specified parameters.</summary>
        public static object? CreateInstance(Type type, BindingFlags bindingAttr, Binder? binder, object?[]? args, CultureInfo? culture, object?[]? activationAttributes)
        {
            if (type is null) throw new ArgumentNullException(nameof(type));

            // This is a stub - real implementation would use JIT/runtime to instantiate
            throw new NotSupportedException("Activator.CreateInstance requires runtime support.");
        }

        /// <summary>Creates an instance of the type whose name is specified.</summary>
        public static ObjectHandle? CreateInstance(string assemblyName, string typeName)
        {
            throw new NotSupportedException("Activator.CreateInstance with assembly name requires runtime support.");
        }

        /// <summary>Creates an instance of the type designated by the specified generic type parameter, using the parameterless constructor.</summary>
        public static T CreateInstance<T>()
        {
            return (T)CreateInstance(typeof(T))!;
        }
    }

    /// <summary>
    /// Wraps marshal-by-value object references, allowing them to be returned through a reference.
    /// </summary>
    public class ObjectHandle
    {
        private readonly object? _wrappedObject;

        /// <summary>Initializes a new instance of the ObjectHandle class, wrapping the given object.</summary>
        public ObjectHandle(object? o)
        {
            _wrappedObject = o;
        }

        /// <summary>Returns the wrapped object.</summary>
        public object? Unwrap()
        {
            return _wrappedObject;
        }
    }
}
