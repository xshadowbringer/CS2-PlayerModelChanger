using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using Newtonsoft.Json;

namespace Service;


enum KeyType {
    STEAMID,
    PERMISSIONFLAG,
    PERMISSIONGROUP,
    ALL
}
abstract class EntryKey {
    public string content {get;set;}

    public EntryKey(string content) {
        this.content = content;
    }

    public bool Equals(EntryKey? key)
    {
        if (!(key is EntryKey)) {
            return false;
        }
        if (key == null) {
            return false;
        }
        if (this is AllKey) {
            return true;
        }
        return content.Equals(key.content);
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != this.GetType()) return false;
        return Equals((EntryKey) obj);
    }
    public override int GetHashCode()
    {
        return content.GetHashCode();
    }

    public abstract bool Fits(CCSPlayerController player);
}

class SteamIDKey : EntryKey {
    public SteamIDKey(string content) : base(content) {}
    public override bool Fits(CCSPlayerController player)
    {
        return this.content == player.AuthorizedSteamID?.SteamId64.ToString();    
    }
}
class PermissionFlagKey : EntryKey {
    public PermissionFlagKey(string content) : base(content) {}
    public override bool Fits(CCSPlayerController player)
    {
        return AdminManager.PlayerHasPermissions(player, new string[]{content});   
    }
}
class PermissionGroupKey : EntryKey {
    public PermissionGroupKey(string content) : base(content) {}
    public override bool Fits(CCSPlayerController player)
    {
        return AdminManager.PlayerInGroup(player, new string[]{content});   
    }
}
class AllKey : EntryKey {
    public AllKey(): base("") {}
    public override bool Fits(CCSPlayerController player)
    {
        return true;
    }
}

class DefaultModelEntry {
    public EntryKey key;
    public string item; // model index
    public string side;

    public DefaultModelEntry(EntryKey key, string item, string side)
    {
        this.key = key;
        this.item = item;
        this.side = side;
    }
}

class ConfigDefaultModelsTemplate {
    [JsonProperty("all")] public Dictionary<string, string>? allModels;
    [JsonProperty("t")] public Dictionary<string, string>? tModels;
    [JsonProperty("ct")] public Dictionary<string, string>? ctModels;
    
}
class WhoCanChangeModelTemplate {
    [JsonProperty("all")] public List<string>? all;
    [JsonProperty("t")] public List<string>? t;
    [JsonProperty("ct")] public List<string>? ct;
}
class AllowedModelChanger {
    public List<EntryKey> tAllowed = new List<EntryKey>();
    public List<EntryKey> ctAllowed = new List<EntryKey>();
}
class ConfigTemplate {
    [JsonProperty("DefaultModels")] public ConfigDefaultModelsTemplate models;
    [JsonProperty("WhoCanChangeModel")] public WhoCanChangeModelTemplate whoCanChangeModel;
}

public class DefaultModelManager {

    private List<DefaultModelEntry> DefaultModels = new List<DefaultModelEntry>();
    private AllowedModelChanger WhoCanChangeModel = new AllowedModelChanger();

    public DefaultModelManager(string ModuleDirectory) {
        var filePath = Path.Join(ModuleDirectory, "../../configs/plugins/PlayerModelChanger/DefaultModels.json");
        if (File.Exists(filePath)) {
            StreamReader reader = File.OpenText(filePath);
            string content = reader.ReadToEnd();
            ConfigTemplate config = JsonConvert.DeserializeObject<ConfigTemplate>(content)!;

            DefaultModels = ParseModelConfig(config.models);
            
            var allAllowed = config.whoCanChangeModel.all?.Select(ParseKey).ToList();
            if (allAllowed != null) {
                WhoCanChangeModel.tAllowed = allAllowed;
                WhoCanChangeModel.ctAllowed = allAllowed;
            }
            var tAllowed = config.whoCanChangeModel.t?.Select(ParseKey).ToList();
            var ctAllowed = config.whoCanChangeModel.ct?.Select(ParseKey).ToList();
            if (tAllowed != null) {
                WhoCanChangeModel.tAllowed = tAllowed;
            }
            if (ctAllowed != null) {
                WhoCanChangeModel.ctAllowed = ctAllowed;
            }
        } else {
            Console.WriteLine("'DefaultModels.json' not found. Disabling default models feature.");
        }
    }

    private static EntryKey ParseKey(string key) {
        EntryKey entryKey;
        if (key == "*") {
            entryKey = new AllKey();
        } else if (key.StartsWith("@")) {
            entryKey = new PermissionFlagKey(key);
        } else if (key.StartsWith("#")) {
            entryKey = new PermissionGroupKey(key);
        } else {
            entryKey = new SteamIDKey(key);
        }
        return entryKey;
    }
    private static List<DefaultModelEntry> ParseModelConfig(ConfigDefaultModelsTemplate config) {
        List<DefaultModelEntry> defaultModels = new List<DefaultModelEntry>();

        if (config.allModels != null) {
            foreach (var model in config.allModels)
            {
                var key = ParseKey(model.Key);
                defaultModels.Add(new DefaultModelEntry(key, model.Value, "ct"));
                defaultModels.Add(new DefaultModelEntry(key, model.Value, "t"));
            }
        }
        if (config.tModels != null) {
            foreach (var model in config.tModels)
            {
                var key = ParseKey(model.Key);
                defaultModels.RemoveAll(entry => entry.key.Equals(key) && entry.side == "t");
                defaultModels.Add(new DefaultModelEntry(key, model.Value, "t"));
            }
        }
        if (config.ctModels != null) {
            foreach (var model in config.ctModels)
            {
                var key = ParseKey(model.Key);
                defaultModels.RemoveAll(entry => entry.key.Equals(key) && entry.side == "ct");
                defaultModels.Add(new DefaultModelEntry(key, model.Value, "ct"));
            }
        }
        return defaultModels;
    }

    // stage 0 : search steam id
    // stage 1 : search permission flag
    // stage 2 : search permission group
    // stage 3 : search all

    private DefaultModelEntry? GetPlayerDefaultModelWithPriority(List<DefaultModelEntry> entries) {
        var result = entries.Find(entry => entry.key is SteamIDKey);
        if (result != null) return result;
        result = entries.Find(entry => entry.key is PermissionFlagKey);
        if (result != null) return result;
        result = entries.Find(entry => entry.key is PermissionGroupKey);
        if (result != null) return result;
        result = entries.Find(entry => entry.key is AllKey);
        return result;
    }
    public string? GetPlayerDefaultModel(CCSPlayerController player, string side) {;
        var filter1 = DefaultModels.Where(entry => entry.side == side && entry.key.Fits(player)).ToList();
        if (filter1 == null) {
            return null;
        }
        var result = GetPlayerDefaultModelWithPriority(filter1);
        if (result == null) {
            return null;
        }
        if (result.item == "") {
            return null;
        }
        return result.item;
    }
    public bool CanPlayerChangeModel(CCSPlayerController player, string side) {
        
        if (side == "all") {
            var flag1 = false;
            var flag2 = false;
            foreach (var key in WhoCanChangeModel.tAllowed)
            {
                if (key.Fits(player)) {
                    flag1 = true;
                    break;
                }
            }
            foreach (var key in WhoCanChangeModel.ctAllowed)
            {
                if (key.Fits(player)) {
                    flag2 = true;
                    break;
                }
            }
            return flag1 && flag2;
        }
        var list = side == "t" ? WhoCanChangeModel.tAllowed : WhoCanChangeModel.ctAllowed;
        foreach (var key in list)
        {
            if (key.Fits(player)) {
                return true;
            }
        }
        return false;
    }
}