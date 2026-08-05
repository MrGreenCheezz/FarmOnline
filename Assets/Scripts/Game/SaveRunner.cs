using UnityEngine;

namespace Farm.Game
{
    /// <summary>
    /// Хранитель партии в сцене фермы: раскладывает сохранение на старте и складывает его
    /// обратно — по таймеру, при выходе и при возврате в меню.
    /// <para>
    /// Порядок исполнения поздний намеренно: к моменту его <c>Start</c> склад уже стал стоком,
    /// фермер нашёл свой дом, а стартовые грядки посадились. Только теперь сцену можно честно
    /// разобрать и собрать заново из файла.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    [AddComponentMenu("Farm/Save Runner")]
    public sealed class SaveRunner : MonoBehaviour
    {
        [Tooltip("Как часто сохраняться само, секунд. 0 — только вручную и на выходе.")]
        [SerializeField, Min(0f)] private float _autosaveInterval = 60f;

        [Tooltip("Писать в консоль о каждом сохранении. Полезно, пока систему обкатывают.")]
        [SerializeField] private bool _logSaves = true;

        private static SaveRunner _instance;
        private float _timer;

        /// <summary>Партию уже загрузили — с этого момента её не стыдно сохранять.</summary>
        public bool Ready { get; private set; }

        private void Awake()
        {
            _instance = this;
            _timer = _autosaveInterval;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void Start()
        {
            if (GameFlow.ConsumeLoadRequest())
            {
                var data = FarmSave.Read();
                if (data != null) FarmSave.Apply(data);
                else Debug.LogWarning("[Save] Просили продолжить, но сохранение не прочиталось — играем с нуля");
            }

            Ready = true;
        }

        private void Update()
        {
            if (_autosaveInterval <= 0f || !Ready) return;

            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;

            _timer = _autosaveInterval;
            Save("автосохранение");
        }

        /// <summary>Свернули игру на телефоне — это тот же выход, только без предупреждения.</summary>
        private void OnApplicationPause(bool paused)
        {
            if (paused) Save("сворачивание");
        }

        private void OnApplicationQuit() => Save("выход");

        public void Save(string reason)
        {
            if (!Ready) return;

            var data = FarmSave.Capture();
            if (!FarmSave.Write(data)) return;

            if (_logSaves) Debug.Log("[Save] Сохранено (" + reason + "): " + data.Describe());
        }

        /// <summary>
        /// Сохранить, если в сцене есть кому. Зовётся из <see cref="GameFlow"/>, которому
        /// в момент перехода уже не на что опереться — сцена вот-вот выгрузится.
        /// </summary>
        public static void SaveIfPossible(string reason)
        {
            if (_instance != null) _instance.Save(reason);
        }
    }
}
