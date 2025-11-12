using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Duckov.Buffs;
using Duckov.Economy;
using Duckov.ItemUsage;
using Duckov.Modding;
using Duckov.Utilities;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using Newtonsoft.Json;
using SodaCraft.Localizations;
using UnityEngine;

namespace codenameBakery
{
    public class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private string _logFilePath;
        private string _dllDirectory;
        private string _configsPath;
        private string _iconsPath;
        private List<ItemConfig> _loadedItemConfigs = new List<ItemConfig>();
            private static readonly List<string> addedFormulaIds = new List<string>();
            private static readonly List<string> addedDecomposeIds = new List<string>();

        protected override void OnAfterSetup()
        {
            _dllDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            _configsPath = Path.Combine(_dllDirectory, "configs");
            _iconsPath = Path.Combine(_dllDirectory, "icons");
            InitializeLogFile();
            LogToFile("Mod started, beginning initialization.");

            LocalizationManager.OnSetLanguage += HandleLanguageChange;

            try
            {
                Directory.CreateDirectory(_configsPath);
                Directory.CreateDirectory(_iconsPath);
                if (!HasConfigFiles())
                {
                    LogToFile("Config files not found, generating default.");
                    GenerateDefaultConfigs();
                }
                LoadAndCreateItems();
                
                HandleLanguageChange(LocalizationManager.CurrentLanguage);
            }
            catch (Exception ex)
            {
                LogToFile($"Initialization failed: {ex.Message}\nStack Trace: {ex.StackTrace}", true);
            }
        }
        
        protected override void OnBeforeDeactivate()
        {
            LogToFile("Mod is deactivating.");
            LocalizationManager.OnSetLanguage -= HandleLanguageChange;
            RemoveAllAddedFormulas();
            RemoveAllAddedDecomposeFormulas();
        }

        #region Core Item Creation & Management

        private void LoadAndCreateItems()
        {
            string[] files = Directory.GetFiles(_configsPath, "*.json");
            _loadedItemConfigs.Clear();
            
            if (files.Length == 0)
            {
                LogToFile("Config directory is empty, cannot create items.", true);
                return;
            }

            foreach (string filePath in files)
            {
                try
                {
                    ItemConfig itemConfig = JsonConvert.DeserializeObject<ItemConfig>(File.ReadAllText(filePath));
                    if (itemConfig?.DisplayNames == null || itemConfig.DisplayNames.Count == 0 || itemConfig.NewItemId <= 0)
                    {
                        LogToFile($"Invalid config: {Path.GetFileName(filePath)}, skipping.", true);
                        continue;
                    }

                    _loadedItemConfigs.Add(itemConfig);
                    CreateCustomItem(itemConfig);
                }
                catch (Exception ex)
                {
                    LogToFile($"Failed to process config {Path.GetFileName(filePath)}: {ex.Message}", true);
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
                    LogToFile($"Original item ID {config.OriginalItemId} does not exist, skipping '{config.LocalizationKey}'", true);
                    return;
                }

                GameObject gameObject = Instantiate(prefab.gameObject);
                gameObject.name = $"CustomItem_{config.NewItemId}";
                DontDestroyOnLoad(gameObject);
                Item component = gameObject.GetComponent<Item>();

                if (component == null)
                {
                    LogToFile($"Failed to clone, Item component not found on '{config.LocalizationKey}'", true);
                    Destroy(gameObject);
                    return;
                }
        
                LogToFile($"--- Starting creation for '{config.LocalizationKey}' ---");
        
                // GỌI CÁC HÀM SỬA ĐỔI TRƯỚC (chúng sẽ hoạt động trên một item "hợp lệ")
                SetBaseItemProperties(component, config);
                SetItemIcon(component, prefab, config);
                SetWorldModelTexture(component, config);
                ApplyAdvancedProperties(component, config);
        
                // THAY ĐỔI DANH TÍNH VÀO GIÂY PHÚT CUỐI CÙNG
                LogToFile($"Setting final typeID to {config.NewItemId} just before registration.", false);
                SetPrivateFieldValue(component, "typeID", config.NewItemId);

                // ĐĂNG KÝ VẬT PHẨM VỚI DANH TÍNH MỚI
                RegisterItem(component, config.NewItemId, gameObject);

                // Các hàm sau đăng ký
                AddCraftingFormulas(config);
                AddDecomposeFormulas(config);
            }
            catch (Exception ex)
            {
                LogToFile($"Failed to create item '{config.LocalizationKey}': {ex.Message} \n {ex.StackTrace}", true);
            }
        }

        #endregion

        #region Property Application
        
        private void SetBaseItemProperties(Item item, ItemConfig config)
        {
            // KHÔNG SET typeID ở đây nữa
            SetPrivateFieldValue(item, "weight", config.Weight);
            SetPrivateFieldValue(item, "value", config.Value);
            SetPrivateFieldValue(item, "displayName", config.LocalizationKey);
            SetPrivateFieldValue(item, "quality", config.Quality);
            SetPrivateFieldValue(item, "order", 0);
    
            if (config.Tags != null)
            {
                foreach (string tagName in config.Tags)
                {
                    bool hasTag = item.Tags.Any(t => t != null && t.name.Equals(tagName, StringComparison.OrdinalIgnoreCase));
                    if (!hasTag)
                    {
                        Tag targetTag = GetTargetTag(tagName);
                        if (targetTag != null)
                        {
                            item.Tags.Add(targetTag);
                        }
                    }
                }
            }
        }

        private void ApplyAdvancedProperties(Item item, ItemConfig config)
        {
            if (config.EnergyValue > 0 || config.WaterValue > 0)
                ApplyFoodAndDrinkProperties(item, config);
                
            if (config.MaxDurability > 0)
                ApplyDurabilityProperties(item, config);
            
            if (config.HealValue > 0)
                ApplyHealingProperties(item, config);
            
            if (config.UseTime > 0)
                ApplyUseTime(item, config);

            if (config.AdditionalSlotCount > 0 && config.AdditionalSlotTags != null && config.AdditionalSlotTags.Length > 0)
                AddSlotsToItem(item, config.AdditionalSlotTags, config.AdditionalSlotCount, config.ReplaceExistingSlots, config.AdditionalSlotNames);

            if (config.Modifiers != null && config.Modifiers.Count > 0)
                ApplyModifierEffects(item, config);

            if (config.WeaponProperties != null)
                ApplyWeaponProperties(item, config.WeaponProperties);
                
            // SỬA Ở ĐÂY: Thêm ".AmmoProperties"
            if (config.AmmoProperties != null)
                ApplyAmmoProperties(item, config.AmmoProperties);
                
            // SỬA Ở ĐÂY: Thêm ".MeleeWeaponProperties"
            if (config.MeleeWeaponProperties != null)
                ApplyMeleeWeaponProperties(item, config.MeleeWeaponProperties);

            if (config.BuffDuration != null || (config.BuffCopyConfigs != null && config.BuffCopyConfigs.Length > 0))
                ApplyBuffProperties(item, config);
            
            if (config.Gacha != null)
                SetGacha(item, config.Gacha);
        }
        
