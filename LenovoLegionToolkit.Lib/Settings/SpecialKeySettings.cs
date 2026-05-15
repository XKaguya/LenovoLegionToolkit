using System;
using System.Collections.Generic;
using System.IO;
using LenovoLegionToolkit.Lib.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LenovoLegionToolkit.Lib.Settings;

public class SpecialKeySettings : AbstractSettings<SpecialKeySettings.SpecialKeySettingsStore>
{
    public class SpecialKeySettingsStore
    {
        public Dictionary<int, CustomSpecialKey> KeyModes { get; set; } = [];
        public Dictionary<int, List<Guid>> KeyActions { get; set; } = [];
        public Dictionary<int, string> KeyDescriptions { get; set; } = [];

        public Guid? SmartKeySinglePressActionId { get; set; }
        public Guid? SmartKeyDoublePressActionId { get; set; }
        public List<Guid> SmartKeySinglePressActionList { get; set; } = [];
        public List<Guid> SmartKeyDoublePressActionList { get; set; } = [];
    }

    public SpecialKeySettings() : base("special_key.json") { }

    public override SpecialKeySettingsStore? LoadStore()
    {
        var store = base.LoadStore() ?? new SpecialKeySettingsStore();
        MigrateFromLegacy(store);
        return store;
    }

    private void MigrateFromLegacy(SpecialKeySettingsStore store)
    {
        if (store.KeyModes.Count > 0)
            return;

        var fnF9 = (int)SpecialKey.FnF9;

        if (TryReadLegacySmartKey(out var singleList, out var doubleList))
        {
            if (singleList is { Count: > 0 })
            {
                store.KeyModes[fnF9] = CustomSpecialKey.Custom;
                store.KeyActions[fnF9] = singleList;
            }

            if (store.KeyActions.GetValueOrDefault(fnF9) is null && doubleList is { Count: > 0 })
            {
                store.KeyModes[fnF9] = CustomSpecialKey.Custom;
                store.KeyActions[fnF9] = doubleList;
            }

            Log.Instance.Trace($"Migrated legacy SmartKey settings");
        }

        store.SmartKeySinglePressActionId = null;
        store.SmartKeyDoublePressActionId = null;
        store.SmartKeySinglePressActionList.Clear();
        store.SmartKeyDoublePressActionList.Clear();
    }

    private static bool TryReadLegacySmartKey(out List<Guid>? singleList, out List<Guid>? doubleList)
    {
        singleList = null;
        doubleList = null;

        try
        {
            var path = Path.Combine(Folders.AppData, "settings.json");
            if (!File.Exists(path))
                return false;

            var json = File.ReadAllText(path);
            var root = JObject.Parse(json);

            singleList = ParseGuidList(root, "SmartKeySinglePressActionId", "SmartKeySinglePressActionList");
            doubleList = ParseGuidList(root, "SmartKeyDoublePressActionId", "SmartKeyDoublePressActionList");

            return singleList is { Count: > 0 } || doubleList is { Count: > 0 };
        }
        catch
        {
            return false;
        }
    }

    private static List<Guid>? ParseGuidList(JObject root, string idField, string listField)
    {
        var id = root[idField]?.ToObject<Guid?>();
        var list = root[listField]?.ToObject<List<Guid>>() ?? [];

        if (id is null)
            return [];

        if (id is { } g && g != Guid.Empty)
        {
            if (list.Count == 0)
                list.Add(g);
            return list;
        }

        return null;
    }
}
