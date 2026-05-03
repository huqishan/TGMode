using ControlLibrary;
using Module.Business.Models;
using Module.Business.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Module.Business.ViewModels;

/// <summary>
/// 产品配置界面命令实现。
/// </summary>
public sealed partial class ProductConfigurationViewModel
{
    #region 构造与初始化

    public ProductConfigurationViewModel()
    {
        Products.CollectionChanged += Products_CollectionChanged;
        ProductsView = CollectionViewSource.GetDefaultView(Products);
        ProductsView.Filter = FilterProducts;
        InitializeCommands();
        SelectedProduct = Products.FirstOrDefault();
        SetPageStatus(Products.Count == 0 ? "暂无产品配置，请点击新增。" : $"已读取 {Products.Count} 个产品", NeutralBrush);
    }

    /// <summary>
    /// 初始化页面命令，所有按钮通过 Command 绑定。
    /// </summary>
    private void InitializeCommands()
    {
        NewProductCommand = new RelayCommand(_ => NewProduct());
        DuplicateProductCommand = new RelayCommand(_ => DuplicateSelectedProduct(), _ => SelectedProduct is not null);
        DeleteProductCommand = new RelayCommand(_ => DeleteSelectedProduct(), _ => SelectedProduct is not null);
        SaveProductsCommand = new RelayCommand(_ => SaveProducts());
        AddKeyValueCommand = new RelayCommand(_ => AddKeyValue(), _ => SelectedProduct is not null);
        DeleteKeyValueCommand = new RelayCommand(_ => DeleteSelectedKeyValue(), _ => SelectedProduct is not null && SelectedKeyValue is not null);
    }

    #endregion

    #region 产品命令方法

    /// <summary>
    /// 新增产品配置。
    /// </summary>
    private void NewProduct()
    {
        if (!CanRunCreateOrCopyCommand())
        {
            return;
        }

        ProductProfile product = CreateProduct(GenerateUniqueProductName("产品"));
        Products.Add(product);
        SelectCreatedProduct(product);
        SetPageStatus("已新增产品，填写键值对后点击保存。", SuccessBrush);
    }

    /// <summary>
    /// 复制当前产品及其键值对。
    /// </summary>
    private void DuplicateSelectedProduct()
    {
        if (!CanRunCreateOrCopyCommand())
        {
            return;
        }

        if (SelectedProduct is null)
        {
            return;
        }

        ProductProfile product = CreateCopyProduct(SelectedProduct);
        Products.Add(product);
        SelectCreatedProduct(product);
        SetPageStatus($"已复制产品：{product.ProductName}", SuccessBrush);
    }

    /// <summary>
    /// 删除当前选中的产品。
    /// </summary>
    private void DeleteSelectedProduct()
    {
        if (SelectedProduct is null)
        {
            return;
        }

        int index = Products.IndexOf(SelectedProduct);
        Products.Remove(SelectedProduct);
        SelectedProduct = Products.Count == 0
            ? null
            : Products[Math.Clamp(index, 0, Products.Count - 1)];

        SetPageStatus("已删除产品，点击保存后生效。", WarningBrush);
    }

    /// <summary>
    /// 保存全部产品配置。
    /// </summary>
    private void SaveProducts()
    {
        if (!ValidateProducts(out string message))
        {
            SetPageStatus(message, WarningBrush);
            return;
        }

        BusinessConfigurationStore.SaveCatalog(_catalog);
        SetPageStatus($"已保存 {Products.Count} 个产品。", SuccessBrush);
    }

    #endregion

    #region 键值对命令方法

    /// <summary>
    /// 给当前产品新增一个键值对。
    /// </summary>
    private void AddKeyValue()
    {
        if (SelectedProduct is null)
        {
            return;
        }

        ProductKeyValueItem item = new()
        {
            Key = GenerateUniqueKeyName(SelectedProduct, "参数"),
            Value = string.Empty
        };

        SelectedProduct.KeyValues.Add(item);
        SelectedProduct.MarkModified();
        SelectedKeyValue = item;
        SetPageStatus("已新增键值对。", SuccessBrush);
    }

    /// <summary>
    /// 删除当前选中的键值对。
    /// </summary>
    private void DeleteSelectedKeyValue()
    {
        if (SelectedProduct is null || SelectedKeyValue is null)
        {
            return;
        }

        int index = SelectedProduct.KeyValues.IndexOf(SelectedKeyValue);
        SelectedProduct.KeyValues.Remove(SelectedKeyValue);
        SelectedProduct.MarkModified();
        SelectedKeyValue = SelectedProduct.KeyValues.Count == 0
            ? null
            : SelectedProduct.KeyValues[Math.Clamp(index, 0, SelectedProduct.KeyValues.Count - 1)];

        SetPageStatus("已删除键值对。", WarningBrush);
    }

