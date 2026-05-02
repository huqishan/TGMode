using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace ControlLibrary
{
    public abstract class ViewModelProperties : INotifyPropertyChanged
    {
        #region 通知事件

        public event PropertyChangedEventHandler? PropertyChanged;

        #endregion
        #region 属性通知方法

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
        protected bool SetField<T>(ref T field, T value, bool trimString, [CallerMemberName] string? propertyName = null) 
        {
            object? normalizedValue = value;
            if (trimString && value is string stringValue)
            {
                normalizedValue = stringValue.Trim();
            }

            if (Equals(field, normalizedValue))
            {
                return false;
            }

            field = (T)normalizedValue!;
            OnPropertyChanged(propertyName);
            return true;
        }
        public void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}
