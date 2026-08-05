using System.Collections.Generic;
using UnityEngine;
using Farm.Farming;

namespace Farm.Juice
{
    /// <summary>
    /// Дневная мелкая живность: бабочки, которые бестолково носятся над фермой и иногда
    /// присаживаются отдохнуть.
    /// <para>
    /// Ночь уже ожила светлячками, а день оставался пустым: всё движение в кадре принадлежало
    /// фермеру и ветру, то есть системам. Живность не принадлежит никому — она просто есть,
    /// и именно это отличает мир от механизма. Тронуть её нельзя, пользы от неё нет, и это
    /// не недоделка: как только у неё появится польза, игрок начнёт за ней охотиться, и она
    /// перестанет быть фоном.
    /// </para>
    /// <para>
    /// Меш строится кодом — двух плоскостей-крыльев достаточно, чтобы силуэт читался с первого
    /// кадра. Готовой модели в паках нет вовсе, а «питомцы» Kenney размером с кошку: бабочка
    /// такого размера читалась бы как зверь, которого забыли выключить.
    /// </para>
    /// <para>
    /// Чистое оформление: ни коллайдеров (иначе перехватит курсор у грядок — на этом уже
    /// горели), ни событий, ни ссылок из ядра. Удали компонент — день просто опустеет.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Farm/Ambient Critters")]
    public sealed class AmbientCritters : MonoBehaviour
    {
        private sealed class Critter
        {
            public Transform Root;
            public Transform LeftWing;
            public Transform RightWing;
            public Vector2 Anchor;      // вокруг какой точки фермы вьётся
            public float Seed;          // своя ветка шума — иначе рой летит строем
            public float Flap;          // фаза взмаха
            public Vector3 Previous;
        }

        [Header("Сколько и где")]
        [Tooltip("Сколько бабочек над фермой днём. Десяток читается как «летают», " +
                 "три — как «баг с одной бабочкой», тридцать — как нашествие.")]
        [SerializeField, Range(0, 40)] private int _count = 11;

        [Tooltip("Радиус, в котором они держатся, метров.")]
        [SerializeField, Min(2f)] private float _radius = 13f;

        [Tooltip("Насколько далеко каждая отходит от своей точки. Маленький разброс " +
                 "превращает их в привязанные шарики, большой — в равномерный шум по полю.")]
        [SerializeField, Min(0.5f)] private float _wander = 3.5f;

        [Header("Полёт")]
        [Tooltip("Высота порхания над землёй, от и до.")]
        [SerializeField] private Vector2 _height = new Vector2(0.5f, 1.7f);

        [Tooltip("Скорость блуждания. Больше — суетливее.")]
        [SerializeField, Range(0.02f, 0.6f)] private float _speed = 0.14f;

        [Tooltip("Взмахов в секунду.")]
        [SerializeField, Range(1f, 20f)] private float _flapsPerSecond = 7f;

        [Tooltip("Размах крыльев, метров. Крупнее настоящей бабочки намеренно: камера стоит " +
                 "в двух десятках метров, и честные пять сантиметров были бы пикселем. " +
                 "Ориентир — пучок травы рядом, а не живая бабочка.")]
        [SerializeField, Range(0.05f, 1f)] private float _span = 0.42f;

        [Tooltip("Какую долю времени бабочка сидит, а не летает. Отдых — самое живое в ней: " +
                 "то, что движется без остановки, читается как заводная игрушка.")]
        [SerializeField, Range(0f, 0.8f)] private float _restShare = 0.3f;

        [Header("Цвет")]
        [Tooltip("Из чего выбирается окраска. Пастель намеренно: яркая бабочка спорит " +
                 "с грядками за внимание, а её дело — быть замеченной краем глаза.")]
        [SerializeField] private Color[] _palette =
        {
            new Color(0.98f, 0.95f, 0.80f),
            new Color(0.96f, 0.80f, 0.55f),
            new Color(0.85f, 0.90f, 0.98f),
            new Color(0.98f, 0.86f, 0.90f),
            new Color(0.88f, 0.96f, 0.84f),
        };

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private readonly List<Critter> _critters = new List<Critter>();
        private Mesh _wingMesh;
        private Material _material;
        private Transform _holder;
        private bool _flying;

