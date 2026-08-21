using System;
using System.Reflection;
using UnityEngine;

namespace SeaPowerNightVision
{
    /// <summary>
    /// Small reflection helpers used to drive whichever post-processing stack the game shipped
    /// with (URP/HDRP volumes or the legacy Post Processing Stack v2) without the mod having to
    /// reference those assemblies at compile time.
    /// </summary>
    internal static class Reflect
    {
        private const BindingFlags Instance = BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

        /// <summary>Finds a loaded type by full name, trying each candidate in order.</summary>
        public static Type FindType(params string[] fullNames)
        {
            foreach (var name in fullNames)
            {
                var direct = Type.GetType(name, false);
                if (direct != null)
                {
                    return direct;
                }
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var name in fullNames)
                {
                    try
                    {
                        var type = assembly.GetType(name, false);
                        if (type != null)
                        {
                            return type;
                        }
                    }
                    catch (Exception)
                    {
                        // Some dynamic assemblies throw on GetType; ignore them.
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Finds a type by short name that derives from <paramref name="baseType"/> — used to
        /// locate e.g. <c>ColorAdjustments</c> whether it lives in the URP or HDRP namespace.
        /// </summary>
        public static Type FindDerivedType(Type baseType, string shortName)
        {
            if (baseType == null)
            {
                return null;
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types;
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (var type in types)
                {
                    if (type != null && type.Name == shortName && baseType.IsAssignableFrom(type) && !type.IsAbstract)
                    {
                        return type;
                    }
                }
            }

            return null;
        }

        public static bool SetMember(object target, string name, object value)
        {
            if (target == null)
            {
                return false;
            }

            var type = target.GetType();

            var property = type.GetProperty(name, Instance);
            if (property != null && property.CanWrite)
            {
                property.SetValue(target, value, null);
                return true;
            }

            var field = type.GetField(name, Instance);
            if (field != null)
            {
                field.SetValue(target, value);
                return true;
            }

            return false;
        }

        public static object GetMember(object target, string name)
        {
            if (target == null)
            {
                return null;
            }

            var type = target.GetType();

            var property = type.GetProperty(name, Instance);
            if (property != null && property.CanRead)
            {
                return property.GetValue(target, null);
            }

            var field = type.GetField(name, Instance);
            return field != null ? field.GetValue(target) : null;
        }

        /// <summary>
        /// Sets a post-processing parameter. Both URP/HDRP's <c>VolumeParameter&lt;T&gt;</c> and
        /// PPv2's <c>ParameterOverride&lt;T&gt;</c> expose <c>value</c> and <c>overrideState</c>,
        /// so one helper drives both.
        /// </summary>
        public static bool SetParameter(object component, string parameterName, object value, bool overrideState = true)
        {
            var parameter = GetMember(component, parameterName);
            if (parameter == null)
            {
                return false;
            }

            var ok = SetMember(parameter, "value", value);
            SetMember(parameter, "overrideState", overrideState);
            return ok;
        }
    }
}
