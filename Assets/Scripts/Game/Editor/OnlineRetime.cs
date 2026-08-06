using System.Text;
using UnityEditor;
using UnityEngine;
using Farm.Farming;

namespace Farm.Game.EditorTools
{
    /// <summary>
    /// Разовый пересчёт таймингов роста под онлайн: реальные минуты и часы вместо секунд
    /// одиночной линии (таблица — <see cref="TierEconomy.OnlineGrowSeconds"/>).
    /// <para>
    /// Правило проекта: числа живут в ассетах, формулы — справочник. Поэтому здесь именно
    /// инструмент, который переписывает ассеты, а не рантайм-подмена: дизайнер сохраняет
    /// право поправить любую культуру руками после пересчёта.
    /// </para>
    /// <para>
    /// Стадии масштабируются пропорционально своей доле в старой длительности — форма кривой
    /// роста (когда семечко становится ростком) сохраняется, меняется только масштаб.
    /// </para>
    /// </summary>
    public static class OnlineRetime
    {
        [MenuItem("Farm/Онлайн/Пересчитать тайминги роста")]
        public static void RetimeGrowables()
        {
            var guids = AssetDatabase.FindAssets("t:GrowableDefinition");
            var report = new StringBuilder("[Онлайн] Пересчёт таймингов:\n");
            int changed = 0;

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var def = AssetDatabase.LoadAssetAtPath<GrowableDefinition>(path);
                if (def == null) continue;

                double oldTotal = def.TotalGrowTime;
                if (oldTotal <= 0.0)
                {
                    report.Append("  — ").Append(def.Id).Append(": нет стадий, пропущен\n");
                    continue;
                }

                int tier = def.YieldResource != null ? def.YieldResource.Tier : 1;
                float newTotal = TierEconomy.OnlineGrowSeconds(tier);
                float scale = (float)(newTotal / oldTotal);

                var so = new SerializedObject(def);

                // Дефолтная длительность стадии масштабируется тем же множителем — стадии
                // с собственным нулём берут её, и пропорции цепочки сохраняются точно.
                var defaultDuration = so.FindProperty("_defaultStageDuration");
                defaultDuration.floatValue *= scale;

                var stages = so.FindProperty("_stages");
                for (int i = 0; i < stages.arraySize; i++)
                {
                    var duration = stages.GetArrayElementAtIndex(i).FindPropertyRelative("_duration");
                    if (duration.floatValue > 0f) duration.floatValue *= scale;
                }

                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(def);
                changed++;

                report.Append("  — ").Append(def.Id)
                      .Append(" (ступень ").Append(tier).Append("): ")
                      .Append(Describe(oldTotal)).Append(" → ").Append(Describe(newTotal)).Append('\n');
            }

            AssetDatabase.SaveAssets();
            report.Append("Готово: ").Append(changed).Append(" из ").Append(guids.Length).Append(" ассетов.");
            Debug.Log(report.ToString());
        }

        private static string Describe(double seconds)
        {
            if (seconds >= 3600.0) return (seconds / 3600.0).ToString("0.#") + " ч";
            if (seconds >= 60.0) return (seconds / 60.0).ToString("0.#") + " мин";
            return seconds.ToString("0") + " с";
        }
    }
}
