using System.Collections.Generic;
using UnityEngine;

namespace Farm.Farming
{
    /// <summary>
    /// Телефонная книга контента: по стабильному <c>Id</c> отдаёт ассет. Нужна сохранениям —
    /// в файле лежат строки, а не ссылки, иначе сейв ломался бы от любой пересборки базы ассетов.
    /// <para>
    /// Заполняется <b>сама</b> — сканирует папку контента при каждой перезагрузке домена в
    /// редакторе. Это условие, а не удобство: реестр, который надо пополнять руками, — ровно тот
    /// список, который однажды забудут, и забытый ассет молча пропадёт из сохранения игрока.
    /// Добавление контента по-прежнему стоит один ассет и ноль правок.
    /// </para>
    /// </summary>
    public sealed class ContentRegistry : ScriptableObject
    {
        /// <summary>Где лежит сам ассет реестра, внутри Resources.</summary>
        public const string ResourcePath = "ContentRegistry";

        [SerializeField] private ResourceDefinition[] _resources = new ResourceDefinition[0];
        [SerializeField] private GrowableDefinition[] _growables = new GrowableDefinition[0];
        [SerializeField] private BuildingDefinition[] _buildings = new BuildingDefinition[0];
        [SerializeField] private ImprovementDefinition[] _improvements = new ImprovementDefinition[0];
        [SerializeField] private ShopItemDefinition[] _shopItems = new ShopItemDefinition[0];

        private Dictionary<string, ResourceDefinition> _resourceById;
        private Dictionary<string, GrowableDefinition> _growableById;
        private Dictionary<string, BuildingDefinition> _buildingById;
        private Dictionary<string, ImprovementDefinition> _improvementById;
        private Dictionary<string, ShopItemDefinition> _shopItemById;

        private static ContentRegistry _instance;

        /// <summary>Реестр из Resources. Null, если ассета нет, — тогда сохранения молчат об этом громко.</summary>
        public static ContentRegistry Instance
        {
            get
            {
                if (_instance != null) return _instance;

                _instance = Resources.Load<ContentRegistry>(ResourcePath);
                if (_instance == null)
                    Debug.LogError("[Farm] Нет ассета Resources/" + ResourcePath +
                                   " — сохранения не смогут найти контент по id");

                return _instance;
            }
        }

        public ResourceDefinition Resource(string id) => Find(ref _resourceById, _resources, id);
        public GrowableDefinition Growable(string id) => Find(ref _growableById, _growables, id);
        public BuildingDefinition Building(string id) => Find(ref _buildingById, _buildings, id);
        public ImprovementDefinition Improvement(string id) => Find(ref _improvementById, _improvements, id);
        public ShopItemDefinition ShopItem(string id) => Find(ref _shopItemById, _shopItems, id);

        /// <summary>Сколько чего знает реестр — для проверок и логов.</summary>
        public string Summary =>
            _resources.Length + " ресурсов, " + _growables.Length + " растимых, " +
            _buildings.Length + " построек, " + _improvements.Length + " замыслов, " +
            _shopItems.Length + " товаров";

        /// <summary>
        /// Общий поиск по любому виду. Словарь строится лениво: за партию его спросят
        /// сотни раз при загрузке и ни разу потом.
        /// </summary>
        private static T Find<T>(ref Dictionary<string, T> cache, T[] all, string id) where T : ScriptableObject
        {
            if (string.IsNullOrEmpty(id)) return null;

            if (cache == null)
            {
                cache = new Dictionary<string, T>(all.Length);
                for (int i = 0; i < all.Length; i++)
                {
                    var item = all[i];
                    if (item == null) continue;

                    string key = IdOf(item);
                    if (string.IsNullOrEmpty(key) || cache.ContainsKey(key)) continue;
                    cache[key] = item;
                }
            }

            cache.TryGetValue(id, out T found);
            return found;
        }

        /// <summary>Ассеты не делят общий интерфейс — их <c>Id</c> совпадают только по духу.</summary>
        private static string IdOf(ScriptableObject item)
        {
            switch (item)
            {
                case ResourceDefinition r: return r.Id;
                case GrowableDefinition g: return g.Id;
                case BuildingDefinition b: return b.Id;
                case ImprovementDefinition i: return i.Id;
                case ShopItemDefinition s: return s.Id;
                default: return null;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

#if UNITY_EDITOR
        /// <summary>
        /// Пересобрать из папки контента. Зовётся при каждой перезагрузке домена, поэтому
        /// новый ассет попадает в реестр сам — достаточно, что Unity его импортировала.
        /// </summary>
        [ContextMenu("Пересобрать из ассетов")]
        public void Rebuild()
        {
            _resources = Load<ResourceDefinition>();
            _growables = Load<GrowableDefinition>();
            _buildings = Load<BuildingDefinition>();
            _improvements = Load<ImprovementDefinition>();
            _shopItems = Load<ShopItemDefinition>();

            _resourceById = null;
            _growableById = null;
            _buildingById = null;
            _improvementById = null;
            _shopItemById = null;

            UnityEditor.EditorUtility.SetDirty(this);
        }

        private static T[] Load<T>() where T : ScriptableObject
        {
            var guids = UnityEditor.AssetDatabase.FindAssets("t:" + typeof(T).Name);
            var found = new List<T>(guids.Length);

            foreach (var guid in guids)
            {
                var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(
                    UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) found.Add(asset);
            }

            return found.ToArray();
        }

        [UnityEditor.InitializeOnLoadMethod]
        private static void RebuildOnDomainReload()
        {
            // Отложенно: при перезагрузке домена база ассетов ещё может импортироваться,
            // и поиск вернул бы неполный список.
            UnityEditor.EditorApplication.delayCall += () =>
            {
                var registry = Resources.Load<ContentRegistry>(ResourcePath);
                if (registry == null) return;

                registry.Rebuild();
                UnityEditor.AssetDatabase.SaveAssetIfDirty(registry);
            };
        }
#endif
    }
}
