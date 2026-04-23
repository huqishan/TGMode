using System;

namespace ControlLibrary.Controls.Navigation.Models
{
    /// <summary>
    /// Event payload for the custom navigation control.
    /// </summary>
    public sealed class ModernNavigationSelectionChangedEventArgs : EventArgs
    {
        public ModernNavigationSelectionChangedEventArgs(ControlInfoDataItem? selectedItem, bool isSettingsSelected)
        {
            SelectedItem = selectedItem;
            IsSettingsSelected = isSettingsSelected;
        }

        public ControlInfoDataItem? SelectedItem { get; }

        public bool IsSettingsSelected { get; }
    }
}
