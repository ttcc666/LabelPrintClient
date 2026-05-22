using System.Collections;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfTextBoxBase = System.Windows.Controls.Primitives.TextBoxBase;

namespace LabelPrintClient.Infrastructure;

public static class SearchableComboBoxBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(SearchableComboBoxBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty SearchMemberPathProperty =
        DependencyProperty.RegisterAttached(
            "SearchMemberPath",
            typeof(string),
            typeof(SearchableComboBoxBehavior),
            new PropertyMetadata(string.Empty));

    private static readonly DependencyProperty StateProperty =
        DependencyProperty.RegisterAttached(
            "State",
            typeof(SearchState),
            typeof(SearchableComboBoxBehavior),
            new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject obj)
    {
        return (bool)obj.GetValue(IsEnabledProperty);
    }

    public static void SetIsEnabled(DependencyObject obj, bool value)
    {
        obj.SetValue(IsEnabledProperty, value);
    }

    public static string GetSearchMemberPath(DependencyObject obj)
    {
        return (string)obj.GetValue(SearchMemberPathProperty);
    }

    public static void SetSearchMemberPath(DependencyObject obj, string value)
    {
        obj.SetValue(SearchMemberPathProperty, value);
    }

    private static SearchState? GetState(DependencyObject obj)
    {
        return obj.GetValue(StateProperty) as SearchState;
    }

    private static void SetState(DependencyObject obj, SearchState? value)
    {
        obj.SetValue(StateProperty, value);
    }

    private static void OnIsEnabledChanged(DependencyObject obj, DependencyPropertyChangedEventArgs e)
    {
        if (obj is not WpfComboBox comboBox)
            return;

        if ((bool)e.NewValue)
        {
            var state = GetState(comboBox);
            if (state != null)
                return;

            state = new SearchState(comboBox);
            SetState(comboBox, state);
            state.Attach();
        }
        else
        {
            var state = GetState(comboBox);
            state?.Detach();
            SetState(comboBox, null);
        }
    }

    private sealed class SearchState
    {
        private readonly WpfComboBox _comboBox;
        private readonly DependencyPropertyDescriptor _itemsSourceDescriptor;
        private readonly TextChangedEventHandler _textChangedHandler;
        private List<object> _allItems = new();
        private object? _committedSelectedItem;
        private bool _isUpdatingItemsSource;
        private bool _isUpdatingSelection;

        public SearchState(WpfComboBox comboBox)
        {
            _comboBox = comboBox;
            _itemsSourceDescriptor = DependencyPropertyDescriptor.FromProperty(
                ItemsControl.ItemsSourceProperty,
                typeof(WpfComboBox));
            _textChangedHandler = OnEditableTextChanged;
        }

        public void Attach()
        {
            _comboBox.IsEditable = true;
            _comboBox.IsTextSearchEnabled = false;
            _comboBox.StaysOpenOnEdit = true;

            var searchPath = GetEffectiveSearchMemberPath();
            if (!string.IsNullOrWhiteSpace(searchPath))
                TextSearch.SetTextPath(_comboBox, searchPath);

            _itemsSourceDescriptor.AddValueChanged(_comboBox, OnItemsSourceChanged);
            _comboBox.SelectionChanged += OnSelectionChanged;
            _comboBox.DropDownOpened += OnDropDownOpened;
            _comboBox.DropDownClosed += OnDropDownClosed;
            _comboBox.PreviewKeyDown += OnPreviewKeyDown;
            _comboBox.AddHandler(WpfTextBoxBase.TextChangedEvent, _textChangedHandler);

            CaptureItems();
            _committedSelectedItem = _comboBox.SelectedItem;
        }

        public void Detach()
        {
            _itemsSourceDescriptor.RemoveValueChanged(_comboBox, OnItemsSourceChanged);
            _comboBox.SelectionChanged -= OnSelectionChanged;
            _comboBox.DropDownOpened -= OnDropDownOpened;
            _comboBox.DropDownClosed -= OnDropDownClosed;
            _comboBox.PreviewKeyDown -= OnPreviewKeyDown;
            _comboBox.RemoveHandler(WpfTextBoxBase.TextChangedEvent, _textChangedHandler);
        }

        private void OnItemsSourceChanged(object? sender, EventArgs e)
        {
            if (_isUpdatingItemsSource)
                return;

            CaptureItems();
            _committedSelectedItem = _comboBox.SelectedItem;
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingSelection)
                return;

            if (_comboBox.SelectedItem != null)
                _committedSelectedItem = _comboBox.SelectedItem;
        }

        private void OnDropDownOpened(object? sender, EventArgs e)
        {
            CaptureItems();
            RestoreFullList(_comboBox.SelectedItem ?? _committedSelectedItem);
            _comboBox.Dispatcher.BeginInvoke(() =>
            {
                var textBox = FindVisualChild<WpfTextBox>(_comboBox);
                textBox?.SelectAll();
            });
        }

        private void OnDropDownClosed(object? sender, EventArgs e)
        {
            var selectedItem = _comboBox.SelectedItem ?? _committedSelectedItem;
            RestoreFullList(selectedItem);
        }

        private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                RestoreFullList(_committedSelectedItem);
                _comboBox.IsDropDownOpen = false;
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.Enter)
            {
                RestoreFullList(_comboBox.SelectedItem ?? _committedSelectedItem);
                _comboBox.IsDropDownOpen = false;
            }
        }

        private void OnEditableTextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_comboBox.IsDropDownOpen ||
                e.OriginalSource is not WpfTextBox textBox)
            {
                return;
            }

            ApplyFilter(textBox.Text);
        }

        private void CaptureItems()
        {
            if (_comboBox.ItemsSource is IEnumerable source)
                _allItems = source.Cast<object>().ToList();
            else
                _allItems = _comboBox.Items.Cast<object>().ToList();
        }

        private void ApplyFilter(string keyword)
        {
            var filteredItems = string.IsNullOrWhiteSpace(keyword)
                ? _allItems.ToList()
                : _allItems.Where(x => Matches(x, keyword)).ToList();

            var selectedItem = _comboBox.SelectedItem ?? _committedSelectedItem;
            if (selectedItem != null &&
                _allItems.Contains(selectedItem) &&
                !filteredItems.Contains(selectedItem))
            {
                filteredItems.Insert(0, selectedItem);
            }

            SetFilteredItems(filteredItems, selectedItem, keepDropDownOpen: true);
        }

        private void RestoreFullList(object? selectedItem)
        {
            SetFilteredItems(_allItems, selectedItem);
        }

        private void SetFilteredItems(IEnumerable<object> items, object? selectedItem, bool keepDropDownOpen = false)
        {
            _isUpdatingItemsSource = true;
            _isUpdatingSelection = true;
            try
            {
                var itemList = items.ToList();
                _comboBox.ItemsSource = itemList;

                if (selectedItem != null && itemList.Contains(selectedItem))
                    _comboBox.SelectedItem = selectedItem;
                else if (selectedItem == null)
                    _comboBox.SelectedItem = null;

                if (keepDropDownOpen && _comboBox.IsKeyboardFocusWithin)
                    _comboBox.IsDropDownOpen = true;
            }
            finally
            {
                _isUpdatingSelection = false;
                _isUpdatingItemsSource = false;
            }
        }

        private bool Matches(object item, string keyword)
        {
            var text = GetItemText(item);
            return text.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        }

        private string GetItemText(object item)
        {
            if (item is ComboBoxItem comboBoxItem)
                return comboBoxItem.Content?.ToString() ?? string.Empty;

            if (item is string text)
                return text;

            var path = GetEffectiveSearchMemberPath();
            if (string.IsNullOrWhiteSpace(path))
                return item.ToString() ?? string.Empty;

            var value = ResolvePropertyPath(item, path);
            return value?.ToString() ?? string.Empty;
        }

        private string GetEffectiveSearchMemberPath()
        {
            var path = GetSearchMemberPath(_comboBox);
            return string.IsNullOrWhiteSpace(path)
                ? _comboBox.DisplayMemberPath
                : path;
        }

        private static object? ResolvePropertyPath(object source, string path)
        {
            object? current = source;
            foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (current == null)
                    return null;

                var property = TypeDescriptor.GetProperties(current)[part];
                current = property?.GetValue(current);
            }

            return current;
        }

        private static T? FindVisualChild<T>(DependencyObject parent)
            where T : DependencyObject
        {
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                if (child is T typedChild)
                    return typedChild;

                var descendant = FindVisualChild<T>(child);
                if (descendant != null)
                    return descendant;
            }

            return null;
        }
    }
}