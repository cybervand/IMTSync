using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml.Linq;
using ColossalFramework.Importers;
using CSM.IMTSync.Commands;
using IMT;
using IMT.Manager;
using ModsCommon;

namespace CSM.IMTSync.Services
{
    internal static class TemplateSync
    {
        private static readonly object PendingPreviewLock = new object();
        private static readonly Dictionary<Guid, byte[]> PendingPreviewPngs = new Dictionary<Guid, byte[]>();

        private static readonly FieldInfo StyleTemplatesField =
            typeof(TemplateManager<StyleTemplate>).GetField("<TemplatesDictionary>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo IntersectionTemplatesField =
            typeof(TemplateManager<IntersectionTemplate>).GetField("<TemplatesDictionary>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);

        public static IMTActionCommand BuildUpsert(StyleTemplate template)
        {
            if (template == null || template.IsAsset) return null;
            if (template.GetType() != typeof(StyleTemplate))
            {
                Log.Warn($"BuildUpsert(StyleTemplate): ignoring runtime template type {template.GetType().FullName}");
                return null;
            }
            return new IMTActionCommand
            {
                Type = IMTActionType.UpsertStyleTemplate,
                TemplateId = template.Id.ToString(),
                TemplateXml = StyleSerializer.ToXml(template.ToXml()),
            };
        }

        public static IMTActionCommand BuildDelete(StyleTemplate template)
        {
            if (template == null || template.IsAsset) return null;
            if (template.GetType() != typeof(StyleTemplate)) return null;
            return new IMTActionCommand
            {
                Type = IMTActionType.DeleteStyleTemplate,
                TemplateId = template.Id.ToString(),
            };
        }

        public static IMTActionCommand BuildUpsert(IntersectionTemplate template, Image screenshot = null)
        {
            if (template == null || template.IsAsset) return null;
            if (template.GetType() != typeof(IntersectionTemplate))
            {
                Log.Warn($"BuildUpsert(IntersectionTemplate): ignoring runtime template type {template.GetType().FullName}");
                return null;
            }
            return new IMTActionCommand
            {
                Type = IMTActionType.UpsertIntersectionTemplate,
                TemplateId = template.Id.ToString(),
                TemplateXml = StyleSerializer.ToXml(template.ToXml()),
                TemplatePreviewPng = EncodePreview(template, screenshot),
            };
        }

        public static IMTActionCommand BuildDelete(IntersectionTemplate template)
        {
            if (template == null || template.IsAsset) return null;
            if (template.GetType() != typeof(IntersectionTemplate)) return null;
            return new IMTActionCommand
            {
                Type = IMTActionType.DeleteIntersectionTemplate,
                TemplateId = template.Id.ToString(),
            };
        }

        public static bool ApplyUpsert(IMTActionCommand cmd)
        {
            if (cmd == null || string.IsNullOrEmpty(cmd.TemplateXml)) return false;

            XElement xml;
            try { xml = StyleSerializer.LoadXml(cmd.TemplateXml); }
            catch (Exception ex) { Log.Warn("Template upsert XML parse threw: " + ex.Message); return false; }
            if (xml == null) return false;

            switch (cmd.Type)
            {
                case IMTActionType.UpsertStyleTemplate:
                    if (!StyleTemplate.FromXml(xml, out var styleTemplate) || styleTemplate == null)
                    { Log.Warn("UpsertStyleTemplate: failed to parse StyleTemplate XML"); return false; }
                    UpsertStyleTemplate(styleTemplate);
                    Log.Info($"Applied remote StyleTemplate upsert \"{styleTemplate.Name}\" {styleTemplate.Id}");
                    return true;

                case IMTActionType.UpsertIntersectionTemplate:
                    var expectedId = GetTemplateId(cmd, xml);
                    byte[] hiddenPreview = null;
                    if (expectedId.HasValue)
                        hiddenPreview = HidePreviewPngForParse(expectedId.Value);

                    if (!IntersectionTemplate.FromXml(xml, out var intersectionTemplate) || intersectionTemplate == null)
                    {
                        if (expectedId.HasValue && hiddenPreview != null)
                            SavePreviewPng(expectedId.Value, hiddenPreview);
                        Log.Warn("UpsertIntersectionTemplate: failed to parse IntersectionTemplate XML");
                        return false;
                    }

                    SavePreviewPng(intersectionTemplate.Id, cmd.TemplatePreviewPng ?? hiddenPreview);
                    UpsertIntersectionTemplate(intersectionTemplate);
                    QueuePreview(intersectionTemplate.Id, cmd.TemplatePreviewPng ?? hiddenPreview);
                    Log.Info($"Applied remote IntersectionTemplate upsert \"{intersectionTemplate.Name}\" {intersectionTemplate.Id}");
                    return true;

                default:
                    return false;
            }
        }

        public static bool ApplyDelete(IMTActionCommand cmd)
        {
            if (cmd == null || string.IsNullOrEmpty(cmd.TemplateId)) return false;
            Guid id;
            try { id = new Guid(cmd.TemplateId); }
            catch (Exception)
            { Log.Warn($"{cmd.Type}: invalid template id {cmd.TemplateId}"); return false; }

            switch (cmd.Type)
            {
                case IMTActionType.DeleteStyleTemplate:
                    RemoveStyleTemplate(id);
                    Log.Info($"Applied remote StyleTemplate delete {id}");
                    return true;

                case IMTActionType.DeleteIntersectionTemplate:
                    RemoveIntersectionTemplate(id);
                    Log.Info($"Applied remote IntersectionTemplate delete {id}");
                    return true;

                default:
                    return false;
            }
        }

        private static void UpsertStyleTemplate(StyleTemplate template)
        {
            var manager = SingletonManager<StyleTemplateManager>.Instance;
            var dict = StyleTemplatesField?.GetValue(manager) as Dictionary<Guid, StyleTemplate>;
            if (dict == null)
            {
                manager.AddTemplate(template);
                manager.TemplateChanged(template);
                return;
            }

            dict[template.Id] = template;
            manager.TemplateChanged(template);
        }

        private static void UpsertIntersectionTemplate(IntersectionTemplate template)
        {
            var manager = SingletonManager<IntersectionTemplateManager>.Instance;
            var dict = IntersectionTemplatesField?.GetValue(manager) as Dictionary<Guid, IntersectionTemplate>;
            if (dict == null)
            {
                manager.AddTemplate(template);
                manager.TemplateChanged(template);
                return;
            }

            dict[template.Id] = template;
            manager.TemplateChanged(template);
        }

        private static void RemoveStyleTemplate(Guid id)
        {
            var manager = SingletonManager<StyleTemplateManager>.Instance;
            var dict = StyleTemplatesField?.GetValue(manager) as Dictionary<Guid, StyleTemplate>;
            if (dict == null)
            {
                Log.Warn("DeleteStyleTemplate: template dictionary unavailable");
                return;
            }
            if (!dict.TryGetValue(id, out var template))
            {
                Log.Info($"DeleteStyleTemplate: template {id} not present locally");
                return;
            }
            manager.DeleteTemplate(template);
        }

        private static void RemoveIntersectionTemplate(Guid id)
        {
            var manager = SingletonManager<IntersectionTemplateManager>.Instance;
            var dict = IntersectionTemplatesField?.GetValue(manager) as Dictionary<Guid, IntersectionTemplate>;
            if (dict == null)
            {
                Log.Warn("DeleteIntersectionTemplate: template dictionary unavailable");
                return;
            }
            if (!dict.TryGetValue(id, out var template))
            {
                Log.Info($"DeleteIntersectionTemplate: template {id} not present locally");
                return;
            }
            manager.DeleteTemplate(template);
        }

        private static byte[] EncodePreview(IntersectionTemplate template, Image screenshot)
        {
            try
            {
                if (screenshot != null)
                    return screenshot.GetFormattedImage(Image.BufferFileFormat.PNG);
            }
            catch (Exception ex) { Log.Warn("Encode preset screenshot from Image threw: " + ex.Message); }

            try
            {
                var fromDisk = template == null ? null : ReadPreviewPng(template.Id);
                if (fromDisk != null && fromDisk.Length > 0)
                    return fromDisk;
            }
            catch (Exception ex) { Log.Warn("Encode preset screenshot from disk threw: " + ex.Message); }

            return null;
        }

        private static Guid? GetTemplateId(IMTActionCommand cmd, XElement xml)
        {
            if (cmd != null && TryParseGuid(cmd.TemplateId, out var commandId))
                return commandId;

            var value = xml?.Attribute("Id")?.Value ?? xml?.Attribute("id")?.Value ?? xml?.Attribute("ID")?.Value;
            if (TryParseGuid(value, out var xmlId))
                return xmlId;

            return null;
        }

        private static bool TryParseGuid(string value, out Guid id)
        {
            try
            {
                id = string.IsNullOrEmpty(value) ? Guid.Empty : new Guid(value);
                return !string.IsNullOrEmpty(value);
            }
            catch (Exception)
            {
                id = Guid.Empty;
                return false;
            }
        }

        private static byte[] HidePreviewPngForParse(Guid id)
        {
            try
            {
                var path = GetPreviewPath(id);
                if (!File.Exists(path)) return null;

                var bytes = File.ReadAllBytes(path);
                File.Delete(path);
                return bytes;
            }
            catch (Exception ex) { Log.Warn($"Hide preset screenshot {id} threw: " + ex.Message); return null; }
        }

        private static void SavePreviewPng(Guid id, byte[] png)
        {
            if (png == null || png.Length == 0) return;

            try
            {
                var path = GetPreviewPath(id);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, png);
            }
            catch (Exception ex) { Log.Warn($"Save preset screenshot {id} threw: " + ex.Message); }
        }

        private static byte[] ReadPreviewPng(Guid id)
        {
            try
            {
                var path = GetPreviewPath(id);
                return File.Exists(path) ? File.ReadAllBytes(path) : null;
            }
            catch (Exception ex)
            {
                Log.Warn($"Read preset screenshot {id} threw: " + ex.Message);
                return null;
            }
        }

        private static void QueuePreview(Guid id, byte[] png)
        {
            if (png == null || png.Length == 0) return;
            lock (PendingPreviewLock)
            {
                PendingPreviewPngs[id] = png;
            }
        }

        public static void FlushDeferredPreviews()
        {
            List<KeyValuePair<Guid, byte[]>> pending;
            lock (PendingPreviewLock)
            {
                if (PendingPreviewPngs.Count == 0) return;
                pending = new List<KeyValuePair<Guid, byte[]>>(PendingPreviewPngs);
                PendingPreviewPngs.Clear();
            }

            foreach (var item in pending)
                TrySetPreviewTexture(item.Key, item.Value);
        }

        private static void TrySetPreviewTexture(Guid id, byte[] png)
        {
            try
            {
                var manager = SingletonManager<IntersectionTemplateManager>.Instance;
                var dict = IntersectionTemplatesField?.GetValue(manager) as Dictionary<Guid, IntersectionTemplate>;
                if (dict == null || !dict.TryGetValue(id, out var template) || template == null)
                    return;

                var image = new Image(png);
                var texture = image.CreateTexture();
                if (texture == null) return;

                template.Preview = texture;
                ImtUiRefresh.RequestCurrentPanel();
                Log.Info($"Applied deferred preset preview texture {id}.");
            }
            catch (Exception ex) { Log.Warn($"Deferred preset preview texture {id} threw: " + ex.Message); }
        }

        private static string GetPreviewPath(Guid id)
        {
            return Path.Combine(
                Path.Combine(
                    Path.Combine(Directory.GetCurrentDirectory(), "IntersectionMarkingTool"),
                    "TemplateScreenshots"),
                id + ".png");
        }
    }
}