        private void ApplyFoodAndDrinkProperties(Item item, ItemConfig config)
        {
            FoodDrink foodDrink = item.GetComponent<FoodDrink>() ?? item.gameObject.AddComponent<FoodDrink>();
            foodDrink.energyValue = config.EnergyValue;
            foodDrink.waterValue = config.WaterValue;
            foodDrink.UseDurability = config.UseDurability;
            foodDrink.energyKey = "Usage_Energy";
            foodDrink.waterKey = "Usage_Water";
            LogToFile($"Item '{config.LocalizationKey}' stats - Energy: {config.EnergyValue}, Water: {config.WaterValue}", false);
        }
        
        private void ApplyDurabilityProperties(Item item, ItemConfig config)
        {
            item.MaxDurability = config.MaxDurability;
            if (config.DurabilityLoss > 0f) item.DurabilityLoss = config.DurabilityLoss;
            item.Durability = item.MaxDurability * (1f - item.DurabilityLoss);
            
            if (config.Repairable && !item.Tags.Any(t => t.name.Equals("Repairable", StringComparison.OrdinalIgnoreCase)))
            {
                Tag repairableTag = GetTargetTag("Repairable");
                if(repairableTag != null) item.Tags.Add(repairableTag);
            }
        }
        
        private void ApplyHealingProperties(Item item, ItemConfig config)
        {
            Drug drug = item.GetComponent<Drug>() ?? item.gameObject.AddComponent<Drug>();
            drug.healValue = config.HealValue;
            drug.useDurability = config.UseDurabilityDrug;
            drug.durabilityUsage = config.DurabilityUsageDrug;
            drug.canUsePart = config.CanUsePartDrug;
            drug.healValueDescriptionKey = "Usage_Health";
            drug.durabilityUsageDescriptionKey = "Usage_Durability";
            LogToFile($"Applied healing properties to '{config.LocalizationKey}': {config.HealValue} HP", false);
        }
        
        private void ApplyUseTime(Item item, ItemConfig config)
        {
            UsageUtilities usageUtils = item.GetComponent<UsageUtilities>() ?? item.gameObject.AddComponent<UsageUtilities>();
            SetPrivateFieldValue(usageUtils, "useTime", config.UseTime);
        }
        
        private void ApplyModifierEffects(Item item, ItemConfig config)
        {
            ModifierDescriptionCollection modifiers = item.GetComponentInChildren<ModifierDescriptionCollection>(true);
            if (modifiers == null) return;
            if (config.Modifiers == null || config.Modifiers.Count == 0) return;

            LogToFile($"Applying {config.Modifiers.Count} modifiers to '{config.LocalizationKey}'.", false);

            var existingModifiers = modifiers.ToDictionary(mod => mod.Key, mod => mod);

            foreach (var modEntry in config.Modifiers)
            {
                string key = modEntry.Key;
                float value = modEntry.Value;

                if (existingModifiers.TryGetValue(key, out ModifierDescription existingMod))
                {
                    SetPrivateFieldValue(existingMod, "value", value);
                }
                else
                {
                    ModifierTarget target = GetModifierTarget(key);
            
                    // SỬA LỖI Ở ĐÂY:
                    // 1. Luôn sử dụng ModifierType.Add (giá trị 0).
                    // 2. Tham số thứ sáu phải là một số nguyên (int), không phải float.
                    var newMod = new ModifierDescription(target, key, 0, value, false, 0); 
                    modifiers.Add(newMod);
                    LogToFile($"Added new modifier '{key}': {value} (Target: {target})", false);
                }
            }
        }
        
