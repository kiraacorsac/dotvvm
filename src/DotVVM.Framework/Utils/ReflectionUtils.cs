using DotVVM.Framework.Controls;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Dynamic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using DotVVM.Framework.Compilation.ControlTree;
using DotVVM.Framework.Compilation.ControlTree.Resolved;
using System.Security.Cryptography;
using System.Text;

#if DotNetCore
using Microsoft.Extensions.DependencyModel;
#endif

namespace DotVVM.Framework.Utils
{
    public static class ReflectionUtils
    {
        public static IEnumerable<Assembly> GetAllAssemblies()
        {
#if DotNetCore
            return DependencyContext.Default.GetDefaultAssemblyNames().Select(Assembly.Load);
#else
            return AppDomain.CurrentDomain.GetAssemblies();
#endif
        }

        public static bool IsFullName(string typeName)
            => typeName.Contains(".");

        public static bool IsAssemblyNamespace(string fullName)
            => GetAllNamespaces().Contains(fullName, StringComparer.Ordinal);

        public static ISet<string> GetAllNamespaces()
            => new HashSet<string>(GetAllAssemblies()
                .SelectMany(a => a.GetLoadableTypes()
                .Select(t => t.Namespace))
                .Distinct()
                .ToList());

        /// <summary>
        /// Gets the property name from lambda expression, e.g. 'a => a.FirstName'
        /// </summary>
        public static MemberInfo GetMemberFromExpression(Expression expression)
        {
            var body = expression as MemberExpression;

            if (body == null)
            {
                var unaryExpressionBody = (UnaryExpression)expression;
                body = unaryExpressionBody.Operand as MemberExpression;
            }

            return body.Member;
        }

        // http://haacked.com/archive/2012/07/23/get-all-types-in-an-assembly.aspx/
        public static IEnumerable<Type> GetLoadableTypes(this Assembly assembly)
        {
            if (assembly == null) throw new ArgumentNullException("assembly");
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                return e.Types.Where(t => t != null);
            }
        }


        /// <summary>
        /// Gets filesystem path of assembly CodeBase
        /// http://stackoverflow.com/questions/52797/how-do-i-get-the-path-of-the-assembly-the-code-is-in
        /// </summary>
        public static string GetCodeBasePath(this Assembly assembly)
        {
            string codeBase = assembly.CodeBase;
            UriBuilder uri = new UriBuilder(codeBase);
            return Uri.UnescapeDataString(uri.Path);
        }

        /// <summary>
        /// Gets the specified property of a given object.
        /// </summary>
        public static object GetObjectPropertyValue(object item, string propertyName, out PropertyInfo prop)
        {
            prop = null;
            if (item == null) return null;

            var type = item.GetType();
            prop = type.GetProperty(propertyName);
            if (prop == null)
            {
                throw new Exception(String.Format("The object of type {0} does not have a property named {1}!", type, propertyName));     // TODO: exception handling
            }
            return prop.GetValue(item);
        }

        /// <summary>
        /// Extracts the value of a specified property and converts it to string. If the property name is empty, returns a string representation of a given object.
        /// Null values are converted to empty string.
        /// </summary>
        public static string ExtractMemberStringValue(object item, string propertyName)
        {
            if (!string.IsNullOrEmpty(propertyName))
            {
                PropertyInfo prop;
                item = GetObjectPropertyValue(item, propertyName, out prop);
            }
            return item?.ToString() ?? "";
        }

        /// <summary>
        /// Converts a value to a specified type
        /// </summary>
        public static object ConvertValue(object value, Type type)
        {
            var typeinfo = type.GetTypeInfo();
            // handle null values
            if ((value == null) && (typeinfo.IsValueType))
                return Activator.CreateInstance(type);

            // handle nullable types
            if (typeinfo.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                if ((value is string) && ((string)value == string.Empty))
                {
                    // value is an empty string, return null
                    return null;
                }
                else
                {
                    // value is not null
                    type = Nullable.GetUnderlyingType(type); typeinfo = type.GetTypeInfo();
                }
            }

            // handle exceptions
            if ((value is string) && (type == typeof(Guid)))
                return new Guid((string)value);
            if (type == typeof(object)) return value;

