using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using System.Windows.Input;
using WBImageViewer.Models;
using WBImageViewer.Services;
using MessageBox = System.Windows.MessageBox;

namespace WBImageViewer.Views;

/// <summary>
/// Codex++-style supplier centre: several providers grouped by name, a checkbox-driven
/// model catalogue fetched from upstream, and independent translation / OCR defaults.
/// </summary>
public partial class AiProviderWindow : Window
{
    private sealed class RouteOption
    {
        public string BaseUrl { get; init; } = "";
        public List<AiModelEntry> Models { get; init; } = new();
        public string Label => $"{BaseUrl}   ·   {Models.Count} 个模型（双击采用）";
    }

    private sealed class SupplierVm : INotifyPropertyChanged
    {
        public AiProvider Provider { get; }
        public SupplierVm(AiProvider provider) => Provider = provider;

        public string Name
        {
            get => Provider.Name;
            set { Provider.Name = value; On(nameof(Name)); }
        }

        public string ProtocolText => Provider.Protocol == AiApiProtocol.Responses ? "Responses" : "Chat Completions";
        public string ModelCountText => $"{Provider.Models.Count} 个模型";
        public bool Enabled => Provider.Enabled;

        public void Refresh()
        {
            On(nameof(Name));
            On(nameof(ProtocolText));
            On(nameof(ModelCountText));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void On(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private readonly AiProviderConfig _config;
    private readonly ObservableCollection<SupplierVm> _suppliers = new();
    private bool _loading;

    public AiProviderWindow()
    {
        InitializeComponent();
        TopmostCoordinator.Register(this);
        _config = App.Settings.AiConfig.Clone();
        // Persisted models are already the checked subset; Selected is UI-only and
        // deliberately not serialised, so restore it on load.
        foreach (var provider in _config.Providers)
            foreach (var model in provider.Models) model.Selected = true;

        ProviderList.ItemsSource = _suppliers;
        foreach (var provider in _config.Providers) _suppliers.Add(new SupplierVm(provider));

        ProtocolBox.SelectedIndex = 0;
        ProviderEnabledBox.IsChecked = true;
        TargetLanguageBox.Text = _config.TargetLanguage;

        RefreshDefaultProviders();
        if (_suppliers.Count > 0) ProviderList.SelectedIndex = 0;
        else LoadEditor(null);
    }

    private AiProvider? Current => (ProviderList.SelectedItem as SupplierVm)?.Provider;

    // ---------------- editor sync ----------------

    private void LoadEditor(AiProvider? provider)
    {
        _loading = true;
        ProviderNameBox.Text = provider?.Name ?? "";
        BaseUrlBox.Text = provider?.BaseUrl ?? "";
        ApiKeyBox.Password = provider?.ApiKey ?? "";
        ProviderEnabledBox.IsChecked = provider?.Enabled ?? true;
        ProtocolBox.SelectedIndex = provider?.Protocol == AiApiProtocol.Responses ? 1 : 0;

        ModelList.ItemsSource = provider?.Models ?? new List<AiModelEntry>();
        RefreshTestModels(provider);
        _loading = false;
    }

    private void FlushEditor()
    {
        if (_loading) return;
        var provider = Current;
        if (provider == null) return;

        provider.Name = ProviderNameBox.Text.Trim();
        provider.BaseUrl = BaseUrlBox.Text.Trim();
        provider.ApiKey = ApiKeyBox.Password;
        provider.Enabled = ProviderEnabledBox.IsChecked == true;
        provider.Protocol = ProtocolBox.SelectedIndex == 1 ? AiApiProtocol.Responses : AiApiProtocol.ChatCompletions;
        provider.Models = ModelList.ItemsSource?.Cast<AiModelEntry>().ToList() ?? new List<AiModelEntry>();
        (ProviderList.SelectedItem as SupplierVm)?.Refresh();
    }

    private void ProviderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        foreach (var removed in e.RemovedItems.OfType<SupplierVm>()) removed.Refresh();
        LoadEditor(Current);
    }

    private void ProviderField_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        FlushEditor();
    }

    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        FlushEditor();
    }

    // ---------------- suppliers ----------------

    private void AddProvider_Click(object sender, RoutedEventArgs e)
    {
        FlushEditor();
        var provider = new AiProvider { Name = $"新供应商 {_config.Providers.Count + 1}" };
        _config.Providers.Add(provider);
        _suppliers.Add(new SupplierVm(provider));
        ProviderList.SelectedItem = _suppliers.Last();
        RefreshDefaultProviders();
        ProviderNameBox.Focus();
        ProviderNameBox.SelectAll();
    }

