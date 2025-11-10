// --- START OF BuffUtils.cs ---
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Duckov.Buffs;
using Duckov.Economy;
using Duckov.ItemUsage;
using Duckov.Modding;
using Duckov.Utilities;
using Duckov;
using ItemStatsSystem;
using UnityEngine;


namespace codenameBakery
{
    public static class BuffUtils
    {
        public static void ReplaceOrModifyBuff(Item item, float duration, bool replaceOriginal, int replacementBuffId = -1)
        {
            try
            {
                if (item == null || duration <= 0f) return;

                AddBuff[] addBuffComponents = item.GetComponents<AddBuff>();
                if (addBuffComponents == null || addBuffComponents.Length == 0) return;

                foreach (AddBuff addBuff in addBuffComponents)
                {
                    if (addBuff.buffPrefab == null) continue;
                    
                    Buff targetBuff = addBuff.buffPrefab;
                    if (replaceOriginal && replacementBuffId > 0)
                    {
                        Buff newBuff = FindBuffById(replacementBuffId);
                        if (newBuff != null)
                        {
                            addBuff.buffPrefab = newBuff;
                            targetBuff = newBuff;
                        }
                    }
                    SetBuffDuration(targetBuff, duration);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[codenameBakery] Error in ReplaceOrModifyBuff: " + ex.Message);
            }
        }
        
        public static void CopyAndAddBuffs(Item item, BuffCopyConfig[] copyConfigs)
        {
            if (item == null || copyConfigs == null || copyConfigs.Length == 0) return;
            try
            {
                foreach (var config in copyConfigs)
                {
                    if (config != null && !string.IsNullOrEmpty(config.originalBuffId) && !string.IsNullOrEmpty(config.newBuffId))
                    {
                        CopyAndAddBuff(item, config.originalBuffId, config.newBuffId, config.newDuration);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[codenameBakery] Error in CopyAndAddBuffs: " + ex.Message);
            }
        }

        public static void CopyAndAddBuff(Item item, string originalBuffId, string newBuffId, float newDuration = -1f)
        {
            try
            {
                AddBuff originalAddBuff = item.GetComponents<AddBuff>()
                    .FirstOrDefault(c => c.buffPrefab != null && c.buffPrefab.ID.ToString() == originalBuffId);

                if (originalAddBuff == null) return;
                
                if (!int.TryParse(newBuffId, out int newBuffIdInt)) return;
                
                Buff newBuffPrefab = FindBuffById(newBuffIdInt);
                if (newBuffPrefab == null) return;

                AddBuff newComponent = item.gameObject.AddComponent<AddBuff>();
                newComponent.buffPrefab = newBuffPrefab;
                newComponent.chance = originalAddBuff.chance;
                
                float duration = newDuration > 0f ? newDuration : GetBuffDuration(originalAddBuff.buffPrefab);
                SetBuffDuration(newComponent.buffPrefab, duration);
            }
            catch (Exception ex)
            {
                Debug.LogError("[codenameBakery] Error in CopyAndAddBuff: " + ex.Message);
            }
        }
        
        private static Buff FindBuffById(int buffId)
        {
            try
            {
                GameplayDataSettings.BuffsData buffsData = GameplayDataSettings.Buffs;
                if (buffsData == null) return null;
                
                var buffsListField = typeof(GameplayDataSettings.BuffsData).GetField("m_Buffs", BindingFlags.Instance | BindingFlags.NonPublic);
                if (buffsListField == null) return null;

                var buffsList = buffsListField.GetValue(buffsData) as List<Buff>;
                return buffsList?.Find(b => b != null && b.ID == buffId);
            }
            catch (Exception ex)
            {
                Debug.LogError("[codenameBakery] Exception while trying to find buff by ID: " + ex.Message);
                return null;
            }
        }

        private static void SetBuffDuration(Buff buff, float duration)
        {
            if (buff == null) return;
            try
            {
                var durationField = typeof(Buff).GetField("_duration", BindingFlags.Instance | BindingFlags.NonPublic);
                if (durationField != null)
                {
                    durationField.SetValue(buff, duration);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[codenameBakery] Error in SetBuffDuration: " + ex.Message);
            }
        }
        
        // FIX: Thêm hàm helper để đọc giá trị duration private
        private static float GetBuffDuration(Buff buff)
        {
            if (buff == null) return 0f;
            try
            {
                var durationField = typeof(Buff).GetField("_duration", BindingFlags.Instance | BindingFlags.NonPublic);
                if (durationField != null)
                {
                    return (float)durationField.GetValue(buff);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[codenameBakery] Error in GetBuffDuration: " + ex.Message);
            }
            return 0f;
        }
    }
}
