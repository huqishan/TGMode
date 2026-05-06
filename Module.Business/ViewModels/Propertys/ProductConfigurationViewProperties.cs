using ControlLibrary;
using Module.Business.Models;
using Module.Business.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Module.Business.ViewModels;

/// <summary>
/// 产品配置界面的属性集中声明。
/// </summary>
public sealed partial class ProductConfigurationViewModel
{
    #region 状态颜色字段

    private static readonly Brush SuccessBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));

    private static readonly Brush WarningBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EA580C"));

    private static readonly Brush NeutralBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));

    #endregion

    #region 私有状态字段

    private BusinessConfigurationCatalog _catalog = BusinessConfigurationStore.LoadCatalog();
    private ProductProfile? _selectedProduct;
    private ProductKeyValueItem? _selectedKeyValue;
    private string _searchText = string.Empty;
    private string _pageStatusText = "等待编辑";
    private Brush _pageStatusBrush = NeutralBrush;
    private DateTime _lastCreateOrCopyCommandAt = DateTime.MinValue;

    #endregion

    #region 集合属性

    public ObservableCollection<ProductProfile> Products => _catalog.Products;

    public ICollectionView ProductsView { get; private set; } = null!;

    #endregion

    #region 搜索与当前编辑属性

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetField(ref _searchText, value ?? string.Empty))
            {
                return;
            }

            ProductsView.Refresh();
        }
    }

    public ProductProfile? SelectedProduct
    {
        get => _selectedProduct;
        set
        {
            if (ReferenceEquals(_selectedProduct, value))
            {
                return;
            }

            if (_selectedProduct is not null)
            {
                _selectedProduct.PropertyChanged -= SelectedProduct_PropertyChanged;
            }

            _selectedProduct = value;

            if (_selectedProduct is not null)
            {
                _selectedProduct.PropertyChanged += SelectedProduct_PropertyChanged;
            }

            SelectedKeyValue = _selectedProduct?.KeyValues.FirstOrDefault();
            OnPropertyChanged();
            RaisePageSummaryChanged();
            RaiseCommandStatesChanged();
        }
    }

    public ProductKeyValueItem? SelectedKeyValue
    {
        get => _selectedKeyValue;
        set
        {
            if (SetField(ref _selectedKeyValue, value))
            {
                RaiseCommandStatesChanged();
            }
        }
    }

    #endregion

    #region 页面状态属性

    public string PageStatusText
    {
        get => _pageStatusText;
        private set => SetField(ref _pageStatusText, value);
    }

    public Brush PageStatusBrush
    {
        get => _pageStatusBrush;
        private set => SetField(ref _pageStatusBrush, value);
    }

    public string ProductCountText => $"{Products.Count} 个产品";

    public string KeyValueCountText => SelectedProduct is null
        ? "未选择产品"
        : $"{SelectedProduct.KeyValueCount} 个键值对";

    #endregion

    #region 命令属性

    public ICommand NewProductCommand { get; private set; } = null!;

    public ICommand DuplicateProductCommand { get; private set; } = null!;

    public ICommand DeleteProductCommand { get; private set; } = null!;

    public ICommand SaveProductsCommand { get; private set; } = null!;

    public ICommand AddKeyValueCommand { get; private set; } = null!;

    public ICommand DeleteKeyValueCommand { get; private set; } = null!;

    #endregion

    #region 属性联动方法

    private void SelectedProduct_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProductProfile.ProductName)
            or nameof(ProductProfile.KeyValueCount)
            or nameof(ProductProfile.KeyValueSummary))
        {
            SelectedProduct?.MarkModified();
        }

        if (e.PropertyName is nameof(ProductProfile.ProductName)
            or nameof(ProductProfile.KeyValueCount)
            or nameof(ProductProfile.KeyValueSummary)
            or nameof(ProductProfile.LastModifiedAt)
            or nameof(ProductProfile.LastModifiedText))
        {
            RaisePageSummaryChanged();
            ProductsView.Refresh();
        }
    }

    private void RaisePageSummaryChanged()
    {
        OnPropertyChanged(nameof(ProductCountText));
        OnPropertyChanged(nameof(KeyValueCountText));
    }

    #endregion
}
