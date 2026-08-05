using UnityEngine;
using UnityEngine.UIElements;

namespace Farm.Game
{
    /// <summary>
    /// Главное меню. Как и весь интерфейс проекта: раскладка живёт в UXML, стили в USS,
    /// а C# только заполняет именованные элементы и вешает обработчики.
    /// <para>
    /// «Продолжить» — первая кнопка и она же подсвечена, когда есть что продолжать: игрок,
    /// вернувшийся к своей ферме, не должен искать её глазами. Нет сохранения — кнопка
    /// исчезает совсем, а не висит серой: пустая кнопка обещает то, чего нет.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    [AddComponentMenu("Farm/Main Menu")]
    public sealed class MainMenu : MonoBehaviour
    {
        private VisualElement _root;
        private Button _continue;
        private Label _saveInfo;
        private VisualElement _confirm;

        private void OnEnable()
        {
            _root = GetComponent<UIDocument>()?.rootVisualElement;
            if (_root == null) { enabled = false; return; }

            _continue = _root.Q<Button>("menu-continue");
            _saveInfo = _root.Q<Label>("menu-save-info");
            _confirm = _root.Q<VisualElement>("menu-confirm");

            var newGame = _root.Q<Button>("menu-new");
            var quit = _root.Q<Button>("menu-quit");

            if (_continue != null) _continue.clicked += GameFlow.Continue;
            if (newGame != null) newGame.clicked += OnNewGame;

            if (quit != null)
            {
                // В браузере выходить некуда — кнопку убираем совсем, как и «Продолжить»
                // без сохранения. Мёртвый пункт хуже отсутствующего.
                if (GameFlow.CanQuit) quit.clicked += GameFlow.Quit;
                else quit.style.display = DisplayStyle.None;
            }

            var confirmYes = _root.Q<Button>("menu-confirm-yes");
            var confirmNo = _root.Q<Button>("menu-confirm-no");
            if (confirmYes != null) confirmYes.clicked += GameFlow.NewGame;
            if (confirmNo != null) confirmNo.clicked += HideConfirm;

            HideConfirm();
            Refresh();
        }

        private void Refresh()
        {
            var save = FarmSave.Exists ? FarmSave.Read() : null;

            // Нет партии — нет и кнопки. Ряд из двух пунктов честнее ряда из трёх, где один мёртв.
            if (_continue != null)
                _continue.style.display = save != null ? DisplayStyle.Flex : DisplayStyle.None;

            if (_saveInfo == null) return;

            _saveInfo.text = save != null ? save.Describe() : "новая ферма ждёт хозяина";
            _saveInfo.EnableInClassList("menu__info--empty", save == null);
        }

        /// <summary>
        /// Новая игра поверх существующей — единственное необратимое действие в меню,
        /// и единственное, которое спрашивает.
        /// </summary>
        private void OnNewGame()
        {
            if (!FarmSave.Exists) { GameFlow.NewGame(); return; }
            if (_confirm != null) _confirm.style.display = DisplayStyle.Flex;
        }

        private void HideConfirm()
        {
            if (_confirm != null) _confirm.style.display = DisplayStyle.None;
        }
    }
}