        private void OnDisable() => Recall();

        private void OnDestroy()
        {
            Recall();

            if (_wingMesh != null) Destroy(_wingMesh);
            if (_material != null) Destroy(_material);
        }

        /// <summary>
        /// Днём должны летать, ночью — нет. Сверкой каждый кадр, а не подпиской на
        /// <c>DayStarted</c>/<c>NightStarted</c>.
        /// <para>
        /// Подписка тут уже подводила: компонент вешается из <c>FarmJuice.OnEnable</c>, а
        /// <c>DayNightCycle.Instance</c> к тому моменту ещё пуст — ссылка захватывалась как
        /// null, подписки не случалось, и бабочки честно летали всю ночь. Сверка состояния
        /// не зависит ни от порядка инициализации, ни от переводов часов, ни от загрузки
        /// сохранения: любое рассогласование чинится следующим же кадром.
        /// </para>
        /// </summary>
        private void Sync()
        {
            var clock = DayNightCycle.Instance;
            bool day = clock == null || !clock.IsNight;

            if (day == _flying) return;

            if (day) Release();
            else Recall();
        }

        // ---- появление и уход ----

        private void Release()
        {
            if (_flying || _count <= 0) return;
            _flying = true;

            EnsureAssets();

            if (_holder == null)
            {
                _holder = new GameObject("Butterflies").transform;
                _holder.SetParent(transform, false);
            }

            for (int i = 0; i < _count; i++) _critters.Add(Build(i));
        }

        private void Recall()
        {
            _flying = false;

            for (int i = 0; i < _critters.Count; i++)
                if (_critters[i].Root != null) Destroy(_critters[i].Root.gameObject);

            _critters.Clear();
        }

        // ---- сборка ----

        private void EnsureAssets()
        {
            if (_wingMesh == null) _wingMesh = BuildWingMesh();

            if (_material != null) return;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            _material = new Material(shader) { name = "Butterfly", hideFlags = HideFlags.DontSave };

            // Крыло — плоскость, и со спины его видно ровно так же часто, как с лица.
            _material.SetFloat("_Cull", 0f);
            _material.SetFloat("_Smoothness", 0f);
            _material.doubleSidedGI = true;
        }

        /// <summary>
        /// Крыло: веер от линии сгиба, с передней долей и задней. Ось сгиба — локальный X,
        /// поэтому взмах делается поворотом вокруг Z и никакой скиннинг не нужен.
        /// <para>
        /// Ширина крыла сопоставима с длиной намеренно. Первая версия была узкой (длина втрое
        /// больше ширины) — и пара таких крыльев читалась не бабочкой, а белой полоской бумаги
        /// на траве. Узнаваемость здесь целиком в силуэте: деталей на такой мелочи не видно.
        /// </para>
        /// </summary>
        private static Mesh BuildWingMesh()
        {
            var mesh = new Mesh { name = "ButterflyWing", hideFlags = HideFlags.DontSave };

            mesh.vertices = new[]
            {
                new Vector3(0f,    0f, 0f),      // сгиб
                new Vector3(0.15f, 0f, 0.35f),
                new Vector3(0.85f, 0f, 0.75f),   // кончик передней доли
                new Vector3(1f,    0f, 0.05f),
                new Vector3(0.7f,  0f, -0.55f),  // задняя доля
                new Vector3(0.1f,  0f, -0.3f),
            };

            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 3, 4, 0, 4, 5 };

            var normals = new Vector3[6];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;
            mesh.normals = normals;

            mesh.RecalculateBounds();
            return mesh;
        }

