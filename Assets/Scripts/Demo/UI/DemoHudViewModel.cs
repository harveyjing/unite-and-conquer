using System;
using Unity.Properties;
using UnityEngine.UIElements;

namespace Demo
{
    // View model for the demo HUD. Plain C# class implementing
    // INotifyBindingPropertyChanged so the UI Toolkit runtime data
    // binding system can observe property changes. Each setter
    // short-circuits when the new value equals the old value, so the
    // controller can call setters every frame without redundant binding
    // updates or text reassignment.
    public class DemoHudViewModel : INotifyBindablePropertyChanged
    {
        public event EventHandler<BindablePropertyChangedEventArgs> propertyChanged;

        string _connectionText = "Disconnected";
        string _positionText   = "Pos: -";
        string _ghostCountText = "Ghosts: 0";
        string _tickText       = "Tick: -";

        [CreateProperty]
        public string ConnectionText
        {
            get => _connectionText;
            set
            {
                if (_connectionText == value) return;
                _connectionText = value;
                propertyChanged?.Invoke(this, new BindablePropertyChangedEventArgs(nameof(ConnectionText)));
            }
        }

        [CreateProperty]
        public string PositionText
        {
            get => _positionText;
            set
            {
                if (_positionText == value) return;
                _positionText = value;
                propertyChanged?.Invoke(this, new BindablePropertyChangedEventArgs(nameof(PositionText)));
            }
        }

        [CreateProperty]
        public string GhostCountText
        {
            get => _ghostCountText;
            set
            {
                if (_ghostCountText == value) return;
                _ghostCountText = value;
                propertyChanged?.Invoke(this, new BindablePropertyChangedEventArgs(nameof(GhostCountText)));
            }
        }

        [CreateProperty]
        public string TickText
        {
            get => _tickText;
            set
            {
                if (_tickText == value) return;
                _tickText = value;
                propertyChanged?.Invoke(this, new BindablePropertyChangedEventArgs(nameof(TickText)));
            }
        }
    }
}
