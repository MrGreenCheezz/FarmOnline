using System;
using UnityEngine;

namespace Farm.Characters
{
    /// <summary>
    /// Связка «определение жителя → агент сцены → койка». Живёт в сцене, а не в ассете,
    /// потому что агент и койка существуют только в сцене.
    /// <para>
    /// Работает в Awake, и это не мелочь: имя агента — идентичность сейва, и смениться
    /// оно обязано до того, как SaveRunner (order 100) начнёт раскладывать жителей по
    /// именам, а пересев черт — до того, как данные сейва станут авторитетными.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Farm/Colony Roster")]
    public sealed class ColonyRoster : MonoBehaviour
    {
        [Serializable]
        private sealed class Entry
        {
            [Tooltip("Кто живёт.")]
            public ResidentDefinition Definition;

            [Tooltip("Каким агентом сцены он живёт.")]
            public FarmerAgent Agent;

            [Tooltip("Где спит. Пусто — как настроено на самом агенте.")]
            public Transform Bed;

            [Tooltip("Куда носит урожай и при чём вертится. Пусто — как настроено на агенте.")]
            public Transform Home;
        }

        [SerializeField] private Entry[] _entries = Array.Empty<Entry>();

        private void Awake()
        {
            FarmerAgent first = null;

            foreach (var entry in _entries)
            {
                if (entry == null || entry.Definition == null || entry.Agent == null)
                {
                    // Полупустая строка — ошибка расстановки, и молчать о ней нельзя.
                    Debug.LogWarning("[Roster] Пустая строка ростера — житель пропущен", this);
                    continue;
                }

                entry.Agent.ApplyDefinition(entry.Definition, entry.Bed, entry.Home);
                if (first == null) first = entry.Agent;
            }

            // Первая строка ростера — житель №1: за ним следит HUD, в него ложатся сейвы
            // эпохи одного фермера. Порядку регистрации это доверять нельзя — он от
            // порядка OnEnable, которого Unity не обещает.
            if (first != null) FarmerRegistry.Designate(first);
        }
    }
}
