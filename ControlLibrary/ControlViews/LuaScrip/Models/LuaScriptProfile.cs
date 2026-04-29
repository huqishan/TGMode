using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ControlLibrary.ControlViews.LuaScrip.Models
{
    public sealed class LuaScriptProfile : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string _scriptText = string.Empty;
        private DateTime _lastModifiedAt = DateTime.Now;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Name
        {
            get => _name;
            set
            {
                if (SetField(ref _name, value ?? string.Empty))
                {
                    Touch();
                }
            }
        }

        public string ScriptText
        {
            get => _scriptText;
            set
            {
                if (SetField(ref _scriptText, NormalizeLineEndings(value)))
                {
                    Touch();
                    OnPropertyChanged(nameof(LineCount));
                    OnPropertyChanged(nameof(Summary));
                }
            }
        }

        public DateTime LastModifiedAt
        {
            get => _lastModifiedAt;
            set
            {
                if (SetField(ref _lastModifiedAt, value))
                {
                    OnPropertyChanged(nameof(Summary));
                }
            }
        }

        public int LineCount => string.IsNullOrEmpty(ScriptText)
            ? 1
            : ScriptText.Count(character => character == '\n') + 1;

        public string Summary => $"{LineCount} 行 · 修改于 {LastModifiedAt:yyyy-MM-dd HH:mm}";

        public LuaScriptProfile Clone(string name)
        {
            return new LuaScriptProfile
            {
                Name = name,
                ScriptText = ScriptText,
                LastModifiedAt = DateTime.Now
            };
        }

        internal void AcceptLoadedState(DateTime lastModifiedAt)
        {
            _lastModifiedAt = lastModifiedAt == default ? DateTime.Now : lastModifiedAt;
            OnPropertyChanged(nameof(LastModifiedAt));
            OnPropertyChanged(nameof(Summary));
        }

        private void Touch()
        {
            LastModifiedAt = DateTime.Now;
            OnPropertyChanged(nameof(Summary));
        }

        private static string NormalizeLineEndings(string? text)
        {
            return (text ?? string.Empty)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\r", "\n", StringComparison.Ordinal)
                .Replace("\n", "\r\n", StringComparison.Ordinal);
        }

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