        private void ApplyWeaponProperties(Item item, WeaponConfig weaponConfig)
        {
            var gunAgent = item.GetComponent<ItemAgent_Gun>();
            var gunSettings = item.GetComponent<ItemSetting_Gun>();

            if (gunAgent == null || gunSettings == null)
            {
                LogToFile($"Base item for '{item.DisplayNameRaw}' is not a gun. Skipping weapon properties.", true);
                return;
            }

            LogToFile($"Applying weapon properties for '{item.DisplayNameRaw}'.", false);

            // SỬA LỖI Ở ĐÂY: Quay lại sử dụng logic gán số nguyên (int)
            if (!string.IsNullOrEmpty(weaponConfig.Caliber))
                SetPrivateFieldValue(gunSettings, "caliber", weaponConfig.Caliber);

            if (weaponConfig.AutoReload.HasValue)
                SetPrivateFieldValue(gunSettings, "autoReload", weaponConfig.AutoReload.Value);

            if (!string.IsNullOrEmpty(weaponConfig.TriggerMode))
            {
                int triggerModeValue = 0; // single / bolt
                if (weaponConfig.TriggerMode.ToLower() == "burst") triggerModeValue = 1;
                if (weaponConfig.TriggerMode.ToLower() == "auto") triggerModeValue = 2;
                SetPrivateFieldValue(gunSettings, "triggerMode", triggerModeValue);
            }

            if (!string.IsNullOrEmpty(weaponConfig.ReloadMode))
            {
                int reloadModeValue = 0; // magazine / fullMag
                if (weaponConfig.ReloadMode.ToLower() == "perbullet") reloadModeValue = 1;
                SetPrivateFieldValue(gunSettings, "reloadMode", reloadModeValue);
            }
            
            // Phần SetStatValue này đã chính xác và giữ nguyên
            SetStatValue(item, typeof(ItemAgent_Gun), "Damage", weaponConfig.DamageMultiplier, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "Distance", weaponConfig.DistanceMultiplier, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "bulletSpeed", weaponConfig.BulletSpeedMultiplier, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "adsTime", weaponConfig.ADSTimeMultiplier, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "moveSpeedMultiplier", weaponConfig.MoveSpeedMultiplierAdd, false);
            SetStatValue(item, typeof(ItemAgent_Gun), "adsMoveSpeedMultiplier", weaponConfig.ADSMoveSpeedMultiplierAdd, false);
            SetStatValue(item, typeof(ItemAgent_Gun), "Distance", weaponConfig.RangeAddition, false);
            SetStatValue(item, typeof(ItemAgent_Gun), "bulletSpeed", weaponConfig.BulletSpeedAddition, false);
            SetStatValue(item, typeof(ItemAgent_Gun), "CriticalChance", weaponConfig.CriticalChanceMultiplier, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "reloadSpeed", weaponConfig.ReloadSpeedMultiplier, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "Accuracy", weaponConfig.AccuracyMultiplier, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "CriticalDamageFactor", weaponConfig.CriticalDamageFactorMultiplier, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "penetrate", weaponConfig.PenetrateMultiplier, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "armorPiercing", weaponConfig.ArmorPiercingMultiplier, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "armorBreak", weaponConfig.ArmorBreakMultiplier, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "explosionDamage", weaponConfig.ExplosionDamageMultiplier, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "explosionRange", weaponConfig.ExplosionRangeMultiplier, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "shotCount", weaponConfig.ShotCountMultiplier, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "shotAngle", weaponConfig.ShotAngleMultiplier, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "burstCount", weaponConfig.BurstCountMultiplier, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "soundRange", weaponConfig.SoundRangeMultiplier, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "adsAimDistanceFactor", weaponConfig.ADSAimDistanceFactorMultiplier, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "scatterFactor", weaponConfig.ScatterFactorMultiplier, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "scatterFactorADS", weaponConfig.ScatterFactorADSMultiplier, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "defaultScatter", weaponConfig.DefaultScatterMultiplier, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "defaultScatterADS", weaponConfig.DefaultScatterADSMultiplier, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "maxScatter", weaponConfig.MaxScatterMultiplier, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "maxScatterADS", weaponConfig.MaxScatterADSMultiplier, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "scatterGrow", weaponConfig.ScatterGrowMultiplier, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "scatterGrowADS", weaponConfig.ScatterGrowADSMultiplier, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "scatterRecover", weaponConfig.ScatterRecoverMultiplier, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "recoilVMin", weaponConfig.RecoilVMinMultiplier, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "recoilVMax", weaponConfig.RecoilVMaxMultiplier, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "recoilHMin", weaponConfig.RecoilHMinMultiplier, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "recoilHMax", weaponConfig.RecoilHMaxMultiplier, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "recoilScaleV", weaponConfig.RecoilScaleVMultiplier, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "recoilScaleH", weaponConfig.RecoilScaleHMultiplier, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "recoilRecover", weaponConfig.RecoilRecoverMultiplier, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "recoilTime", weaponConfig.RecoilTimeMultiplier, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "recoilRecoverTime", weaponConfig.RecoilRecoverTimeMultiplier, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "capacity", weaponConfig.CapacityMultiplier, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "buffChance", weaponConfig.BuffChanceMultiplier, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "bulletBleedChance", weaponConfig.BulletBleedChanceMultiplier, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "bulletDurabilityCost", weaponConfig.BulletDurabilityCostMultiplier, true);
        }

        private void ApplyAmmoProperties(Item item, AmmoConfig ammoConfig)
        {
            if (ammoConfig == null) return;
    
            var gunSettings = item.GetComponent<ItemSetting_Gun>();
            if (gunSettings == null && !string.IsNullOrEmpty(ammoConfig.Caliber))
            {
                gunSettings = item.gameObject.AddComponent<ItemSetting_Gun>();
                SetPrivateFieldValue(gunSettings, "caliber", ammoConfig.Caliber);
                LogToFile($"Set caliber '{ammoConfig.Caliber}' for ammo '{item.DisplayNameRaw}'", false);
            }

            LogToFile($"Applying ammo properties to '{item.DisplayNameRaw}'.", false);
            
            
            SetStatValue(item, typeof(ItemAgent_Gun), "Damage", ammoConfig.NewDamageMultiplier, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "CriticalChance", ammoConfig.NewCritRateGain, false);
            SetStatValue(item, typeof(ItemAgent_Gun), "CriticalDamageFactor", ammoConfig.NewCritDamageFactorGain, false);
            SetStatValue(item, typeof(ItemAgent_Gun), "armorPiercing", ammoConfig.NewArmorPiercingGain, false);
            SetStatValue(item, typeof(ItemAgent_Gun), "explosionRange", ammoConfig.NewExplosionRange, false, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "buffChance", ammoConfig.NewBuffChanceMultiplier, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "bulletBleedChance", ammoConfig.NewBleedChance, false, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "explosionDamage", ammoConfig.NewExplosionDamage, false, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "armorBreak", ammoConfig.NewArmorBreakGain, false);
            SetStatValue(item, typeof(ItemAgent_Gun), "bulletDurabilityCost", ammoConfig.NewDurabilityCost, false, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "bulletSpeed", ammoConfig.NewBulletSpeed, false, true);
            SetStatValue(item, typeof(ItemAgent_Gun), "Distance", ammoConfig.NewBulletDistance, false, true);
        }

        private void ApplyMeleeWeaponProperties(Item item, MeleeWeaponConfig meleeConfig)
        {
            if (meleeConfig == null) return;
            var meleeAgent = item.GetComponent<ItemAgent_MeleeWeapon>();
            if (meleeAgent == null) return;

            LogToFile($"Applying melee properties to '{item.DisplayNameRaw}'.", false);

            SetStatValue(item, typeof(ItemAgent_MeleeWeapon), "damage", meleeConfig.NewDamage, false, true);
            SetStatValue(item, typeof(ItemAgent_MeleeWeapon), "critRate", meleeConfig.NewCritRate, false, true);
            SetStatValue(item, typeof(ItemAgent_MeleeWeapon), "critDamageFactor", meleeConfig.NewCritDamageFactor, false, true);
            SetStatValue(item, typeof(ItemAgent_MeleeWeapon), "armorPiercing", meleeConfig.NewArmorPiercing, false, true);
            SetStatValue(item, typeof(ItemAgent_MeleeWeapon), "attackSpeed", meleeConfig.NewAttackSpeed, false, true);
            SetStatValue(item, typeof(ItemAgent_MeleeWeapon), "attackRange", meleeConfig.NewAttackRange, false, true);
            SetStatValue(item, typeof(ItemAgent_MeleeWeapon), "staminaCost", meleeConfig.NewStaminaCost, false, true);
            SetStatValue(item, typeof(ItemAgent_MeleeWeapon), "bleedChance", meleeConfig.NewBleedChance, false, true);
            SetStatValue(item, typeof(ItemAgent_MeleeWeapon), "moveSpeedMultiplier", meleeConfig.NewMoveSpeedMultiplier, true, true);
        }

