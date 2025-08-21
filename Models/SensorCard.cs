using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CMDevicesManager.Models
{
    public sealed class SensorCard : INotifyPropertyChanged
    {
        public string Name { get; }
        public string SubTitle { get; }
        public string Unit { get; }
        public string IconGlyph { get; }

        private double _value;
        public double Value
        {
            get => _value;
            set { if (_value != value) { _value = value; OnPropertyChanged(); } }
        }

        public SensorCard(string name, string subTitle, string unit, string iconGlyph)
        {
            Name = name;
            SubTitle = subTitle;
            Unit = unit;
            IconGlyph = iconGlyph;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}