using System;
using Unity.Properties;
using UnityEngine.UIElements;

namespace Demo
{
    // Plain C# view model bound to BattleHud.uxml via runtime data binding.
    // Setters short-circuit on unchanged values; the controller can poll
    // every frame without churning the binding system.
    public class BattleHudViewModel : INotifyBindablePropertyChanged
    {
        public event EventHandler<BindablePropertyChangedEventArgs> propertyChanged;

        string _redAliveText  = "Red: -";
        string _blueAliveText = "Blue: -";
        string _winnerText    = string.Empty;

        [CreateProperty]
        public string RedAliveText
        {
            get => _redAliveText;
            set
            {
                if (_redAliveText == value) return;
                _redAliveText = value;
                propertyChanged?.Invoke(this, new BindablePropertyChangedEventArgs(nameof(RedAliveText)));
            }
        }

        [CreateProperty]
        public string BlueAliveText
        {
            get => _blueAliveText;
            set
            {
                if (_blueAliveText == value) return;
                _blueAliveText = value;
                propertyChanged?.Invoke(this, new BindablePropertyChangedEventArgs(nameof(BlueAliveText)));
            }
        }

        [CreateProperty]
        public string WinnerText
        {
            get => _winnerText;
            set
            {
                if (_winnerText == value) return;
                _winnerText = value;
                propertyChanged?.Invoke(this, new BindablePropertyChangedEventArgs(nameof(WinnerText)));
            }
        }
    }
}