        private void ApplyBuffProperties(Item item, ItemConfig config)
        {
            if (config.BuffDuration != null && config.BuffDuration.Duration > 0)
            {
                BuffUtils.ReplaceOrModifyBuff(item, config.BuffDuration.Duration, config.BuffDuration.ReplaceOriginalBuff, config.BuffDuration.ReplacementBuffId);
            }
            if (config.BuffCopyConfigs != null && config.BuffCopyConfigs.Length > 0)
            {
                BuffUtils.CopyAndAddBuffs(item, config.BuffCopyConfigs);
            }
        }

        private void SetGacha(Item item, GachaConfig gachaConfig)
        {
            if (!ValidateGachaConfig(gachaConfig)) return;
            UseToCreateItem gachaComponent = item.GetComponent<UseToCreateItem>() ?? item.gameObject.AddComponent<UseToCreateItem>();
            SetPrivateFieldValue(gachaComponent, "notificationKey", gachaConfig.NotificationKey);
            SetPrivateFieldValue(gachaComponent, "description", gachaConfig.Description);

            var randomPoolField = typeof(UseToCreateItem).GetField("randomPool", BindingFlags.Instance | BindingFlags.NonPublic);
            if (randomPoolField == null) return;
            
            var randomPoolInstance = randomPoolField.GetValue(gachaComponent);
            var clearMethod = randomPoolInstance.GetType().GetMethod("Clear");
            var addMethod = randomPoolInstance.GetType().GetMethod("Add");
            var entryType = typeof(UseToCreateItem).GetNestedType("Entry", BindingFlags.NonPublic);
            if (clearMethod == null || addMethod == null || entryType == null) return;

            clearMethod.Invoke(randomPoolInstance, null);

            foreach (var entryConfig in gachaConfig.Entries)
            {
                object entryInstance = Activator.CreateInstance(entryType);
                entryType.GetField("item", BindingFlags.Instance | BindingFlags.Public)?.SetValue(entryInstance, entryConfig.ItemId);
                addMethod.Invoke(randomPoolInstance, new object[] { entryInstance, entryConfig.Weight });
            }
        }

        private void AddCraftingFormulas(ItemConfig config)
        {
            if (config.AdditionalRecipes == null || config.AdditionalRecipes.Count == 0) return;

            foreach (var recipe in config.AdditionalRecipes)
            {
                if (!string.IsNullOrEmpty(recipe.FormulaId) && recipe.CostItems != null && recipe.CostItems.Length > 0)
                {
                    var costItems = recipe.CostItems.Select(ci => new ValueTuple<int, long>(ci.ItemId, ci.Amount)).ToArray();
                    // SỬA Ở ĐÂY: Bỏ "CraftingUtils."
                    AddCraftingFormula(recipe.FormulaId, recipe.CraftingMoney, costItems, config.NewItemId, recipe.ResultItemAmount, recipe.CraftingTags, recipe.RequirePerk, recipe.UnlockByDefault, recipe.HideInIndex, recipe.LockInDemo);
                }
            }
        }

        private void AddDecomposeFormulas(ItemConfig config)
        {
            if (config.EnableDecompose && config.DecomposeResults != null && config.DecomposeResults.Length > 0)
            {
                string formulaId = !string.IsNullOrEmpty(config.DecomposeFormulaId) 
                    ? config.DecomposeFormulaId 
                    : $"deconstruct_{config.NewItemId}";
                
                AddDecomposeFormula(formulaId, config.NewItemId, config.DecomposeMoney, config.DecomposeResults, config.DecomposeTime);
            }
        }

        #endregion

        #region Localization and Logging (English)

        private void HandleLanguageChange(SystemLanguage newLanguage)
        {
            LogToFile($"Game language changed to {newLanguage}. Updating item localizations.", false);
            string langKey = GetLanguageKey(newLanguage);

            foreach (var config in _loadedItemConfigs)
            {
                if (string.IsNullOrEmpty(config.LocalizationKey)) continue;

                if (config.DisplayNames.TryGetValue(langKey, out string displayName))
                    LocalizationManager.SetOverrideText(config.LocalizationKey, displayName);
                else if (config.DisplayNames.TryGetValue("English", out string fallbackName))
                    LocalizationManager.SetOverrideText(config.LocalizationKey, fallbackName);

                if (config.LocalizationDescValues.TryGetValue(langKey, out string description))
                    LocalizationManager.SetOverrideText(config.LocalizationDesc, description);
                else if (config.LocalizationDescValues.TryGetValue("English", out string fallbackDesc))
                    LocalizationManager.SetOverrideText(config.LocalizationDesc, fallbackDesc);
            }
        }

        private string GetLanguageKey(SystemLanguage language)
        {
            switch (language)
            {
                case SystemLanguage.English: return "English";
                case SystemLanguage.ChineseSimplified: return "ChineseSimplified";
                case SystemLanguage.ChineseTraditional: return "ChineseTraditional";
                case SystemLanguage.Russian: return "Russian";
                case SystemLanguage.Japanese: return "Japanese";
                case SystemLanguage.Korean: return "Korean";
                case SystemLanguage.German: return "German";
                case SystemLanguage.French: return "French";
                case SystemLanguage.Spanish: return "Spanish";
                default: return "English";
            }
        }

        private void LogToFile(string message, bool isError = false)
        {
            try
            {
                string text = $"[{DateTime.Now:HH:mm:ss}] {message}\n";
                File.AppendAllText(_logFilePath, text);
                if (isError) Debug.LogError("[codenameBakery] " + message);
                else Debug.Log("[codenameBakery] " + message);
            }
            catch (Exception ex)
            {
                Debug.LogError("Failed to write to log: " + ex.Message);
            }
        }

        #endregion

        #region Utility Methods (Reflection, Helpers, etc.)
        
