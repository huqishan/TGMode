using Shared.Infrastructure.Extensions;
using Shared.Infrastructure.PackMethod;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Module.MES.Views
{
    public partial class DataStructureConfigView : UserControl, INotifyPropertyChanged
    {
        private static readonly string DataStructureConfigDirectory =
            Path.Combine(AppContext.BaseDirectory, "Config", "DataStructure");

        private static readonly Brush SuccessBrush =
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));

        private static readonly Brush WarningBrush =
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EA580C"));

        private static readonly Brush NeutralBrush =
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));


        private string _previewText = "请选择或创建一个数据结构配置。";
        private string _previewStatusText = "等待输入";
        private Brush _previewStatusBrush = NeutralBrush;

        public DataStructureConfigView()
        {
            InitializeComponent();
            DataContext = this;
        }

        public event PropertyChangedEventHandler? PropertyChanged;


        private void AddProfile_Click(object sender, RoutedEventArgs e)
        {
            
        }

        private void DuplicateProfile_Click(object sender, RoutedEventArgs e)
        {
           
        }

        private void DeleteProfile_Click(object sender, RoutedEventArgs e)
        {
           
        }

        private void SaveProfiles_Click(object sender, RoutedEventArgs e)
        {
            
        }

        
    }
}