    private void DuplicateProvider_Click(object sender, RoutedEventArgs e)
    {
        var source = Current;
        if (source == null) return;
        FlushEditor();
        var copy = source.Clone();
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name = source.Name + " 副本";
        copy.Models = source.Models.Select(m => new AiModelEntry { Id = m.Id, DisplayName = m.DisplayName, Selected = m.Selected }).ToList();
        _config.Providers.Add(copy);
        _suppliers.Add(new SupplierVm(copy));
        ProviderList.SelectedItem = _suppliers.Last();
        RefreshDefaultProviders();
    }

    private void RemoveProvider_Click(object sender, RoutedEventArgs e)
    {
        var provider = Current;
        if (provider == null) return;
        if (MessageBox.Show($"确定删除供应商“{provider.Name}”吗？", "删除供应商",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        var index = ProviderList.SelectedIndex;
        _config.Providers.Remove(provider);
        _suppliers.RemoveAt(index);
        if (string.Equals(_config.TranslateProviderId, provider.Id, StringComparison.Ordinal))
            _config.TranslateProviderId = _config.TranslateModel = "";
        if (string.Equals(_config.OcrProviderId, provider.Id, StringComparison.Ordinal))
            _config.OcrProviderId = _config.OcrModel = "";
        ProviderList.SelectedIndex = Math.Min(index, _suppliers.Count - 1);
        RefreshDefaultProviders();
    }

    // ---------------- models ----------------

    private void AddModel_Click(object sender, RoutedEventArgs e)
    {
        var provider = Current;
        if (provider == null) return;
        FlushEditor();
        var id = NewModelBox.Text.Trim();
        if (id.Length == 0) return;
        if (provider.Models.Any(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("该模型已在列表中。", "添加模型", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        provider.Models.Add(new AiModelEntry { Id = id, DisplayName = id, Selected = true });
        NewModelBox.Text = "";
        ModelList.ItemsSource = provider.Models.ToList();
        RefreshTestModels(provider);
        (ProviderList.SelectedItem as SupplierVm)?.Refresh();
        RefreshDefaultModelBoxes();
    }

    private async void FetchModels_Click(object sender, RoutedEventArgs e)
    {
        var provider = Current;
        if (provider == null) return;
        FlushEditor();
        if (string.IsNullOrWhiteSpace(provider.BaseUrl))
        {
            MessageBox.Show("请先填写调用地址。", "从上游获取模型", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var button = (Button)sender;
        var original = button.Content;
        button.IsEnabled = false;
        button.Content = "获取中…";
        try
        {
            var fetched = await AiService.FetchModelsAsync(provider.BaseUrl, provider.ApiKey).ConfigureAwait(true);
            var selectedIds = provider.Models.Where(m => m.Selected)
                .Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var knownIds = provider.Models.Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var model in fetched)
            {
                model.Selected = selectedIds.Contains(model.Id);
                model.DisplayName = model.Id;
            }
            // Keep manually added models that upstream did not report.
            foreach (var model in provider.Models.Where(m => !knownIds.Contains(m.Id) ||
                                                            fetched.All(f => f.Id != m.Id)))
                fetched.Add(model);

            provider.Models = fetched;
            ModelList.ItemsSource = fetched.ToList();
            RefreshTestModels(provider);
            (ProviderList.SelectedItem as SupplierVm)?.Refresh();
            RefreshDefaultModelBoxes();
            TestResultText.Text = $"已从上游获取 {fetched.Count} 个模型，请勾选需要使用的模型。";
            TestResultText.Foreground = System.Windows.Media.Brushes.SeaGreen;
        }
        catch (Exception ex)
        {
            TestResultText.Text = "获取失败：" + ex.Message;
            TestResultText.Foreground = System.Windows.Media.Brushes.Firebrick;
        }
        finally
        {
            button.IsEnabled = true;
            button.Content = original;
        }
    }

    private void RefreshTestModels(AiProvider? provider)
    {
        TestModelBox.ItemsSource = provider?.Models.Select(m => m.Id).ToList() ?? new List<string>();
        if (provider != null && TestModelBox.SelectedIndex < 0)
        {
            var first = provider.Models.FirstOrDefault(m => m.Selected) ?? provider.Models.FirstOrDefault();
            if (first != null) TestModelBox.SelectedValue = first.Id;
        }
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        var provider = Current;
        if (provider == null) return;
        FlushEditor();
        var model = TestModelBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(model))
            model = provider.Models.FirstOrDefault(m => m.Selected)?.Id ?? "";
        if (model.Length == 0)
        {
            TestResultText.Text = "请先选择或输入测试模型。";
            TestResultText.Foreground = System.Windows.Media.Brushes.DarkOrange;
            return;
        }

        var button = (Button)sender;
        var original = button.Content;
        button.IsEnabled = false;
        button.Content = "测试中…";
        TestResultText.Text = "正在连接…";
        try
        {
            var result = await AiService.TestAsync(provider, model).ConfigureAwait(true);
            TestResultText.Text = result.Message;
            TestResultText.Foreground = result.Ok
                ? System.Windows.Media.Brushes.SeaGreen
                : System.Windows.Media.Brushes.Firebrick;
        }
        finally
        {
            button.IsEnabled = true;
            button.Content = original;
        }
    }

    private async void CodexRoute_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        var original = button.Content;
        button.IsEnabled = false;
        button.Content = "探测本地路由…";
        RouteHint.Text = "正在探测本机 8317 / 8318 / 1234 / 11434 / 8080 / 8000 / 3000 端口…";
        try
        {
            var routes = await AiService.ProbeLocalRoutesAsync().ConfigureAwait(true);
            if (routes.Count == 0)
            {
                RouteList.Visibility = Visibility.Collapsed;
                RouteHint.Text = "未发现可用的本地路由。请确认 Codex++ / CliProxyApi 已启动，或在调用地址中手动填写 http://127.0.0.1:8317/v1。";
                return;
            }
            RouteList.ItemsSource = routes.Select(r => new RouteOption { BaseUrl = r.BaseUrl, Models = r.Models }).ToList();
            RouteList.Visibility = Visibility.Visible;
            RouteHint.Text = $"发现 {routes.Count} 个可用路由，双击即可创建供应商：";
        }
        catch (Exception ex)
        {
            RouteHint.Text = "探测失败：" + ex.Message;
        }
        finally
        {
            button.IsEnabled = true;
            button.Content = original;
        }
    }

    private void RouteList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RouteList.SelectedItem is not RouteOption option) return;
        FlushEditor();
        var provider = Current ?? new AiProvider();
        if (Current == null)
        {
            _config.Providers.Add(provider);
            _suppliers.Add(new SupplierVm(provider));
        }
        provider.Name = "Codex++ 本地路由";
        provider.Protocol = AiApiProtocol.ChatCompletions;
        provider.BaseUrl = option.BaseUrl;
        provider.ApiKey = "";
        provider.Enabled = true;
        provider.Models = option.Models.Select(m => new AiModelEntry { Id = m.Id, DisplayName = m.Id, Selected = true }).ToList();

        if (Current == null) ProviderList.SelectedItem = _suppliers.Last();
        (ProviderList.SelectedItem as SupplierVm)?.Refresh();
        LoadEditor(provider);
        RefreshDefaultProviders();
        TestResultText.Text = $"已对接 {option.BaseUrl}，拉取 {provider.Models.Count} 个模型。若路由需要密钥，请填写 API 密钥。";
        TestResultText.Foreground = System.Windows.Media.Brushes.SeaGreen;
    }

    // ---------------- defaults ----------------

    private void RefreshDefaultProviders()
    {
        var enabled = _config.Providers.Where(p => p.Enabled).ToList();
        var translateId = _config.TranslateProviderId;
        var ocrId = _config.OcrProviderId;

        _loading = true;
        TranslateProviderBox.ItemsSource = enabled;
        OcrProviderBox.ItemsSource = enabled;
        TranslateProviderBox.SelectedItem = enabled.FirstOrDefault(p => p.Id == translateId);
        OcrProviderBox.SelectedItem = enabled.FirstOrDefault(p => p.Id == ocrId);
        _loading = false;
        RefreshDefaultModelBoxes();
    }

    private void DefaultBinding_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        RefreshDefaultModelBoxes();
    }

    private void RefreshDefaultModelBoxes()
    {
        _loading = true;
        var translate = TranslateProviderBox.SelectedItem as AiProvider;
        var ocr = OcrProviderBox.SelectedItem as AiProvider;
        TranslateModelBox.ItemsSource = translate?.Models.Where(m => m.Selected).Select(m => m.Id).ToList() ?? new List<string>();
        OcrModelBox.ItemsSource = ocr?.Models.Where(m => m.Selected).Select(m => m.Id).ToList() ?? new List<string>();
        TranslateModelBox.Text = _config.TranslateModel;
        OcrModelBox.Text = _config.OcrModel;
        _loading = false;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        FlushEditor();

        foreach (var provider in _config.Providers)
            provider.Models = provider.Models.Where(m => m.Selected).ToList();

        _config.TranslateProviderId = (TranslateProviderBox.SelectedItem as AiProvider)?.Id ?? "";
        _config.OcrProviderId = (OcrProviderBox.SelectedItem as AiProvider)?.Id ?? "";
        _config.TranslateModel = TranslateModelBox.Text.Trim();
        _config.OcrModel = OcrModelBox.Text.Trim();
        _config.TargetLanguage = string.IsNullOrWhiteSpace(TargetLanguageBox.Text) ? "简体中文" : TargetLanguageBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(_config.TranslateProviderId) || string.IsNullOrWhiteSpace(_config.TranslateModel))
        {
            MessageBox.Show("请为“翻译”指定默认供应商与模型。", "保存配置", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        App.ApplyAiConfig(_config);
        DialogResult = true;
        Close();
    }
}