        private Critter Build(int index)
        {
            var root = new GameObject("Butterfly").transform;
            root.SetParent(_holder, false);

            var critter = new Critter
            {
                Root = root,
                LeftWing = BuildWing(root, 1f),
                RightWing = BuildWing(root, -1f),
                Anchor = Random.insideUnitCircle * _radius,
                Seed = Random.value * 100f,
                Flap = Random.value * Mathf.PI * 2f,
            };

            var block = new MaterialPropertyBlock();
            var color = _palette != null && _palette.Length > 0
                ? _palette[index % _palette.Length]
                : Color.white;
            block.SetColor(BaseColorId, color);
            block.SetColor(ColorId, color);

            critter.LeftWing.GetComponent<MeshRenderer>().SetPropertyBlock(block);
            critter.RightWing.GetComponent<MeshRenderer>().SetPropertyBlock(block);

            critter.Previous = Place(critter, 0f);
            return critter;
        }

        private Transform BuildWing(Transform root, float side)
        {
            var wing = new GameObject(side > 0f ? "WingL" : "WingR");
            wing.transform.SetParent(root, false);
            wing.transform.localScale = new Vector3(_span * 0.5f * side, 1f, _span * 0.5f);

            wing.AddComponent<MeshFilter>().sharedMesh = _wingMesh;

            var renderer = wing.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            return wing.transform;
        }

        // ---- полёт ----

        private void Update()
        {
            Sync();
            if (!_flying) return;

            float dt = Time.deltaTime;

            for (int i = 0; i < _critters.Count; i++)
            {
                var critter = _critters[i];
                if (critter.Root == null) continue;

                Vector3 position = Place(critter, Time.time);

                // Курс — по тому, куда её несёт. Своей воли у бабочки нет, и это честно:
                // она и в жизни летит не туда, куда собиралась.
                Vector3 step = position - critter.Previous;
                if (step.sqrMagnitude > 1e-6f)
                    critter.Root.rotation = Quaternion.Slerp(
                        critter.Root.rotation, Quaternion.LookRotation(step, Vector3.up), 6f * dt);

                critter.Root.position = position;
                critter.Previous = position;

                Flap(critter, dt);
            }
        }

        /// <summary>Где бабочка в этот момент: снос шумом вокруг своей точки плюс высота.</summary>
        private Vector3 Place(Critter critter, float time)
        {
            float t = time * _speed;

            float x = critter.Anchor.x + Signed(Mathf.PerlinNoise(t, critter.Seed)) * _wander;
            float z = critter.Anchor.y + Signed(Mathf.PerlinNoise(t + 31.7f, critter.Seed)) * _wander;

            // Отдых — третий, более медленный шум: когда он низкий, бабочка почти сидит.
            float energy = Mathf.PerlinNoise(t * 0.35f + 71.3f, critter.Seed);
            float rest = Mathf.Clamp01((energy - _restShare) / Mathf.Max(0.05f, 1f - _restShare));

            float high = Mathf.Lerp(_height.x, _height.y, rest);
            float bob = Mathf.Sin(time * 2.3f + critter.Seed) * 0.08f * rest;

            var point = new Vector3(x, 0f, z);
            point.y = FarmingRuntime.Ground.SampleHeight(point) + high + bob;
            return point;
        }

        /// <summary>Взмах: сидящая складывает крылья и почти не машет.</summary>
        private void Flap(Critter critter, float dt)
        {
            float energy = Mathf.PerlinNoise(Time.time * _speed * 0.35f + 71.3f, critter.Seed);
            float rest = Mathf.Clamp01((energy - _restShare) / Mathf.Max(0.05f, 1f - _restShare));

            critter.Flap += dt * _flapsPerSecond * Mathf.PI * 2f * Mathf.Lerp(0.15f, 1f, rest);

            // Сидящая держит крылья почти сложенными — это и читается как «присела».
            float open = Mathf.Lerp(58f, 12f, 1f - rest);
            float angle = Mathf.Sin(critter.Flap) * open + Mathf.Lerp(60f, 20f, rest);

            critter.LeftWing.localRotation = Quaternion.Euler(0f, 0f, -angle);
            critter.RightWing.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        private static float Signed(float noise01) => noise01 * 2f - 1f;
    }
}
