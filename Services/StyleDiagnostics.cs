using System;
using System.Reflection;
using IMT.Manager;
using UnityEngine;

namespace CSM.IMTSync.Services
{
    internal static class StyleDiagnostics
    {
        public static string Describe(Style style)
        {
            if (style == null) return "style=null";

            var type = style.GetType();
            var details = DescribePrefabProperty(style, "Prefab");
            if (details == null)
                details = DescribePrefabProperty(style, "Decal");

            return details == null
                ? $"style={type.Name} type={style.Type}"
                : $"style={type.Name} type={style.Type} {details}";
        }

        public static void RepairPrefabRefs(Style style)
        {
            if (style == null) return;
            SafeRepairPrefabProperty(style, "Prefab");
            SafeRepairPrefabProperty(style, "Decal");
        }

        private static string DescribePrefabProperty(object owner, string propertyName)
        {
            var prop = owner.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            var pref = prop?.GetValue(owner, null);
            if (pref == null) return null;

            var raw = ReadStringProperty(pref, "RawName");
            var value = ReadObjectProperty(pref, "Value");
            return $"{propertyName}.raw='{raw ?? ""}' {propertyName}.resolved={(value == null ? "false" : "true")}";
        }

        private static void RepairPrefabProperty(object owner, string propertyName)
        {
            var prop = owner.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            var pref = prop?.GetValue(owner, null);
            if (pref == null) return;

            var valueProp = pref.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
            if (valueProp == null || ReadPropertyValue(pref, valueProp) != null) return;

            var raw = ReadStringProperty(pref, "RawName");
            if (string.IsNullOrEmpty(raw)) return;

            var valueType = valueProp.PropertyType;
            object resolved = null;
            if (valueType == typeof(PropInfo))
                resolved = PrefabCollection<PropInfo>.FindLoaded(raw);
            else if (valueType == typeof(TreeInfo))
                resolved = PrefabCollection<TreeInfo>.FindLoaded(raw);
            else if (valueType == typeof(NetInfo))
                resolved = PrefabCollection<NetInfo>.FindLoaded(raw);

            if (resolved != null)
                valueProp.SetValue(pref, resolved, null);
        }

        private static void SafeRepairPrefabProperty(object owner, string propertyName)
        {
            try { RepairPrefabProperty(owner, propertyName); }
            catch (Exception ex) { Log.Warn($"RepairPrefabProperty({propertyName}) threw: {ex.Message}"); }
        }

        private static string ReadStringProperty(object obj, string propertyName)
        {
            return ReadObjectProperty(obj, propertyName) as string;
        }

        private static object ReadObjectProperty(object obj, string propertyName)
        {
            if (obj == null) return null;
            var prop = obj.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            return ReadPropertyValue(obj, prop);
        }

        private static object ReadPropertyValue(object obj, PropertyInfo prop)
        {
            if (obj == null || prop == null) return null;
            try
            {
                var getter = prop.GetGetMethod(true);
                return getter == null ? null : getter.Invoke(obj, null);
            }
            catch
            {
                return null;
            }
        }
    }
}
