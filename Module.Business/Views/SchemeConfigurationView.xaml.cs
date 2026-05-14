using Module.Business.Models;
using Module.Business.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Shared.Infrastructure.Extensions;

namespace Module.Business.Views
{
    /// <summary>
    /// 方案配置界面的交互逻辑。
    /// </summary>
    public partial class SchemeConfigurationView : UserControl
    {
        #region 常量与字段

        private const string SchemeStepDragDataFormat = "Module.Business.SchemeWorkStepItem";
        private const string OperationDragDataFormat = "Module.Business.WorkStepOperation";
        private const double OperationDrawerClosedOffset = 56d;
        private const double InlineParameterDrawerClosedOffset = 56d;

        private static readonly Duration OperationDrawerAnimationDuration =
            new(TimeSpan.FromMilliseconds(220));

        private static readonly IEasingFunction OperationDrawerEasing =
            new CubicEase { EasingMode = EasingMode.EaseOut };

        private Point _schemeStepDragStartPoint;
        private Point _operationDragStartPoint;
        private Point _operationMethodDragStartPoint;
        private SchemeWorkStepItem? _pendingDraggedSchemeStep;
        private WorkStepOperation? _pendingDraggedOperation;
        private DataRowView? _pendingDraggedOperationMethodRow;
        private bool _isInlineParameterDrawerOpen;

        #endregion

        #region 构造与生命周期

        public SchemeConfigurationView()
        {
            InitializeComponent();
            DataContext = new SchemeConfigurationViewModel();
            HookOperationMethodDragEvents();
            Loaded += SchemeConfigurationView_Loaded;
            Unloaded += SchemeConfigurationView_Unloaded;
            UpdateOperationDrawerVisual(animate: false);
            UpdateInlineParameterDrawerVisual(animate: false);
        }

        private SchemeConfigurationViewModel? ViewModel => DataContext as SchemeConfigurationViewModel;

        private void SchemeConfigurationView_Loaded(object sender, RoutedEventArgs e)
        {
            if (ViewModel is not null)
            {
                ViewModel.SchemeStepEditor.PropertyChanged += SchemeStepEditor_PropertyChanged;
            }

            UpdateOperationDrawerVisual(animate: false);
            UpdateInlineParameterDrawerVisual(animate: false);
        }

        private void SchemeConfigurationView_Unloaded(object sender, RoutedEventArgs e)
        {
            if (ViewModel is not null)
            {
                ViewModel.SchemeStepEditor.PropertyChanged -= SchemeStepEditor_PropertyChanged;
            }
        }

        #endregion

        #region 属性联动

