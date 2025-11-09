using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using SodaCraft.Localizations;
using UnityEngine;
using ItemStatsSystem;
using Duckov.Modding;
using Duckov.ItemUsage;
using Duckov.Utilities;

namespace codenameBakery
{
    public class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private string _logFilePath;
        private string _dllDirectory;
        private string _configsPath;
        private string _iconsPath;
        private List<ItemConfig> _loadedItemConfigs = new List<ItemConfig>();

        protected override void OnAfterSetup()
        {
            this._dllDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            this._configsPath = Path.Combine(this._dllDirectory, "configs");
            this._iconsPath = Path.Combine(this._dllDirectory, "icons");
            this.InitializeLogFile();
            this.LogToFile("Mod started, beginning initialization.", false);

            LocalizationManager.OnSetLanguage += HandleLanguageChange;

            try
            {
                Directory.CreateDirectory(this._configsPath);
                Directory.CreateDirectory(this._iconsPath);
                if (!this.HasConfigFiles())
                {
                    this.LogToFile("Config files not found, generating default.", false);
                    this.GenerateDefaultConfigs();
                }
                this.LoadAndCreateItems();
                
                HandleLanguageChange(LocalizationManager.CurrentLanguage);
            }
            catch (Exception ex)
            {
                this.LogToFile("Initialization failed: " + ex.Message + "\nStack Trace: " + ex.StackTrace, true);
            }
        }
        
        protected override void OnBeforeDeactivate()
        {
            this.LogToFile("Mod is deactivating.", false);
            LocalizationManager.OnSetLanguage -= HandleLanguageChange;
        }

        private void HandleLanguageChange(SystemLanguage newLanguage)
        {
            this.LogToFile($"Game language changed to {newLanguage}. Updating item localizations.", false);
            string langKey = GetLanguageKey(newLanguage);

            foreach (var config in _loadedItemConfigs)
            {
                if (config.DisplayNames.TryGetValue(langKey, out string displayName))
                {
                    LocalizationManager.SetOverrideText(config.LocalizationKey, displayName);
                }
                else if (config.DisplayNames.TryGetValue("English", out string fallbackName))
                {
                     LocalizationManager.SetOverrideText(config.LocalizationKey, fallbackName);
                }

                if (config.LocalizationDescValues.TryGetValue(langKey, out string description))
                {
                    LocalizationManager.SetOverrideText(config.LocalizationDesc, description);
                }
                else if (config.LocalizationDescValues.TryGetValue("English", out string fallbackDesc))
                {
                    LocalizationManager.SetOverrideText(config.LocalizationDesc, fallbackDesc);
                }
            }
        }

        private string GetLanguageKey(SystemLanguage language)
        {
            switch (language)
            {
                case SystemLanguage.English: return "English";
                case SystemLanguage.ChineseSimplified: return "ChineseSimplified";
                case SystemLanguage.Russian: return "Russian";
                case SystemLanguage.French: return "French";
                case SystemLanguage.German: return "German";
                case SystemLanguage.Japanese: return "Japanese";
                case SystemLanguage.Korean: return "Korean";
                case SystemLanguage.Spanish: return "Spanish";
                case SystemLanguage.ChineseTraditional: return "ChineseTraditional";
                default: return "English";
            }
        }
        
        private bool HasConfigFiles()
        {
            return Directory.GetFiles(this._configsPath, "*.json").Length != 0;
        }