            // handle enums
            if (typeinfo.IsEnum && value is string)
            {
                var split = ((string)value).Split(',', '|');
                var isFlags = type.GetTypeInfo().IsDefined(typeof(FlagsAttribute));
                if (!isFlags && split.Length > 1) throw new Exception($"Enum {type} does allow multiple values. Use [FlagsAttribute] to allow it.");

                dynamic result = null;
                foreach (var val in split)
                {
                    try
                    {
                        if (result == null) result = Enum.Parse(type, val.Trim(), ignoreCase: true); // Enum.TryParse requires type parameter
                        else
                        {
                            result |= (dynamic)Enum.Parse(type, val.Trim(), ignoreCase: true);
                        }
                    }
                    catch (Exception ex)
                    {
                        throw new Exception($"The enum {type} does not allow a value '{val}'!", ex); // TODO: exception handling
                    }
                }
                return result;
            }

            // generic to string
            if (type == typeof(string))
            {
                return value.ToString();
            }

            if (value is string && type.IsArray)
            {
                var str = value as string;
                var objectArray = str.Split(',').Select(s => ConvertValue(s.Trim(), typeinfo.GetElementType()))
                    .ToArray();
                var array = Array.CreateInstance(type.GetElementType(), objectArray.Length);
                objectArray.CopyTo(array, 0);
                return array;
            }

            // convert
            return Convert.ChangeType(value, type);
        }

        public static Type FindType(string name, bool ignoreCase = false)
        {
            var stringComparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

            // Type.GetType might sometimes work well
            var type = Type.GetType(name, false, ignoreCase);
            if (type != null) return type;

            var split = name.Split(',');
            name = split[0];

            var assemblies = ReflectionUtils.GetAllAssemblies();
            if (split.Length > 1)
            {
                var assembly = split[1];
                return assemblies.Where(a => a.GetName().Name == assembly).Select(a => a.GetType(name)).FirstOrDefault(t => t != null);
            }

            type = assemblies.Where(a => name.StartsWith(a.GetName().Name, stringComparison)).Select(a => a.GetType(name, false, ignoreCase)).FirstOrDefault(t => t != null);
            if (type != null) return type;
            return assemblies.Select(a => a.GetType(name, false, ignoreCase)).FirstOrDefault(t => t != null);
        }

        public static Type GetEnumerableType(Type collectionType)
        {
            var result = TypeDescriptorUtils.GetCollectionItemType(new ResolvedTypeDescriptor(collectionType));
            if (result == null) return null;
            return ResolvedTypeDescriptor.ToSystemType(result);
        }

        private static readonly HashSet<Type> NumericTypes = new HashSet<Type>()
        {
            typeof (sbyte),
            typeof (byte),
            typeof (short),
            typeof (ushort),
            typeof (int),
            typeof (uint),
            typeof (long),
            typeof (ulong),
            typeof (char),
            typeof (float),
            typeof (double),
            typeof (decimal)
        };

        public static bool IsNumericType(this Type type)
        {
            return NumericTypes.Contains(type);
        }

        public static bool IsDynamicOrObject(this Type type)
        {
            return type.GetInterfaces().Contains(typeof(IDynamicMetaObjectProvider)) ||
                   type == typeof(object);
        }

        public static bool IsDelegate(this Type type)
        {
            return typeof(Delegate).IsAssignableFrom(type);
        }

        public static bool IsReferenceType(this Type type)
        {
            return type.IsArray || type.GetTypeInfo().IsClass || type.GetTypeInfo().IsInterface || type.IsDelegate();
        }

        public static bool IsDerivedFrom(this Type T, Type superClass)
        {
            return superClass.IsAssignableFrom(T);
        }

        public static bool Implements(this Type T, Type interfaceType)
        {
            return T.GetInterfaces().Any(x =>
            {
                return x.Name == interfaceType.Name;
            });
        }

        public static bool IsDynamic(this Type type)
        {
            return type.GetInterfaces().Contains(typeof(IDynamicMetaObjectProvider));
        }

        public static bool IsObject(this Type type)
        {
            return type == typeof(Object);
        }

        public static bool IsNullable(this Type type)
        {
            return Nullable.GetUnderlyingType(type) != null;
        }

        public static T GetCustomAttribute<T>(this ICustomAttributeProvider attributeProvider, bool inherit = true) => 
            (T)attributeProvider.GetCustomAttributes(typeof(T), inherit).FirstOrDefault();

        public static IEnumerable<T> GetCustomAttributes<T>(this ICustomAttributeProvider attributeProvider, bool inherit = true) =>
            attributeProvider.GetCustomAttributes(typeof(T), inherit).Cast<T>();

        public static string GetTypeHash(this Type type)
        {
            using (var sha1 = SHA1.Create())
            {
                var hashBytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(type.AssemblyQualifiedName));

                return Convert.ToBase64String(hashBytes);
            }
        }
    }
}