        private void DeepDebugComponents(Item item, string stage)
        {
            if (item == null) return;
            try
            {
                LogToFile($"\n--- DEEP DEBUG START: {item.name} at stage [{stage}] ---\n", false);
                var allComponents = item.GetComponents<Component>();
        
                foreach (var component in allComponents)
                {
                    if (component == null) continue;
            
                    Type componentType = component.GetType();
                    LogToFile($"[COMPONENT] ==> {componentType.FullName}", false);

                    // Ghi lại tất cả các field
                    FieldInfo[] fields = componentType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    foreach (var field in fields)
                    {
                        try
                        {
                            object value = field.GetValue(component);
                            LogToFile($"    (Field) {field.Name}: {(value != null ? value.ToString() : "null")}", false);
                        }
                        catch { /* Bỏ qua các field không thể đọc */ }
                    }

                    // Ghi lại tất cả các property
                    PropertyInfo[] properties = componentType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    foreach (var prop in properties)
                    {
                        if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue; // Bỏ qua các property không thể đọc hoặc là indexer
                        try
                        {
                            object value = prop.GetValue(component, null);
                            LogToFile($"    (Property) {prop.Name}: {(value != null ? value.ToString() : "null")}", false);
                        }
                        catch { /* Bỏ qua các property không thể đọc */ }
                    }
                }
                LogToFile($"\n--- DEEP DEBUG END: {item.name} at stage [{stage}] ---\n", false);
            }
            catch (Exception ex)
            {
                LogToFile($"Error in DeepDebugComponents: {ex.Message}", true);
            }
        }
        
        private void SetStatValue(Item item, Type agentType, string statName, float value, bool isMultiplier, bool overwrite = false)
        {
            if (isMultiplier && value == 1f) return;
            if (!isMultiplier && value == 0f && !overwrite) return;

            int statId = GetStatId(agentType, statName);
            if (statId != -1)
            {
                Stat stat = item.GetStat(statId);
                if (stat != null)
                {
                    if (overwrite)
                    {
                        stat.BaseValue = value;
                    }
                    else if (isMultiplier)
                    {
                        stat.BaseValue *= value;
                    }
                    else
                    {
                        stat.BaseValue += value;
                    }
                }
            }
        }
        