        /// <summary>
        /// 切换当前方案时退出重命名态，避免文本框停留在旧方案上。
        /// </summary>
        /// <summary>
        /// 同步步骤抽屉开关动画。
        /// </summary>
        private void SchemeStepEditor_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(WorkStepConfigurationViewModel.IsOperationDrawerOpen))
            {
                UpdateOperationDrawerVisual(animate: true);
            }
        }

        #endregion

        #region 方案名称重命名

        /// <summary>
        /// 进入方案名称编辑态，显示文本框并自动全选。
        /// </summary>
        /// <summary>
        /// 文本框失焦后退出编辑态并保留当前输入。
        /// </summary>
        /// <summary>
        /// 回车确认，Esc 恢复编辑前名称。
        /// </summary>
        /// <summary>
        /// 控制方案名称的显示态与编辑态切换。
        /// </summary>
        #endregion

        #region 方案工步拖拽

        private void SaveSchemesButton_Click(object sender, RoutedEventArgs e)
        {
            CommitSchemeNameTextBoxes();
            CommitEditableDataGrids();

            if (ViewModel?.SaveSchemesCommand.CanExecute(null) == true)
            {
                ViewModel.SaveSchemesCommand.Execute(null);
            }
        }

        private void CommitEditableDataGrids()
        {
            SchemeStepsDataGrid?.CommitEdit(DataGridEditingUnit.Cell, true);
            SchemeStepsDataGrid?.CommitEdit(DataGridEditingUnit.Row, true);
            OperationsDataGrid?.CommitEdit(DataGridEditingUnit.Cell, true);
            OperationsDataGrid?.CommitEdit(DataGridEditingUnit.Row, true);
        }

        /// <summary>
        /// 淇濆瓨鍓嶆彁浜ゅ綋鍓嶆柟妗堝悕绉扮殑缁戝畾鍊硷紝閬垮厤鍥犳湭澶辩劍鑰屼涪澶辨渶鍚庝竴娆¤緭鍏ャ€?
        /// </summary>
        private void CommitSchemeNameTextBoxes()
        {
            if (SchemesListBox is null)
            {
                return;
            }

            foreach (TextBox textBox in FindVisualChildren<TextBox>(SchemesListBox))
            {
                BindingExpression? bindingExpression = textBox.GetBindingExpression(TextBox.TextProperty);
                if (bindingExpression?.ParentBinding?.Path?.Path == nameof(SchemeProfile.SchemeName))
                {
                    bindingExpression.UpdateSource();
                }
            }
        }

        private void SchemeStepsDataGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsInlineEditableSchemeStepElement(e.OriginalSource as DependencyObject))
            {
                _pendingDraggedSchemeStep = null;
                return;
            }

            _schemeStepDragStartPoint = e.GetPosition(SchemeStepsDataGrid);
            _pendingDraggedSchemeStep = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject)?.Item as SchemeWorkStepItem;
        }

        private void SchemeStepsDataGrid_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _pendingDraggedSchemeStep is null)
            {
                return;
            }

            Point currentPoint = e.GetPosition(SchemeStepsDataGrid);
            if (Math.Abs(currentPoint.X - _schemeStepDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(currentPoint.Y - _schemeStepDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            SchemeWorkStepItem draggedSchemeStep = _pendingDraggedSchemeStep;
            _pendingDraggedSchemeStep = null;

            DataObject dataObject = new();
            dataObject.SetData(SchemeStepDragDataFormat, draggedSchemeStep);
            DragDrop.DoDragDrop(SchemeStepsDataGrid, dataObject, DragDropEffects.Move);
        }

        private void SchemeStepsDataGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject) is not DataGridRow row)
            {
                return;
            }

            row.IsSelected = true;
            SchemeStepsDataGrid.SelectedItem = row.Item;
            row.Focus();
        }

        private void SchemeStepsDataGrid_DragOver(object sender, DragEventArgs e)
        {
            if (!TryGetSchemeStepDropInfo(e, out _, out _, out bool insertAfter))
            {
                HideSchemeStepDropIndicator();
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            ShowSchemeStepDropIndicator(FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject), insertAfter);
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void SchemeStepsDataGrid_DragLeave(object sender, DragEventArgs e)
        {
            HideSchemeStepDropIndicator();
        }

        private void SchemeStepsDataGrid_Drop(object sender, DragEventArgs e)
        {
            if (TryGetSchemeStepDropInfo(e, out SchemeWorkStepItem? draggedSchemeStep, out SchemeWorkStepItem? targetSchemeStep, out bool insertAfter) &&
                draggedSchemeStep is not null &&
                targetSchemeStep is not null)
            {
                ViewModel?.MoveSchemeStep(draggedSchemeStep, targetSchemeStep, insertAfter);
            }

            _pendingDraggedSchemeStep = null;
            HideSchemeStepDropIndicator();
            e.Handled = true;
        }

        private bool TryGetSchemeStepDropInfo(
            DragEventArgs e,
            out SchemeWorkStepItem? draggedSchemeStep,
            out SchemeWorkStepItem? targetSchemeStep,
            out bool insertAfter)
        {
            draggedSchemeStep = e.Data.GetDataPresent(SchemeStepDragDataFormat)
                ? e.Data.GetData(SchemeStepDragDataFormat) as SchemeWorkStepItem
                : null;
            targetSchemeStep = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject)?.Item as SchemeWorkStepItem;
            insertAfter = false;

            if (draggedSchemeStep is null || targetSchemeStep is null || ReferenceEquals(draggedSchemeStep, targetSchemeStep))
            {
                return false;
            }

            DataGridRow? targetRow = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
            if (targetRow is not null)
            {
                insertAfter = e.GetPosition(targetRow).Y > targetRow.ActualHeight / 2d;
            }

            return true;
        }

        private void ShowSchemeStepDropIndicator(DataGridRow? targetRow, bool insertAfter)
        {
            if (targetRow is null || SchemeStepDropIndicatorCanvas is null || SchemeStepDropIndicator is null)
            {
                HideSchemeStepDropIndicator();
                return;
            }

            double horizontalPadding = 8d;
            double indicatorHeight = 3d;
            double width = Math.Max(0d, SchemeStepDropIndicatorCanvas.ActualWidth - horizontalPadding * 2);
            Point rowTopLeft = targetRow.TranslatePoint(new Point(0, 0), SchemeStepDropIndicatorCanvas);
            double top = rowTopLeft.Y + (insertAfter ? targetRow.ActualHeight : 0d) - indicatorHeight / 2d;
            top = Math.Clamp(top, 0d, Math.Max(0d, SchemeStepDropIndicatorCanvas.ActualHeight - indicatorHeight));

            SchemeStepDropIndicator.Width = width;
            SchemeStepDropIndicator.Height = indicatorHeight;
            Canvas.SetLeft(SchemeStepDropIndicator, horizontalPadding);
            Canvas.SetTop(SchemeStepDropIndicator, top);
            SchemeStepDropIndicator.Visibility = Visibility.Visible;
        }

        private void HideSchemeStepDropIndicator()
        {
            if (SchemeStepDropIndicator is not null)
            {
                SchemeStepDropIndicator.Visibility = Visibility.Collapsed;
            }
        }

        #endregion

        #region 方法指令拖拽

        private void HookOperationMethodDragEvents()
        {
            OperationMethodDataGrid.PreviewMouseLeftButtonDown += OperationMethodDataGrid_PreviewMouseLeftButtonDown;
            OperationMethodDataGrid.PreviewMouseMove += OperationMethodDataGrid_PreviewMouseMove;
            OperationMethodDataGrid.SelectionChanged += OperationMethodDataGrid_SelectionChanged;
        }

        private void OperationMethodDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ViewModel is not null)
            {
                ViewModel.SchemeStepEditor.SelectedOperationMethodRow = OperationMethodDataGrid.SelectedItem as DataRowView;
            }
        }

        private void OperationMethodDataGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _operationMethodDragStartPoint = e.GetPosition(OperationMethodDataGrid);
            _pendingDraggedOperationMethodRow = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject)?.Item as DataRowView;
        }

        private void OperationMethodDataGrid_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _pendingDraggedOperationMethodRow is null)
            {
                return;
            }

            Point currentPoint = e.GetPosition(OperationMethodDataGrid);
            if (Math.Abs(currentPoint.X - _operationMethodDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(currentPoint.Y - _operationMethodDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            WorkStepOperation? operation = ViewModel?.SchemeStepEditor.CreateOperationFromMethodTableRow(_pendingDraggedOperationMethodRow);
            _pendingDraggedOperationMethodRow = null;
            if (operation is null)
            {
                return;
            }

            DataObject dataObject = new();
            dataObject.SetData(OperationDragDataFormat, operation);
            dataObject.SetData(DataFormats.StringFormat, operation.DisplayText);
            DragDrop.DoDragDrop(OperationMethodDataGrid, dataObject, DragDropEffects.Copy);
        }

        #endregion

        #region 步骤拖拽与双击编辑

        private void OperationsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (IsOperationSelectionCheckBox(e.OriginalSource as DependencyObject) ||
                IsInlineEditableOperationCell(e.OriginalSource as DependencyObject))
            {
                return;
            }

            if (FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject)?.Item is not WorkStepOperation operation)
            {
                return;
            }

            OperationsDataGrid.SelectedItem = operation;
            ViewModel?.SchemeStepEditor.OpenOperationDrawerForEdit(operation);
            e.Handled = true;
        }

        private void OperationsDataGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsOperationSelectionCheckBox(e.OriginalSource as DependencyObject) ||
                IsInlineEditableOperationCell(e.OriginalSource as DependencyObject))
            {
                _pendingDraggedOperation = null;
                return;
            }

            _operationDragStartPoint = e.GetPosition(OperationsDataGrid);
            _pendingDraggedOperation = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject)?.Item as WorkStepOperation;
        }

        private void OperationsDataGrid_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _pendingDraggedOperation is null)
            {
                return;
            }

            Point currentPoint = e.GetPosition(OperationsDataGrid);
            if (Math.Abs(currentPoint.X - _operationDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(currentPoint.Y - _operationDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            WorkStepOperation draggedOperation = _pendingDraggedOperation;
            _pendingDraggedOperation = null;

            DataObject dataObject = new();
            dataObject.SetData(OperationDragDataFormat, draggedOperation);
            DragDrop.DoDragDrop(OperationsDataGrid, dataObject, DragDropEffects.Move);
        }

        private void OperationsDataGrid_DragOver(object sender, DragEventArgs e)
        {
            if (!TryGetOperationDropInfo(e, out WorkStepOperation? draggedOperation, out _, out bool insertAfter, out bool isExistingOperation) ||
                draggedOperation is null)
            {
                HideOperationDropIndicator();
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            ShowOperationDropIndicator(FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject), insertAfter);
            e.Effects = isExistingOperation ? DragDropEffects.Move : DragDropEffects.Copy;
            e.Handled = true;
        }

        private void OperationsDataGrid_DragLeave(object sender, DragEventArgs e)
        {
            HideOperationDropIndicator();
        }

        private void OperationsDataGrid_Drop(object sender, DragEventArgs e)
        {
            if (TryGetOperationDropInfo(
                    e,
                    out WorkStepOperation? draggedOperation,
                    out WorkStepOperation? targetOperation,
                    out bool insertAfter,
                    out bool isExistingOperation) &&
                draggedOperation is not null)
            {
                if (isExistingOperation && targetOperation is not null)
                {
                    ViewModel?.SchemeStepEditor.MoveOperation(draggedOperation, targetOperation, insertAfter);
                }
                else if (!isExistingOperation)
                {
                    ViewModel?.SchemeStepEditor.InsertOperation(draggedOperation, targetOperation, insertAfter);
                }
            }

            _pendingDraggedOperation = null;
            HideOperationDropIndicator();
            e.Handled = true;
        }

        private bool TryGetOperationDropInfo(
            DragEventArgs e,
            out WorkStepOperation? draggedOperation,
            out WorkStepOperation? targetOperation,
            out bool insertAfter,
            out bool isExistingOperation)
        {
            draggedOperation = e.Data.GetDataPresent(OperationDragDataFormat)
                ? e.Data.GetData(OperationDragDataFormat) as WorkStepOperation
                : null;
            targetOperation = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject)?.Item as WorkStepOperation;
            insertAfter = false;
            isExistingOperation = draggedOperation is not null &&
                                  ViewModel?.SchemeStepEditor.SelectedWorkStep?.Steps.Contains(draggedOperation) == true;

            if (draggedOperation is null)
            {
                return false;
            }

            DataGridRow? targetRow = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
            if (targetRow is not null)
            {
                insertAfter = e.GetPosition(targetRow).Y > targetRow.ActualHeight / 2d;
            }

            if (isExistingOperation)
            {
                return targetOperation is not null && !ReferenceEquals(draggedOperation, targetOperation);
            }

            return ViewModel?.SchemeStepEditor.SelectedWorkStep is not null;
        }

        private void ShowOperationDropIndicator(DataGridRow? targetRow, bool insertAfter)
        {
            if (targetRow is null || OperationDropIndicatorCanvas is null || OperationDropIndicator is null)
            {
                HideOperationDropIndicator();
                return;
            }

            double horizontalPadding = 8d;
            double indicatorHeight = 3d;
            double width = Math.Max(0d, OperationDropIndicatorCanvas.ActualWidth - horizontalPadding * 2);
            Point rowTopLeft = targetRow.TranslatePoint(new Point(0, 0), OperationDropIndicatorCanvas);
            double top = rowTopLeft.Y + (insertAfter ? targetRow.ActualHeight : 0d) - indicatorHeight / 2d;
            top = Math.Clamp(top, 0d, Math.Max(0d, OperationDropIndicatorCanvas.ActualHeight - indicatorHeight));

            OperationDropIndicator.Width = width;
            OperationDropIndicator.Height = indicatorHeight;
            Canvas.SetLeft(OperationDropIndicator, horizontalPadding);
            Canvas.SetTop(OperationDropIndicator, top);
            OperationDropIndicator.Visibility = Visibility.Visible;
        }

        private void HideOperationDropIndicator()
        {
            if (OperationDropIndicator is not null)
            {
                OperationDropIndicator.Visibility = Visibility.Collapsed;
            }
        }

        #endregion

        #region 抽屉动画

        #region 行内参数编辑

        private void InlineOperationParametersButton_Click(object sender, RoutedEventArgs e)
        {
            CommitEditableDataGrids();

            if ((sender as FrameworkElement)?.DataContext is not WorkStepOperation operation)
            {
                return;
            }

            OperationsDataGrid.SelectedItem = operation;
            if (ViewModel is not null)
            {
                ViewModel.SchemeStepEditor.SelectedOperation = operation;
            }

            InlineParameterDrawerSheet.Tag = new InlineParameterEditState(operation);
            OpenInlineParameterDrawer();
            e.Handled = true;
        }

        private void InlineParameterDrawerBackdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            CloseInlineParameterDrawer();
        }

        private void CloseInlineParameterDrawerButton_Click(object sender, RoutedEventArgs e)
        {
            CloseInlineParameterDrawer();
        }

        private void ApplyInlineParameterDrawerButton_Click(object sender, RoutedEventArgs e)
        {
            InlineInputParameterDataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            InlineInputParameterDataGrid.CommitEdit(DataGridEditingUnit.Row, true);
            InlineReturnParameterDataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            InlineReturnParameterDataGrid.CommitEdit(DataGridEditingUnit.Row, true);

            if (ViewModel is null || InlineParameterDrawerSheet.Tag is not InlineParameterEditState state)
            {
                return;
            }

            state.SanitizeReturnParameterTable();
            ObservableCollection<WorkStepOperationParameter> parameters = state.BuildInputParameters();

            state.TargetOperation.Parameters = parameters;
            state.ApplyReturnParameters();
            state.TargetOperation.AreParametersModified =
                ViewModel.SchemeStepEditor.HasModifiedOperationParameters(state.TargetOperation, parameters);

            CloseInlineParameterDrawer();
            e.Handled = true;
        }

        private void InlineParameterTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ReferenceEquals(e.OriginalSource, sender) ||
                InlineParameterDrawerSheet?.Tag is not InlineParameterEditState state)
            {
                return;
            }

            InlineReturnParameterDataGrid?.CommitEdit(DataGridEditingUnit.Cell, true);
            InlineReturnParameterDataGrid?.CommitEdit(DataGridEditingUnit.Row, true);
            state.SanitizeReturnParameterTable();
        }

        private void OpenInlineParameterDrawer()
        {
            _isInlineParameterDrawerOpen = true;
            UpdateInlineParameterDrawerVisual(animate: true);
        }

        private void CloseInlineParameterDrawer()
        {
            _isInlineParameterDrawerOpen = false;
            UpdateInlineParameterDrawerVisual(animate: true);
        }

        private void UpdateInlineParameterDrawerVisual(bool animate)
        {
            if (InlineParameterDrawerHost is null || InlineParameterDrawerTranslateTransform is null)
            {
                return;
            }

            double targetOpacity = _isInlineParameterDrawerOpen ? 1d : 0d;
            double targetOffset = _isInlineParameterDrawerOpen ? 0d : InlineParameterDrawerClosedOffset;

            if (_isInlineParameterDrawerOpen)
            {
                InlineParameterDrawerHost.IsHitTestVisible = true;
            }

            if (!animate)
            {
                InlineParameterDrawerHost.BeginAnimation(UIElement.OpacityProperty, null);
                InlineParameterDrawerTranslateTransform.BeginAnimation(TranslateTransform.YProperty, null);
                InlineParameterDrawerHost.Opacity = targetOpacity;
                InlineParameterDrawerTranslateTransform.Y = targetOffset;
                InlineParameterDrawerHost.IsHitTestVisible = _isInlineParameterDrawerOpen;
                return;
            }

            DoubleAnimation opacityAnimation = new(targetOpacity, OperationDrawerAnimationDuration)
            {
                EasingFunction = OperationDrawerEasing
            };

            if (!_isInlineParameterDrawerOpen)
            {
                opacityAnimation.Completed += (_, _) =>
                {
                    if (!_isInlineParameterDrawerOpen)
                    {
                        InlineParameterDrawerHost.IsHitTestVisible = false;
                    }
                };
            }

            DoubleAnimation translateAnimation = new(targetOffset, OperationDrawerAnimationDuration)
            {
                EasingFunction = OperationDrawerEasing
            };

            InlineParameterDrawerHost.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
            InlineParameterDrawerTranslateTransform.BeginAnimation(TranslateTransform.YProperty, translateAnimation);
        }

        private sealed class InlineParameterEditState
        {
            public InlineParameterEditState(WorkStepOperation operation)
            {
                TargetOperation = operation;
                OperationTitle = operation.DisplayText;
                OperationSummary = $"{operation.OperationObject}.{operation.InvokeMethod}";
                InputParameterRows = CreateInputParameterRows(operation.Parameters);
                ReturnParameterRows = CreateReturnParameterRows(operation, out IReadOnlyList<string> parsedReturnKeys);
                ParsedReturnKeys = parsedReturnKeys;
                Parameters = new ObservableCollection<WorkStepOperationParameter>(
                    operation.Parameters
                        .OrderBy(parameter => parameter.Sequence)
                        .Select(parameter => parameter.Clone()));
                ParameterSummary = InputParameterRows.Count == 0 ? "无输入参数" : $"{InputParameterRows.Count} 个输入参数";
            }

            public WorkStepOperation TargetOperation { get; }

            public string OperationTitle { get; }

            public string OperationSummary { get; }

            public string ParameterSummary { get; }

            public ObservableCollection<InlineInputParameterRow> InputParameterRows { get; }

            public ObservableCollection<InlineReturnParameterRow> ReturnParameterRows { get; }

            public IReadOnlyList<string> ParsedReturnKeys { get; }

            public ObservableCollection<WorkStepOperationParameter> BuildInputParameters()
            {
                List<WorkStepOperationParameter> parameters = new();
                foreach (InlineInputParameterRow row in InputParameterRows)
                {
                    parameters.Add(new WorkStepOperationParameter
                    {
                        Id = row.Id,
                        Sequence = Math.Max(1, row.Sequence),
                        Name = row.Type,
                        ParameterName = row.ParameterName,
                        Value = row.Value,
                        Remark = row.Description
                    });
                }

                return new ObservableCollection<WorkStepOperationParameter>(
                    parameters
                        .OrderBy(parameter => parameter.Sequence)
                        .Select((parameter, index) =>
                        {
                            parameter.Sequence = index + 1;
                            return parameter;
                        }));
            }

            public void ApplyReturnParameters()
            {
                SanitizeReturnParameterTable();
                List<InlineReturnParameterRow> rows = ReturnParameterRows
                    .Where(item => !IsEmptyReturnParameterRow(item))
                    .Where(IsAllowedReturnParameterRow)
                    .ToList();
                InlineReturnParameterRow? row = rows.FirstOrDefault(item =>
                    !string.IsNullOrWhiteSpace(TargetOperation.ReturnValue) &&
                    string.Equals(
                        item.Key,
                        TargetOperation.ReturnValue.Trim(),
                        StringComparison.OrdinalIgnoreCase)) ??
                    rows.FirstOrDefault(item => item.ShowDataToView) ??
                    (rows.Count == 1 ? rows[0] : null);
                if (row is null)
                {
                    TargetOperation.ReturnValue = string.Empty;
                    TargetOperation.ShowDataToView = false;
                    TargetOperation.ViewDataName = string.Empty;
                    TargetOperation.ViewJudgeType = string.Empty;
                    TargetOperation.ViewJudgeCondition = string.Empty;
                    return;
                }

                TargetOperation.ReturnValue = row.Key;
                TargetOperation.ShowDataToView = row.ShowDataToView;
                TargetOperation.ViewDataName = row.ViewDataName?.Trim() ?? string.Empty;
                TargetOperation.ViewJudgeType = row.ViewJudgeType?.Trim() ?? string.Empty;
                TargetOperation.ViewJudgeCondition = row.ViewJudgeCondition?.Trim() ?? string.Empty;
            }

            public void SanitizeReturnParameterTable()
            {
                HashSet<string> seenKeys = new(StringComparer.OrdinalIgnoreCase);
                List<InlineReturnParameterRow> rowsToRemove = new();
                foreach (InlineReturnParameterRow row in ReturnParameterRows)
                {
                    if (IsEmptyReturnParameterRow(row))
                    {
                        rowsToRemove.Add(row);
                        continue;
                    }

                    string returnValue = row.Key;
                    if (ParsedReturnKeys.Count > 0 &&
                        !ParsedReturnKeys.Any(key => string.Equals(key, returnValue, StringComparison.OrdinalIgnoreCase)))
                    {
                        rowsToRemove.Add(row);
                        continue;
                    }

                    if (!seenKeys.Add(returnValue))
                    {
                        rowsToRemove.Add(row);
                    }
                }

                foreach (InlineReturnParameterRow row in rowsToRemove)
                {
                    ReturnParameterRows.Remove(row);
                }
            }

            public ObservableCollection<WorkStepOperationParameter> Parameters { get; }

            private static ObservableCollection<InlineInputParameterRow> CreateInputParameterRows(IEnumerable<WorkStepOperationParameter> parameters)
            {
                return new ObservableCollection<InlineInputParameterRow>(
                    parameters
                        .OrderBy(parameter => parameter.Sequence)
                        .Select(parameter => new InlineInputParameterRow
                        {
                            Id = parameter.Id,
                            Sequence = parameter.Sequence,
                            Type = parameter.Type,
                            ParameterName = parameter.ParameterName,
                            Value = parameter.Value,
                            Description = parameter.Description
                        }));
            }

            private ObservableCollection<InlineReturnParameterRow> CreateReturnParameterRows(
                WorkStepOperation operation,
                out IReadOnlyList<string> parsedReturnKeys)
            {
                parsedReturnKeys = Array.Empty<string>();
                ObservableCollection<InlineReturnParameterRow> rows = new();

                JsonElement? command = FindProtocolCommand(operation.ProtocolName?.Trim() ?? string.Empty, operation.CommandName?.Trim() ?? string.Empty);
                if (IsSendOnlyProtocolCommand(command))
                {
                    return rows;
                }

                IReadOnlyList<string> parsedKeys = command is null
                    ? Array.Empty<string>()
                    : GetJsonStringArray(command.Value, "ParsedResultKeys");
                parsedReturnKeys = parsedKeys;
                if (parsedKeys.Count > 0)
                {
                    foreach (string parsedKey in parsedKeys)
                    {
                        bool isCurrentReturnValue = string.Equals(parsedKey, operation.ReturnValue, StringComparison.OrdinalIgnoreCase);
                        rows.Add(new InlineReturnParameterRow
                        {
                            Key = parsedKey,
                            ShowDataToView = isCurrentReturnValue && operation.ShowDataToView,
                            ViewDataName = isCurrentReturnValue ? operation.ViewDataName : string.Empty,
                            ViewJudgeType = isCurrentReturnValue ? operation.ViewJudgeType : string.Empty,
                            ViewJudgeCondition = isCurrentReturnValue ? operation.ViewJudgeCondition : string.Empty
                        });
                    }

                    return rows;
                }

                if (!HasReturnParameter(operation))
                {
                    return rows;
                }

                rows.Add(new InlineReturnParameterRow
                {
                    Key = operation.ReturnValue,
                    ShowDataToView = operation.ShowDataToView,
                    ViewDataName = operation.ViewDataName,
                    ViewJudgeType = operation.ViewJudgeType,
                    ViewJudgeCondition = operation.ViewJudgeCondition
                });
                return rows;
            }

            private static bool HasReturnParameter(WorkStepOperation operation)
            {
                return !string.IsNullOrWhiteSpace(operation.ReturnValue) ||
                       operation.ShowDataToView ||
                       !string.IsNullOrWhiteSpace(operation.ViewDataName) ||
                       !string.IsNullOrWhiteSpace(operation.ViewJudgeType) ||
                       !string.IsNullOrWhiteSpace(operation.ViewJudgeCondition);
            }

            private bool IsAllowedReturnParameterRow(InlineReturnParameterRow row)
            {
                if (ParsedReturnKeys.Count == 0)
                {
                    return true;
                }

                string returnValue = row.Key;
                return ParsedReturnKeys.Any(key => string.Equals(key, returnValue, StringComparison.OrdinalIgnoreCase));
            }

            private static bool IsEmptyReturnParameterRow(InlineReturnParameterRow row)
            {
                return string.IsNullOrWhiteSpace(row.Key) &&
                       !row.ShowDataToView &&
                       string.IsNullOrWhiteSpace(row.ViewDataName) &&
                       string.IsNullOrWhiteSpace(row.ViewJudgeType) &&
                       string.IsNullOrWhiteSpace(row.ViewJudgeCondition);
            }

            public sealed class InlineInputParameterRow
            {
                public string Id { get; set; } = string.Empty;

                public int Sequence { get; set; }

                public string Type { get; set; } = string.Empty;

                public string ParameterName { get; set; } = string.Empty;

                public string Value { get; set; } = string.Empty;

                public string Description { get; set; } = string.Empty;
            }

            public sealed class InlineReturnParameterRow
            {
                private string _key = string.Empty;

                public string Key
                {
                    get => _key;
                    set => _key = value?.Trim() ?? string.Empty;
                }

                public bool ShowDataToView { get; set; }

                public string ViewDataName { get; set; } = string.Empty;

                public string ViewJudgeType { get; set; } = string.Empty;

                public string ViewJudgeCondition { get; set; } = string.Empty;
            }

            private static bool IsSendOnlyProtocolCommand(JsonElement? command)
            {
                return command is not null &&
                       !GetJsonBool(command.Value, "WaitForResponse", defaultValue: true) &&
                       !GetJsonBool(command.Value, "IsParseOnly", defaultValue: false);
            }

            private static JsonElement? FindProtocolCommand(string protocolName, string commandName)
            {
                string directory = Path.Combine(AppContext.BaseDirectory, "Config", "Protocol");
                if (!Directory.Exists(directory))
                {
                    return null;
                }

                foreach (string filePath in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        using JsonDocument document = JsonDocument.Parse(ReadPossiblyEncryptedText(filePath));
                        JsonElement root = document.RootElement;
                        if (!string.Equals(GetJsonString(root, "Name"), protocolName, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (root.TryGetProperty("Commands", out JsonElement commandsElement) &&
                            commandsElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (JsonElement commandElement in commandsElement.EnumerateArray())
                            {
                                if (string.Equals(GetJsonString(commandElement, "Name"), commandName, StringComparison.OrdinalIgnoreCase))
                                {
                                    return commandElement.Clone();
                                }
                            }
                        }

                        if (string.Equals(GetJsonString(root, "CommandName"), commandName, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(commandName, "指令 1", StringComparison.OrdinalIgnoreCase))
                        {
                            return root.Clone();
                        }
                    }
                    catch
                    {
                        // Ignore broken protocol files while opening the parameter drawer.
                    }
                }

                return null;
            }

            private static string ReadPossiblyEncryptedText(string filePath)
            {
                string storageText = File.ReadAllText(filePath, Encoding.UTF8);
                try
                {
                    return storageText.DesDecrypt();
                }
                catch
                {
                    return storageText;
                }
            }

            private static string GetJsonString(JsonElement element, string propertyName)
            {
                return element.TryGetProperty(propertyName, out JsonElement propertyElement)
                    ? propertyElement.GetString() ?? string.Empty
                    : string.Empty;
            }

            private static IReadOnlyList<string> GetJsonStringArray(JsonElement element, string propertyName)
            {
                if (!element.TryGetProperty(propertyName, out JsonElement propertyElement) ||
                    propertyElement.ValueKind != JsonValueKind.Array)
                {
                    return Array.Empty<string>();
                }

                return propertyElement
                    .EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString() ?? string.Empty)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

            private static bool GetJsonBool(JsonElement element, string propertyName, bool defaultValue)
            {
                if (!element.TryGetProperty(propertyName, out JsonElement propertyElement))
                {
                    return defaultValue;
                }

                return propertyElement.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.String when bool.TryParse(propertyElement.GetString(), out bool value) => value,
                    _ => defaultValue
                };
            }

        }

        #endregion

        private void OperationDrawerBackdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel?.SchemeStepEditor.CloseOperationDrawerCommand.CanExecute(null) == true)
            {
                ViewModel.SchemeStepEditor.CloseOperationDrawerCommand.Execute(null);
            }
        }

        /// <summary>
        /// 更新步骤编辑抽屉的显示状态。
        /// </summary>
        private void UpdateOperationDrawerVisual(bool animate)
        {
            if (OperationDrawerHost is null || OperationDrawerTranslateTransform is null)
            {
                return;
            }

            bool isOpen = ViewModel?.SchemeStepEditor.IsOperationDrawerOpen == true;
            double targetOpacity = isOpen ? 1d : 0d;
            double targetOffset = isOpen ? 0d : -OperationDrawerClosedOffset;

            if (isOpen)
            {
                OperationDrawerHost.IsHitTestVisible = true;
            }

            if (!animate)
            {
                OperationDrawerHost.BeginAnimation(UIElement.OpacityProperty, null);
                OperationDrawerTranslateTransform.BeginAnimation(TranslateTransform.XProperty, null);
                OperationDrawerHost.Opacity = targetOpacity;
                OperationDrawerTranslateTransform.X = targetOffset;
                OperationDrawerHost.IsHitTestVisible = isOpen;
                return;
            }

            DoubleAnimation opacityAnimation = new(targetOpacity, OperationDrawerAnimationDuration)
            {
                EasingFunction = OperationDrawerEasing
            };

            if (!isOpen)
            {
                opacityAnimation.Completed += (_, _) =>
                {
                    if (ViewModel?.SchemeStepEditor.IsOperationDrawerOpen != true)
                    {
                        OperationDrawerHost.IsHitTestVisible = false;
                    }
                };
            }

            DoubleAnimation translateAnimation = new(targetOffset, OperationDrawerAnimationDuration)
            {
                EasingFunction = OperationDrawerEasing
            };

            OperationDrawerHost.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
            OperationDrawerTranslateTransform.BeginAnimation(TranslateTransform.XProperty, translateAnimation);
        }

        #endregion

        #region 交互辅助方法

        private static bool IsInlineEditableSchemeStepElement(DependencyObject? source)
        {
            return FindAncestor<TextBox>(source) is not null ||
                   FindAncestor<ComboBox>(source) is not null ||
                   FindAncestor<CheckBox>(source) is not null;
        }

        private static bool IsInlineEditableOperationCell(DependencyObject? source)
        {
            if (FindAncestor<ComboBox>(source) is not null ||
                FindAncestor<TextBox>(source) is not null ||
                FindAncestor<Button>(source) is not null)
            {
                return true;
            }

            DataGridCell? cell = FindAncestor<DataGridCell>(source);
            string? bindingPath = null;
            if (cell?.Column is DataGridBoundColumn boundColumn &&
                boundColumn.Binding is Binding binding)
            {
                bindingPath = binding.Path?.Path;
            }
            else if (cell?.Column is DataGridTemplateColumn templateColumn)
            {
                bindingPath = templateColumn.SortMemberPath;
            }

            return string.Equals(bindingPath, nameof(WorkStepOperation.OperationObject), StringComparison.Ordinal) ||
                   string.Equals(bindingPath, nameof(WorkStepOperation.InvokeMethod), StringComparison.Ordinal) ||
                   string.Equals(bindingPath, nameof(WorkStepOperation.AreParametersModified), StringComparison.Ordinal) ||
                   string.Equals(bindingPath, nameof(WorkStepOperation.DelayMilliseconds), StringComparison.Ordinal) ||
                   string.Equals(bindingPath, nameof(WorkStepOperation.Remark), StringComparison.Ordinal);

        }

        private static bool IsOperationSelectionCheckBox(DependencyObject? source)
        {
            return FindAncestor<CheckBox>(source) is not null;
        }

        private static T? FindAncestor<T>(DependencyObject? current)
            where T : DependencyObject
        {
            while (current is not null)
            {
                if (current is T ancestor)
                {
                    return ancestor;
                }

                current = GetParentObject(current);
            }

            return null;
        }

        private static System.Collections.Generic.IEnumerable<T> FindVisualChildren<T>(DependencyObject? root)
            where T : DependencyObject
        {
            if (root is null)
            {
                yield break;
            }

            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int index = 0; index < childCount; index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, index);
                if (child is T typedChild)
                {
                    yield return typedChild;
                }

                foreach (T descendant in FindVisualChildren<T>(child))
                {
                    yield return descendant;
                }
            }
        }

        private static DependencyObject? GetParentObject(DependencyObject source)
        {
            if (source is Visual or System.Windows.Media.Media3D.Visual3D)
            {
                return VisualTreeHelper.GetParent(source);
            }

            if (source is FrameworkContentElement frameworkContentElement)
            {
                return frameworkContentElement.Parent as DependencyObject;
            }

            return null;
        }

        #endregion
    }
}