        private void GenerateDefaultConfigs()
        {
            var defaultItems = new List<ItemConfig>
            {
                new ItemConfig
                {
                    OriginalItemId = 135,
                    NewItemId = 95001,
                    LocalizationKey = "OceanTear",
                    DisplayNames = new Dictionary<string, string>
                    {
                        { "English", "Ocean's Tear" },
                        { "ChineseSimplified", "海洋之泪" },
                        { "Russian", "Слеза океана" }
                    },
                    LocalizationDescValues = new Dictionary<string, string>
                    {
                        { "English", "A new T0 grand-red from the Delta Restricted Zone, comparable to the Heart of Africa. Often found in the Tidal Prison, it seems to be a treasured item brought by a veteran, valued at over 20 million." },
                        { "ChineseSimplified", "三角洲暗区新T0级大红，对标非洲之心，多刷新于潮汐监狱貌似是某位退伍老兵带来的珍藏品，价值超2000W" },
                        { "Russian", "Новый ценный предмет T0 из запретной зоны Дельты, сравнимый с Сердцем Африки. Часто встречается в Приливной тюрьме, по-видимому, это ценный предмет, принесенный ветераном, стоимостью более 20 миллионов." }
                    },
                    Weight = 0.2f,
                    Value = 50000000,
                    Quality = 9,
                    Tags = new string[] { "Luxury" },
                    IconFileName = "ocean_tear.png"
                }
            };

            foreach (var itemConfig in defaultItems)
            {
                string fileName = $"{itemConfig.NewItemId}_{itemConfig.LocalizationKey}.json";
                string filePath = Path.Combine(this._configsPath, fileName);
                string jsonContent = JsonConvert.SerializeObject(itemConfig, Formatting.Indented);
                File.WriteAllText(filePath, jsonContent);
                this.LogToFile("Generated default config: " + fileName, false);
            }
            
            this.GenerateSampleIcon("default_gold.png", new Color(0.9f, 0.8f, 0.2f));
            this.GenerateSampleIcon("default_daily.png", new Color(0.6f, 0.8f, 0.6f));
        }

        private void GenerateSampleIcon(string fileName, Color mainColor)
        {
            string text = Path.Combine(this._iconsPath, fileName);
            if (File.Exists(text)) return;
            
            try
            {
                Texture2D texture2D = new Texture2D(64, 64, TextureFormat.ARGB32, false);
                for (int i = 0; i < 64; i++)
                {
                    for (int j = 0; j < 64; j++)
                    {
                        texture2D.SetPixel(i, j, (i < 4 || i > 59 || j < 4 || j > 59) ? Color.black : mainColor);
                    }
                }
                texture2D.Apply();
                byte[] array = ImageConversion.EncodeToPNG(texture2D);
                File.WriteAllBytes(text, array);
                this.LogToFile("Generated sample icon: " + fileName, false);
            }
            catch (Exception ex)
            {
                this.LogToFile("Failed to generate sample icon " + fileName + ": " + ex.Message, true);
            }
        }

        private void LoadAndCreateItems()
        {
            string[] files = Directory.GetFiles(this._configsPath, "*.json");
            _loadedItemConfigs.Clear();
            
            if (files.Length == 0)
            {
                this.LogToFile("Config directory is empty, cannot create items.", true);
                return;
            }
            foreach (string text in files)
            {
                try
                {
                    ItemConfig itemConfig = JsonConvert.DeserializeObject<ItemConfig>(File.ReadAllText(text));
                    if (itemConfig.DisplayNames == null || itemConfig.DisplayNames.Count == 0 || itemConfig.NewItemId <= 0)
                    {
                        this.LogToFile("Invalid config: " + Path.GetFileName(text) + ", skipping.", true);
                    }
                    else
                    {
                        _loadedItemConfigs.Add(itemConfig);
                        this.CreateCustomItem(itemConfig);
                        this.LogToFile($"Loaded item config: {itemConfig.LocalizationKey} (ID: {itemConfig.NewItemId})", false);
                    }
                }
                catch (Exception ex)
                {
                    this.LogToFile($"Failed to process config {Path.GetFileName(text)}: {ex.Message}", true);
                }
            }
        }

        public void CreateCustomItem(ItemConfig config)
        {
            try
            {
                Item prefab = ItemAssetsCollection.GetPrefab(config.OriginalItemId);
                if (prefab == null)
                {
                    this.LogToFile($"Original item ID {config.OriginalItemId} does not exist, skipping {config.LocalizationKey}", true);
                }
                else
                {
                    GameObject gameObject = UnityEngine.Object.Instantiate<GameObject>(prefab.gameObject);
                    gameObject.name = $"CustomItem_{config.NewItemId}";
                    UnityEngine.Object.DontDestroyOnLoad(gameObject);
                    Item component = gameObject.GetComponent<Item>();
                    if (component == null)
                    {
                        this.LogToFile("Failed to clone, Item component not found: " + config.LocalizationKey, true);
                        UnityEngine.Object.Destroy(gameObject);
                    }
                    else
                    {
                        this.SetItemProperties(component, config);
                        this.SetItemIcon(component, prefab, config);
                        this.SetWorldModelTexture(component, config);
                        this.RegisterItem(component, config.NewItemId, gameObject);
                    }
                }
            }
            catch (Exception ex)
            {
                this.LogToFile($"Failed to create item {config.LocalizationKey}: {ex.Message}", true);
            }
        }