        private T GetPrivateFieldValue<T>(object target, string fieldName) where T : class
        {
            if (target == null) return null;
            try
            {
                FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    return field.GetValue(target) as T;
                }
            }
            catch (Exception ex)
            {
                LogToFile($"GetPrivateFieldValue exception ({fieldName}): {ex.Message}", true);
            }
            return null;
        }
        
        private int GetStatId(Type agentType, string statName)
        {
            try
            {
                FieldInfo field = agentType.GetField(statName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null && field.FieldType == typeof(int))
                {
                    return (int)field.GetValue(null);
                }
            }
            catch (Exception ex)
            {
                LogToFile($"Could not get stat ID for '{statName}' in type '{agentType.Name}': {ex.Message}", true);
            }
            return -1;
        }
        private ModifierTarget GetModifierTarget(string key)
        {
            var characterModifiers = new HashSet<string> { "MovementSpeed", "JumpHeight", "MaxStamina" };
            var weaponModifiers = new HashSet<string> { "Recoil", "Ergonomics", "Accuracy" };

            if (characterModifiers.Contains(key)) return ModifierTarget.Character;
            if (weaponModifiers.Contains(key)) return ModifierTarget.Parent;
    
            return ModifierTarget.Self;
        }
        
        private void SetPrivateFieldValue(object target, string fieldName, object value)
        {
            if (target == null) return;
            try
            {
                FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null) field.SetValue(target, value);
            }
            catch (Exception ex)
            {
                LogToFile($"SetPrivateFieldValue exception ({fieldName}): {ex.Message}", true);
            }
        }

        private Tag GetTargetTag(string tagName)
        {
            if (string.IsNullOrEmpty(tagName)) return null;
            return Resources.FindObjectsOfTypeAll<Tag>().FirstOrDefault(t => t.name.Equals(tagName, StringComparison.OrdinalIgnoreCase));
        }
        
        private static bool ValidateGachaConfig(GachaConfig gachaConfig)
        {
            if (gachaConfig?.Entries == null || gachaConfig.Entries.Length == 0) return false;
            return !gachaConfig.Entries.Any(e => e.ItemId <= 0 || e.Weight <= 0);
        }

        // Original methods like HasConfigFiles, GenerateSampleIcon, etc. go here.
        private bool HasConfigFiles() => Directory.GetFiles(_configsPath, "*.json").Length != 0;
        
        private bool HasTag(Item item, string tagName)
        {
            return item.Tags.Any(tag => 
                tag != null && tag.name.Equals(tagName, StringComparison.OrdinalIgnoreCase));
        }
        
        private void GenerateDefaultConfigs()
        {
            var defaultItem = new ItemConfig
            {
                OriginalItemId = 135,
                NewItemId = 95001,
                LocalizationKey = "OceanTear",
                DisplayNames = new Dictionary<string, string> { { "English", "Ocean's Tear" }, { "ChineseSimplified", "海洋之泪" } },
                LocalizationDescValues = new Dictionary<string, string> { { "English", "A new T0 valuable item..." }, { "ChineseSimplified", "三角洲暗区新T0级大红..." } },
                Weight = 0.2f, Value = 50000000, Quality = 9, Tags = new string[] { "Luxury" }, IconFileName = "ocean_tear.png",
                Modifiers = new Dictionary<string, float> { { "MovementSpeed", 0.05f } } // Example of a new feature
            };

            string fileName = $"{defaultItem.NewItemId}_{defaultItem.LocalizationKey}.json";
            string filePath = Path.Combine(_configsPath, fileName);
            string jsonContent = JsonConvert.SerializeObject(defaultItem, Formatting.Indented);
            File.WriteAllText(filePath, jsonContent);
            LogToFile("Generated default config: " + fileName, false);
            
            GenerateSampleIcon("default_gold.png", new Color(0.9f, 0.8f, 0.2f));
        }

        private void InitializeLogFile()
        {
            string logDir = Path.Combine(_dllDirectory, "logs");
            Directory.CreateDirectory(logDir);
            _logFilePath = Path.Combine(logDir, $"codenameBakery_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            LogToFile("Log path: " + _logFilePath);
        }

        private void GenerateSampleIcon(string fileName, Color mainColor)
        {
            string path = Path.Combine(_iconsPath, fileName);
            if (File.Exists(path)) return;
            
            try
            {
                var texture = new Texture2D(64, 64, TextureFormat.ARGB32, false);
                for (int i = 0; i < 64; i++) {
                    for (int j = 0; j < 64; j++) {
                        texture.SetPixel(i, j, (i < 4 || i > 59 || j < 4 || j > 59) ? Color.black : mainColor);
                    }
                }
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
                LogToFile("Generated sample icon: " + fileName);
            }
            catch (Exception ex)
            {
                LogToFile("Failed to generate sample icon " + fileName + ": " + ex.Message, true);
            }
        }

        private void SetItemIcon(Item newItem, Item originalItem, ItemConfig config)
        {
            if (string.IsNullOrEmpty(config.IconFileName)) return;

            string path = Path.Combine(_iconsPath, config.IconFileName);
            if (!File.Exists(path))
            {
                LogToFile($"Icon file not found: {path}, using original icon for {config.LocalizationKey}.", true);
                return;
            }

            try
            {
                byte[] fileData = File.ReadAllBytes(path);
                var texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                if (ImageConversion.LoadImage(texture, fileData))
                {
                    texture.filterMode = FilterMode.Bilinear;
                    texture.Apply();
                    Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
                    
                    var iconHolder = new GameObject($"IconHolder_{config.NewItemId}");
                    DontDestroyOnLoad(iconHolder);
                    iconHolder.AddComponent<ResourceHolder>().SetIcon(texture, sprite);

                    SetPrivateFieldValue(newItem, "icon", sprite);
                }
            }
            catch (Exception ex)
            {
                LogToFile($"Failed to set UI icon {config.IconFileName}: {ex.Message}", true);
            }
        }

        private void SetWorldModelTexture(Item newItem, ItemConfig config)
        {
            // Nếu không có icon file được chỉ định, chúng ta không làm gì cả và giữ nguyên model gốc.
            if (string.IsNullOrEmpty(config.IconFileName)) return;

            string path = Path.Combine(_iconsPath, config.IconFileName);
            if (!File.Exists(path)) return;

            try
            {
                byte[] fileData = File.ReadAllBytes(path);
                var texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                if (ImageConversion.LoadImage(texture, fileData))
                {
                    texture.filterMode = FilterMode.Bilinear;
                    texture.wrapMode = TextureWrapMode.Clamp;
                    texture.Apply();

                    // Duyệt qua tất cả các Renderer trong item và các con của nó.
                    foreach (Renderer renderer in newItem.GetComponentsInChildren<Renderer>(true))
                    {
                        // Thay vì tạo Material mới, chúng ta chỉ thay đổi texture của material hiện có.
                        // renderer.material sẽ tự động tạo một bản sao của material nếu cần,
                        // đảm bảo chúng ta không thay đổi asset gốc.
                        if (renderer.material != null)
                        {
                            renderer.material.mainTexture = texture;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogToFile($"Failed to replace world texture for {config.LocalizationKey}: {ex.Message}", true);
            }
        }

        private void RegisterItem(Item item, int itemId, GameObject clonedObj)
        {
            if (ItemAssetsCollection.AddDynamicEntry(item))
            {
                LogToFile($"Item registered successfully: {item.DisplayNameRaw} (ID: {itemId})");
            }
            else
            {
                LogToFile($"Item registration failed: {item.DisplayNameRaw} (ID: {itemId})", true);
                Destroy(clonedObj);
            }
        }

        #endregion

        #region Nested Data Classes

        public class ItemConfig
        {
            // --- Basic Info ---
            public int OriginalItemId { get; set; }
            public int NewItemId { get; set; }
            public Dictionary<string, string> DisplayNames { get; set; } = new Dictionary<string, string>();
            public string LocalizationKey { get; set; }
            public Dictionary<string, string> LocalizationDescValues { get; set; } = new Dictionary<string, string>();
            public string IconFileName { get; set; }
            public string SoundKey { get; set; }
            [JsonIgnore] public string ModuleRootDir { get; set; } = "";
            [JsonIgnore] public string LocalizationDesc => LocalizationKey + "_Desc";

            // --- Standard Stats ---
            public float Weight { get; set; }
            public int Value { get; set; }
            public int Quality { get; set; }
            public string[] Tags { get; set; }
            public int MaxStackCount { get; set; } = 1;

            // --- Consumable Stats ---
            public float EnergyValue { get; set; }
            public float WaterValue { get; set; }
            public float UseDurability { get; set; }
            public int HealValue { get; set; }
            public bool UseDurabilityDrug { get; set; }
            public float DurabilityUsageDrug { get; set; }
            public bool CanUsePartDrug { get; set; }
            public float UseTime { get; set; }

            // --- Durability & Repair ---
            public float MaxDurability { get; set; }
            public float DurabilityLoss { get; set; }
            public bool Repairable { get; set; }

            // --- Advanced Modifiers ---
            public Dictionary<string, float> Modifiers { get; set; } = new Dictionary<string, float>();

            // --- Crafting & Decompose ---
            public string DecomposeFormulaId { get; set; }
            public float DecomposeTime { get; set; }
            public List<RecipeConfig> AdditionalRecipes { get; set; } = new List<RecipeConfig>();
            public bool EnableDecompose { get; set; } = false;
            public long DecomposeMoney { get; set; }
            public CraftingItemEntry[] DecomposeResults { get; set; }

            // --- Slots ---
            public string[] AdditionalSlotTags { get; set; }
            public int AdditionalSlotCount { get; set; }
            public bool ReplaceExistingSlots { get; set; }
            public string[] AdditionalSlotNames { get; set; }

            // --- Buffs ---
            public BuffDurationConfig BuffDuration { get; set; }
            public BuffCopyConfig[] BuffCopyConfigs { get; set; }

            // --- Gacha ---
            public GachaConfig Gacha { get; set; }

            // --- Equipment Properties ---
            public WeaponConfig WeaponProperties { get; set; }
            public AmmoConfig AmmoProperties { get; set; }
            public MeleeWeaponConfig MeleeWeaponProperties { get; set; }
        }
        
        
        public class RecipeConfig
        {
            public string FormulaId { get; set; }
            public long CraftingMoney { get; set; }
            public CraftingItemEntry[] CostItems { get; set; }
            public int ResultItemAmount { get; set; } = 1;
            public string[] CraftingTags { get; set; }
            public string RequirePerk { get; set; } = "";
            public bool UnlockByDefault { get; set; } = true;
            public bool HideInIndex { get; set; } = false;
            public bool LockInDemo { get; set; } = false;
        }
        

        public class CraftingItemEntry
        {
            public int ItemId { get; set; }
            public long Amount { get; set; }
        }
        
        public class WeaponConfig
        {
            public string Caliber { get; set; }
            public string TriggerMode { get; set; } // "single", "burst", "auto"
            public string ReloadMode { get; set; } // "magazine", "perbullet"
            public bool? AutoReload { get; set; }
            public int? ProjectileSourceItemId { get; set; }
            public int? MuzzleFxSourceItemId { get; set; }

            public float DamageMultiplier { get; set; } = 1f;
            public float DistanceMultiplier { get; set; } = 1f;
            public float BulletSpeedMultiplier { get; set; } = 1f;
            public float ADSTimeMultiplier { get; set; } = 1f;
            public float MoveSpeedMultiplierAdd { get; set; } = 0f;
            public float ADSMoveSpeedMultiplierAdd { get; set; } = 0f;
            public float RangeAddition { get; set; } = 0f;
            public float BulletSpeedAddition { get; set; } = 0f;
            public float CriticalChanceMultiplier { get; set; } = 1f;
            public float ReloadSpeedMultiplier { get; set; } = 1f;
            public float AccuracyMultiplier { get; set; } = 1f;
            public float CriticalDamageFactorMultiplier { get; set; } = 1f;
            public float PenetrateMultiplier { get; set; } = 1f;
            public float ArmorPiercingMultiplier { get; set; } = 1f;
            public float ArmorBreakMultiplier { get; set; } = 1f;
            public float ExplosionDamageMultiplier { get; set; } = 1f;
            public float ExplosionRangeMultiplier { get; set; } = 1f;
            public float ShotCountMultiplier { get; set; } = 1f;
            public float ShotAngleMultiplier { get; set; } = 1f;
            public float BurstCountMultiplier { get; set; } = 1f;
            public float SoundRangeMultiplier { get; set; } = 1f;
            public float ADSAimDistanceFactorMultiplier { get; set; } = 1f;
            public float ScatterFactorMultiplier { get; set; } = 1f;
            public float ScatterFactorADSMultiplier { get; set; } = 1f;
            public float DefaultScatterMultiplier { get; set; } = 1f;
            public float DefaultScatterADSMultiplier { get; set; } = 1f;
            public float MaxScatterMultiplier { get; set; } = 1f;
            public float MaxScatterADSMultiplier { get; set; } = 1f;
            public float ScatterGrowMultiplier { get; set; } = 1f;
            public float ScatterGrowADSMultiplier { get; set; } = 1f;
            public float ScatterRecoverMultiplier { get; set; } = 1f;
            public float RecoilVMinMultiplier { get; set; } = 1f;
            public float RecoilVMaxMultiplier { get; set; } = 1f;
            public float RecoilHMinMultiplier { get; set; } = 1f;
            public float RecoilHMaxMultiplier { get; set; } = 1f;
            public float RecoilScaleVMultiplier { get; set; } = 1f;
            public float RecoilScaleHMultiplier { get; set; } = 1f;
            public float RecoilRecoverMultiplier { get; set; } = 1f;
            public float RecoilTimeMultiplier { get; set; } = 1f;
            public float RecoilRecoverTimeMultiplier { get; set; } = 1f;
            public float CapacityMultiplier { get; set; } = 1f;
            public float BuffChanceMultiplier { get; set; } = 1f;
            public float BulletBleedChanceMultiplier { get; set; } = 1f;
            public float BulletDurabilityCostMultiplier { get; set; } = 1f;
        }

        public class AmmoConfig
        {
            public string Caliber { get; set; }
            public float NewCritRateGain { get; set; }
            public float NewCritDamageFactorGain { get; set; }
            public float NewArmorPiercingGain { get; set; }
            public float NewDamageMultiplier { get; set; } = 1f;
            public float NewExplosionRange { get; set; }
            public float NewBuffChanceMultiplier { get; set; } = 1f;
            public float NewBleedChance { get; set; }
            public float NewExplosionDamage { get; set; }
            public float NewArmorBreakGain { get; set; }
            public float NewDurabilityCost { get; set; }
            public float NewBulletSpeed { get; set; }
            public float NewBulletDistance { get; set; }
        }
        
        public class MeleeWeaponConfig
        {
            public float NewDamage { get; set; }
            public float NewCritRate { get; set; }
            public float NewCritDamageFactor { get; set; }
            public float NewArmorPiercing { get; set; }
            public float NewAttackSpeed { get; set; }
            public float NewAttackRange { get; set; }
            public float NewStaminaCost { get; set; }
            public float NewBleedChance { get; set; }
            public float NewMoveSpeedMultiplier { get; set; } = 1f;
        }
        
        public class BuffDurationConfig
        {
            public float Duration { get; set; } = 30f;
            public bool ReplaceOriginalBuff { get; set; } = false;
            public int ReplacementBuffId { get; set; } = -1;
        }

        public class GachaEntryConfig
        {
            public int ItemId { get; set; }
            public float Weight { get; set; }
        }

        public class GachaConfig
        {
            public string Description { get; set; }
            public string NotificationKey { get; set; } = "Gacha_DefaultNotification";
            public GachaEntryConfig[] Entries { get; set; }
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

        #endregion

        #region Utility Methods (Moved & Corrected)
        
        public void AddSlotsToItem(Item item, string[] slotTags, int slotCount, bool replaceExisting, string[] slotNames)
        {
            try
            {
                if (item.Slots == null) item.CreateSlotsComponent();
            
                if (replaceExisting)
                {
                    var slotsToRemove = item.Slots.ToList();
                    foreach(var slot in slotsToRemove)
                    {
                        item.Slots.Remove(slot);
                    }
                    LogToFile($"Cleared {slotsToRemove.Count} existing slots from '{item.DisplayNameRaw}' as requested.", false);
                }

                for (int i = 0; i < slotCount; i++)
                {
                    string slotName = (slotNames != null && i < slotNames.Length && !string.IsNullOrEmpty(slotNames[i])) 
                        ? slotNames[i] 
                        : $"custom_slot_{item.TypeID}_{item.Slots.Count}";
                
                    Slot slot = new Slot(slotName);
                    string tag = slotTags[i % slotTags.Length];
                    Tag tagInstance = GetOrCreateTag(tag, tag) as Tag;

                    if (tagInstance != null)
                    {
                        slot.requireTags.Add(tagInstance);
                        item.Slots.Add(slot);
                    }
                }
            }
            catch (Exception ex)
            {
                LogToFile($"[Error] AddSlotsToItem: {ex.Message}", true);
            }
        }

        public object GetOrCreateTag(string tagName, string displayName)
        {
            object existingTag = FindExistingTag(tagName);
            if (existingTag != null) return existingTag;

            try
            {
                Type tagType = typeof(Tag);
                var newTag = ScriptableObject.CreateInstance(tagType) as Tag;
                if (newTag != null)
                {
                    newTag.name = tagName;
                    LocalizationManager.SetOverrideText($"Tag_{tagName}", displayName ?? tagName);
                    return newTag;
                }
            }
            catch (Exception ex)
            {
                LogToFile($"[Error] GetOrCreateTag: {ex.Message}", true);
            }
            return null;
        }
            
        public object FindExistingTag(string tagName)
        {
            return Resources.FindObjectsOfTypeAll<Tag>().FirstOrDefault(t => t.name.Equals(tagName, StringComparison.OrdinalIgnoreCase));
        }

        public void AddCraftingFormula(string formulaId, long money, ValueTuple<int, long>[] costItems, int resultItemId, int resultItemAmount, string[] tags, string requirePerk, bool unlockByDefault, bool hideInIndex, bool lockInDemo)
        {
            try
            {
                CraftingFormulaCollection instance = CraftingFormulaCollection.Instance;
                if (instance == null) return;

                var formulasField = typeof(CraftingFormulaCollection).GetField("formulas", BindingFlags.Instance | BindingFlags.NonPublic);
                if (formulasField == null) return;

                var formulas = (List<CraftingFormula>)formulasField.GetValue(instance);
                if (formulas == null) return;

                string finalFormulaId = formulaId;
                int suffix = 0;
                while (formulas.Any(f => f.id == finalFormulaId) || addedFormulaIds.Contains(finalFormulaId))
                {
                    suffix++;
                    finalFormulaId = $"{formulaId}_{suffix}";
                }

                CraftingFormula newFormula = new CraftingFormula
                {
                    id = finalFormulaId,
                    unlockByDefault = unlockByDefault,
                    cost = new Cost
                    {
                        money = money,
                        items = costItems.Select(ci => new Cost.ItemEntry { id = ci.Item1, amount = ci.Item2 }).ToArray()
                    },
                    result = new CraftingFormula.ItemEntry
                    {
                        id = resultItemId,
                        amount = resultItemAmount
                    },
                    requirePerk = requirePerk,
                    tags = tags ?? new[] { "Workbench" },
                    hideInIndex = hideInIndex,
                    lockInDemo = lockInDemo
                };

                formulas.Add(newFormula);
                if (!addedFormulaIds.Contains(finalFormulaId))
                {
                    addedFormulaIds.Add(finalFormulaId);
                }
                
                var cacheField = typeof(CraftingFormulaCollection).GetField("_formulasById", BindingFlags.Instance | BindingFlags.NonPublic);
                cacheField?.SetValue(instance, null);
            }
            catch (Exception ex)
            {
                 LogToFile($"[Error] adding crafting formula '{formulaId}': {ex.Message}", true);
            }
        }
        
        public void RemoveAllAddedFormulas()
        {
            try
            {
                CraftingFormulaCollection instance = CraftingFormulaCollection.Instance;
                if (instance == null || addedFormulaIds.Count == 0) return;

                var formulasField = typeof(CraftingFormulaCollection).GetField("formulas", BindingFlags.Instance | BindingFlags.NonPublic);
                if (formulasField == null) return;

                var formulas = (List<CraftingFormula>)formulasField.GetValue(instance);
                if (formulas == null) return;

                int removedCount = formulas.RemoveAll(f => addedFormulaIds.Contains(f.id));
                
                addedFormulaIds.Clear();

                var cacheField = typeof(CraftingFormulaCollection).GetField("_formulasById", BindingFlags.Instance | BindingFlags.NonPublic);
                cacheField?.SetValue(instance, null);
                
                LogToFile($"Removed {removedCount} custom crafting formulas.", false);
            }
            catch (Exception ex)
            {
                LogToFile($"[Error] removing crafting formulas: {ex.Message}", true);
            }
        }

        public void AddDecomposeFormula(string formulaId, int itemId, long money, CraftingItemEntry[] resultItems, float time)
        {
            try
            {
                DecomposeDatabase instance = DecomposeDatabase.Instance;
                if (instance == null) return;

                var formulasField = typeof(DecomposeDatabase).GetField("formulas", BindingFlags.Instance | BindingFlags.NonPublic);
                if (formulasField == null) return;

                var formulas = new List<DecomposeFormula>((DecomposeFormula[])formulasField.GetValue(instance));
                if (formulas.Any(f => f.item == itemId)) return;

                var newFormula = new DecomposeFormula
                {
                    item = itemId,
                    valid = true,
                    result = new Cost
                    {
                        money = money,
                        items = resultItems.Select(ci => new Cost.ItemEntry { id = ci.ItemId, amount = ci.Amount }).ToArray()
                    }
                };

                formulas.Add(newFormula);
                formulasField.SetValue(instance, formulas.ToArray());
                
                if (!addedDecomposeIds.Contains(itemId.ToString()))
                {
                    addedDecomposeIds.Add(itemId.ToString());
                }
            }
            catch (Exception ex)
            {
                LogToFile($"[Error] adding decompose formula for item ID '{itemId}': {ex.Message}", true);
            }
        }

        public void RemoveAllAddedDecomposeFormulas()
        {
            try
            {
                DecomposeDatabase instance = DecomposeDatabase.Instance;
                if (instance == null || addedDecomposeIds.Count == 0) return;

                 var formulasField = typeof(DecomposeDatabase).GetField("formulas", BindingFlags.Instance | BindingFlags.NonPublic);
                if (formulasField == null) return;

                var formulas = new List<DecomposeFormula>((DecomposeFormula[])formulasField.GetValue(instance));
                int removedCount = formulas.RemoveAll(f => addedDecomposeIds.Contains(f.item.ToString()));
                
                formulasField.SetValue(instance, formulas.ToArray());
                addedDecomposeIds.Clear();
                
                LogToFile($"Removed {removedCount} custom decompose formulas.", false);
            }
            catch (Exception ex)
            {
                LogToFile($"[Error] removing decompose formulas: {ex.Message}", true);
            }
        }
        
        #endregion
    }
}