    #endregion

    #region 工具方法

    private ProductProfile CreateProduct(string productName)
    {
        ProductProfile product = new()
        {
            ProductName = productName,
            LastModifiedAt = DateTime.Now
        };

        product.KeyValues.Add(new ProductKeyValueItem
        {
            Key = "型号",
            Value = string.Empty
        });

        return product;
    }

    private ProductProfile CreateCopyProduct(ProductProfile source)
    {
        return new ProductProfile
        {
            Id = Guid.NewGuid().ToString("N"),
            ProductName = GenerateCopyProductName(source.ProductName),
            LastModifiedAt = DateTime.Now,
            KeyValues = new ObservableCollection<ProductKeyValueItem>(
                source.KeyValues.Select(item => new ProductKeyValueItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Key = item.Key,
                    Value = item.Value
                }))
        };
    }

    private void SelectCreatedProduct(ProductProfile product)
    {
        SearchText = string.Empty;
        ProductsView.Refresh();
        SelectedProduct = product;
        ProductsView.MoveCurrentTo(product);
    }

    private bool CanRunCreateOrCopyCommand()
    {
        DateTime now = DateTime.UtcNow;
        if (now - _lastCreateOrCopyCommandAt < TimeSpan.FromMilliseconds(300))
        {
            return false;
        }

        _lastCreateOrCopyCommandAt = now;
        return true;
    }

    private string GenerateUniqueProductName(string prefix)
    {
        HashSet<string> existingNames = new(Products.Select(product => product.ProductName), StringComparer.OrdinalIgnoreCase);
        int index = existingNames.Count + 1;
        string candidate;

        do
        {
            candidate = $"{prefix} {index}";
            index++;
        }
        while (existingNames.Contains(candidate));

        return candidate;
    }

    private string GenerateCopyProductName(string baseName)
    {
        HashSet<string> existingNames = new(Products.Select(product => product.ProductName), StringComparer.OrdinalIgnoreCase);
        string copyName = $"{baseName.Trim()} 副本";
        if (!existingNames.Contains(copyName))
        {
            return copyName;
        }

        for (int index = 2; ; index++)
        {
            string candidate = $"{copyName} {index}";
            if (!existingNames.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private static string GenerateUniqueKeyName(ProductProfile product, string prefix)
    {
        HashSet<string> existingKeys = new(product.KeyValues.Select(item => item.Key), StringComparer.OrdinalIgnoreCase);
        int index = existingKeys.Count + 1;
        string candidate;

        do
        {
            candidate = $"{prefix} {index}";
            index++;
        }
        while (existingKeys.Contains(candidate));

        return candidate;
    }

    private bool FilterProducts(object item)
    {
        if (item is not ProductProfile product)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        string keyword = SearchText.Trim();
        return Contains(product.ProductName, keyword) ||
               Contains(product.LastModifiedText, keyword);
    }

    private static bool Contains(string? source, string keyword)
    {
        return source?.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private bool ValidateProducts(out string message)
    {
        if (Products.Count == 0)
        {
            message = "请至少新增一个产品。";
            return false;
        }

        HashSet<string> productNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (ProductProfile product in Products)
        {
            if (string.IsNullOrWhiteSpace(product.ProductName))
            {
                message = "产品名称不能为空。";
                return false;
            }

            if (!productNames.Add(product.ProductName.Trim()))
            {
                message = $"产品名称不能重复：{product.ProductName}";
                return false;
            }

            HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);
            foreach (ProductKeyValueItem item in product.KeyValues)
            {
                if (string.IsNullOrWhiteSpace(item.Key))
                {
                    message = $"产品“{product.ProductName}”的键不能为空。";
                    return false;
                }

                if (!keys.Add(item.Key.Trim()))
                {
                    message = $"产品“{product.ProductName}”的键不能重复：{item.Key}";
                    return false;
                }
            }
        }

        message = string.Empty;
        return true;
    }

    private void Products_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RaisePageSummaryChanged();
        ProductsView.Refresh();
        RaiseCommandStatesChanged();
    }

    private void SetPageStatus(string text, Brush brush)
    {
        PageStatusText = text;
        PageStatusBrush = brush;
    }

    private void RaiseCommandStatesChanged()
    {
        RaiseCommandState(DuplicateProductCommand);
        RaiseCommandState(DeleteProductCommand);
        RaiseCommandState(AddKeyValueCommand);
        RaiseCommandState(DeleteKeyValueCommand);
    }

    private static void RaiseCommandState(ICommand? command)
    {
        if (command is RelayCommand relayCommand)
        {
            relayCommand.RaiseCanExecuteChanged();
        }
    }

    #endregion
}
