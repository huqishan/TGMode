using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ControlLibrary.Controls.Navigation.Models
{
    public class ControlInfoDataItem
    {
        public ControlInfoDataItem(string title, string imageIconPath, string? content, ObservableCollection<ControlInfoDataItem>? items, bool isEnable = true, bool isVisibility = true, string description = null)
        {
            this.UniqueId = Guid.NewGuid().ToString();
            this.Title = title;
            this.Description = description;
            this.ImageIconPath = imageIconPath;
            this.Content = content;
            this.IsEnable = isEnable;
            this.IsVisibility = isVisibility;
            this.Items = items ?? new ObservableCollection<ControlInfoDataItem>();
        }

        public string UniqueId { get; private set; }
        public string Title { get; private set; }
        public string Description { get; private set; }
        public string ImageIconPath { get; private set; }
        public string? Content { get; private set; }
        public bool IsEnable { get; set; }
        public bool IsVisibility { get; set; }
        public ObservableCollection<ControlInfoDataItem> Items { get; private set; }

        public override string ToString()
        {
            return this.Title;
        }
    }
}