        private void SetPrivateField(object target, string fieldName, object value)
        {
            try
            {
                FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null)
                {
                    field.SetValue(target, value);
                }
                else
                {
                    this.LogToFile("SetPrivateField failed: Field not found " + fieldName, true);
                }
            }
            catch (Exception ex)
            {
                this.LogToFile($"SetPrivateField exception ({fieldName}): {ex.Message}", true);
            }
        }

        private void SetItemProperties(Item item, ItemConfig config)
        {
            this.SetPrivateField(item, "typeID", config.NewItemId);
            this.SetPrivateField(item, "weight", config.Weight);
            this.SetPrivateField(item, "value", config.Value);
            this.SetPrivateField(item, "displayName", config.LocalizationKey);
            this.SetPrivateField(item, "quality", config.Quality);
            this.SetPrivateField(item, "order", 0);
            item.Tags.Clear();
            foreach (string text in config.Tags)
            {
                Tag targetTag = this.GetTargetTag(text);
                if (targetTag != null) item.Tags.Add(targetTag);
                else this.LogToFile("Tag " + text + " does not exist, skipped.", true);
            }
            this.SetupFoodDrinkComponent(item, config);
        }

        private void SetupFoodDrinkComponent(Item item, ItemConfig config)
        {
            FoodDrink foodDrink = item.GetComponent<FoodDrink>();
            if (foodDrink == null && (config.EnergyValue > 0 || config.WaterValue > 0))
            {
                foodDrink = item.gameObject.AddComponent<FoodDrink>();
                this.LogToFile("Added FoodDrink component to item " + config.LocalizationKey, false);
            }
            if (foodDrink != null)
            {
                 foodDrink.energyValue = config.EnergyValue;
                 foodDrink.waterValue = config.WaterValue;
                 foodDrink.UseDurability = config.UseDurability;
                 foodDrink.energyKey = "Usage_Energy";
                 foodDrink.waterKey = "Usage_Water";
                 this.LogToFile($"Item {config.LocalizationKey} stats - Energy: {config.EnergyValue}, Water: {config.WaterValue}", false);
            }
        }

        private Tag GetTargetTag(string tagName)
        {
            if (string.IsNullOrEmpty(tagName)) return null;
            return Resources.FindObjectsOfTypeAll<Tag>().FirstOrDefault((Tag t) => t.name.Equals(tagName, StringComparison.OrdinalIgnoreCase));
        }

        private void SetItemIcon(Item newItem, Item originalItem, ItemConfig config)
        {
            if (string.IsNullOrEmpty(config.IconFileName)) return;
            string text = Path.Combine(this._iconsPath, config.IconFileName);
            if (!File.Exists(text))
            {
                this.LogToFile("Icon file not found: " + text + ", using original icon.", true);
                return;
            }
            try
            {
                byte[] array = File.ReadAllBytes(text);
                Texture2D texture2D = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                if (!ImageConversion.LoadImage(texture2D, array))
                {
                    this.LogToFile("Icon decoding failed: " + config.IconFileName, true);
                }
                else
                {
                    texture2D.filterMode = FilterMode.Bilinear;
                    texture2D.Apply();
                    Sprite sprite = Sprite.Create(texture2D, new Rect(0f, 0f, (float)texture2D.width, (float)texture2D.height), new Vector2(0.5f, 0.5f), 100f);
                    GameObject gameObject = new GameObject($"IconHolder_{config.NewItemId}");
                    UnityEngine.Object.DontDestroyOnLoad(gameObject);
                    gameObject.AddComponent<ResourceHolder>().SetIcon(texture2D, sprite);
                    typeof(Item).GetField("icon", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(newItem, sprite);
                    this.LogToFile("Successfully loaded UI icon: " + config.IconFileName, false);
                }
            }
            catch (Exception ex)
            {
                this.LogToFile($"Failed to set UI icon {config.IconFileName}: {ex.Message}", true);
            }
        }

        private void SetWorldModelTexture(Item newItem, ItemConfig config)
        {
            if (string.IsNullOrEmpty(config.IconFileName)) return;
            string text = Path.Combine(this._iconsPath, config.IconFileName);
            if (!File.Exists(text))
            {
                this.LogToFile("World texture file not found: " + text, true);
                return;
            }
            try
            {
                byte[] array = File.ReadAllBytes(text);
                Texture2D texture2D = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                if (!ImageConversion.LoadImage(texture2D, array))
                {
                    this.LogToFile("World texture decoding failed: " + config.IconFileName, true);
                }
                else
                {
                    texture2D.filterMode = FilterMode.Bilinear;
                    texture2D.wrapMode = TextureWrapMode.Clamp;
                    texture2D.Apply();
                    Renderer[] componentsInChildren = newItem.GetComponentsInChildren<Renderer>(true);
                    if (componentsInChildren.Length == 0)
                    {
                        this.LogToFile("Item " + config.LocalizationKey + " has no renderer components.", true);
                    }
                    else
                    {
                        foreach (Renderer renderer in componentsInChildren)
                        {
                            Material material = new Material(renderer.material) { mainTexture = texture2D };
                            renderer.material = material;
                            GameObject gameObject = new GameObject($"WorldTexHolder_{config.NewItemId}_{renderer.name}");
                            UnityEngine.Object.DontDestroyOnLoad(gameObject);
                            gameObject.AddComponent<ResourceHolder>().SetIcon(texture2D, null);
                        }
                        this.LogToFile($"Successfully replaced world texture for item {config.LocalizationKey}: {config.IconFileName}", false);
                    }
                }
            }
            catch (Exception ex)
            {
                this.LogToFile($"Failed to replace world texture {config.IconFileName}: {ex.Message}", true);
            }
        }

        private void RegisterItem(Item item, int itemId, GameObject clonedObj)
        {
            if (ItemAssetsCollection.AddDynamicEntry(item))
            {
                this.LogToFile($"Item registered successfully: {item.DisplayNameRaw} (ID: {itemId})", false);
            }
            else
            {
                this.LogToFile($"Item registration failed: {item.DisplayNameRaw} (ID: {itemId})", true);
                UnityEngine.Object.Destroy(clonedObj);
            }
        }
        
        private void InitializeLogFile()
        {
            string text = Path.Combine(this._dllDirectory, "logs");
            Directory.CreateDirectory(text);
            this._logFilePath = Path.Combine(text, $"bakery_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            this.LogToFile("Log path: " + this._logFilePath, false);
        }

        private void LogToFile(string message, bool isError = false)
        {
            try
            {
                string text = $"[{DateTime.Now:HH:mm:ss}] {message}\n";
                File.AppendAllText(this._logFilePath, text);
                if (isError)
                {
                    Debug.LogError("[Additionalmshookl] " + message);
                }
                else
                {
                    Debug.Log("[Additionalmshookl] " + message);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("Failed to write to log: " + ex.Message);
            }
        }

        public class ItemConfig
        {
            public int OriginalItemId { get; set; }
            public int NewItemId { get; set; }
            public Dictionary<string, string> DisplayNames { get; set; }
            public string LocalizationKey { get; set; }
            public Dictionary<string, string> LocalizationDescValues { get; set; }
            public float Weight { get; set; }
            public int Value { get; set; }
            public int Quality { get; set; }
            public string[] Tags { get; set; }
            public float EnergyValue { get; set; }
            public float WaterValue { get; set; }
            public float UseDurability { get; set; }
            public string IconFileName { get; set; }
            public string LocalizationDesc => this.LocalizationKey + "_Desc";
        }

        private class ResourceHolder : MonoBehaviour
        {
            public Texture2D IconTexture;
            public Sprite IconSprite;
            
            public void SetIcon(Texture2D tex, Sprite spr)
            {
                this.IconTexture = tex;
                this.IconSprite = spr;
            }
        }
    }
}