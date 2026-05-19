using System;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using IMT.Manager;
using IMT.Utilities;

namespace CSM.IMTSync.Services
{
    /// <summary>
    /// Round-trips an IMT Style through XML using IMT's own serialization. We don't model styles
    /// in protobuf - that would mean re-encoding all ~40 IMT style types and tracking every IMT
    /// update. Instead we ship the &lt;Style&gt; XML chunk verbatim.
    /// </summary>
    internal static class StyleSerializer
    {
        // IMT.Manager.Style has a static generic factory:
        //   static bool FromXml<T>(XElement config, ObjectsMap map, bool invert, bool typeChanged, out T style)
        // and ObjectsMap has only one ctor:  ObjectsMap(bool invert, bool isSimple)

        public static string ToXml(Style style)
        {
            if (style == null) return null;
            try { return style.ToXml().ToString(SaveOptions.DisableFormatting); }
            catch (Exception ex) { Log.Error("StyleSerializer.ToXml threw: " + ex); return null; }
        }

        public static string ToXml(XElement elem)
        {
            if (elem == null) return null;
            try { return elem.ToString(SaveOptions.DisableFormatting); }
            catch (Exception ex) { Log.Error("StyleSerializer.ToXml(XElement) threw: " + ex); return null; }
        }

        /// <summary>
        /// Mono-2.x-safe XElement loader. XElement.Parse(string) is BROKEN in CS's Mono
        /// runtime because it sets XmlReaderSettings.MaxCharactersFromEntities — a .NET 4.0+
        /// API that doesn't exist on the actual runtime. This wraps StringReader + XmlReader
        /// + XElement.Load. Returns null on failure.
        /// </summary>
        public static XElement LoadXml(string xml)
        {
            if (string.IsNullOrEmpty(xml)) return null;
            try
            {
                var settings = new XmlReaderSettings();  // no MaxCharactersFromEntities
                using (var sr = new StringReader(xml))
                using (var reader = XmlReader.Create(sr, settings))
                    return XElement.Load(reader);
            }
            catch (Exception ex)
            {
                Log.Error("StyleSerializer.LoadXml threw: " + ex);
                return null;
            }
        }

        public static bool TryFromXml<T>(string xml, out T style) where T : Style
        {
            style = null;
            var elem = LoadXml(xml);
            if (elem == null) return false;
            try
            {
                var map = new ObjectsMap(false, false);
                if (!Style.FromXml<T>(elem, map, false, false, out style))
                    return false;
                try { StyleDiagnostics.RepairPrefabRefs(style); }
                catch (Exception ex) { Log.Warn("StyleDiagnostics.RepairPrefabRefs threw: " + ex.Message); }
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("StyleSerializer.TryFromXml threw: " + ex);
                return false;
            }
        }
    }
}
