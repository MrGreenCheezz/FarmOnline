using UnityEngine;
using UnityEngine.UIElements;

namespace Farm.Game
{
    /// <summary>
    /// Оживляет кнопку «В меню» в HUD. Отдельным компонентом, а не строчкой в самом HUD,
    /// потому что переходы между сценами — забота сборки игры, и <c>Farm.UI</c> о ней не знает.
    /// Так зависимость идёт в одну сторону: игра дотягивается до интерфейса, а не наоборот.
    /// <para>
    /// Перед уходом партия сохраняется — см. <see cref="GameFlow.ToMenu"/>. Выход в меню,
    /// теряющий последние двадцать минут, читается как поломка, а не как выход.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    [AddComponentMenu("Farm/Menu Return Button")]
    public sealed class MenuReturnButton : MonoBehaviour
    {
        private Button _button;

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>()?.rootVisualElement;
            _button = root?.Q<Button>("menu-button");

            if (_button == null) { enabled = false; return; }
            _button.clicked += GameFlow.ToMenu;
        }

        private void OnDisable()
        {
            if (_button != null) _button.clicked -= GameFlow.ToMenu;
            _button = null;
        }
    }
}
