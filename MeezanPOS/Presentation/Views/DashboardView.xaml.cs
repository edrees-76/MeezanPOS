using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MeezanPOS.Application.ViewModels;
using MeezanPOS.Domain.Entities;

namespace MeezanPOS.Presentation.Views;

public partial class DashboardView : UserControl
{
    private readonly HashSet<DashboardAlert> _dismissedAlerts = new();
    private DashboardViewModel? _viewModel;

    public DashboardView()
    {
        InitializeComponent();
        Loaded += DashboardView_Loaded;
        Unloaded += DashboardView_Unloaded;
    }

    private void DashboardView_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is DashboardViewModel vm)
        {
            _viewModel = vm;
            _viewModel.Alerts.CollectionChanged += Alerts_CollectionChanged;
        }
    }

    private void DashboardView_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.Alerts.CollectionChanged -= Alerts_CollectionChanged;
            _viewModel.StopAutoRefresh();
        }
    }

    private void Alerts_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // When collection is cleared (refresh occurs), reset local dismissed alerts
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            _dismissedAlerts.Clear();
            var viewSource = TryFindResource("AlertsViewSource") as CollectionViewSource;
            viewSource?.View?.Refresh();
        }
    }

    private void AlertsViewSource_Filter(object sender, FilterEventArgs e)
    {
        if (e.Item is DashboardAlert alert)
        {
            e.Accepted = !_dismissedAlerts.Contains(alert);
        }
    }

    private void DismissAlert_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is DashboardAlert alert)
        {
            _dismissedAlerts.Add(alert);
            var viewSource = TryFindResource("AlertsViewSource") as CollectionViewSource;
            viewSource?.View?.Refresh();
        }
    }

    private void ExportPdf_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is DashboardViewModel vm)
        {
            if (vm.ExportPdfCommand.CanExecute(null))
            {
                vm.ExportPdfCommand.Execute(null);
            }
        }
    }
}
