using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dragablz;
using Newtonsoft.Json;
using _1RM;
using _1RM.Service;
using _1RM.Service.Locality;
using _1RM.View;
using _1RM.View.Host;
using _1RM.View.Settings;
using _1RM.View.Settings.General;

sealed class MainContext
{
    public required SettingsPageViewModel SettingViewModel { get; init; }
    public object? EditorViewModel => null;
    public object? TopLevelViewModel => null;
    public string MainFilterString { get; set; } = "";
    public bool MainFilterIsFocused { get; set; }
    public int MainFilterCaretIndex { get; set; }
    public bool ShowSetting => false;
}

static class Smoke
{
    static int checks;
    static void Check(bool ok, string name)
    {
        if (!ok) throw new Exception("FAILED: " + name);
        checks++; Console.WriteLine("PASS: " + name);
    }
    static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    static void Layout(Window view, double width = 800, double height = 600)
    {
        Pump(); var grid = (Grid)view.Content;
        grid.Measure(new Size(width, height)); grid.Arrange(new Rect(0, 0, width, height)); grid.UpdateLayout(); Pump();
    }
    static void Click(CheckBox button)
    {
        // Exercise the control's own toggle behavior and its actual two-way binding.
        typeof(ToggleButton).GetMethod("OnToggle", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
        Pump();
    }
    static TabablzControl Tabs(TabWindowView view) => ((Grid)view.Content).Children.OfType<TabablzControl>().Single();
    static StackPanel Buttons(TabWindowView view) => ((Grid)view.Content).Children.OfType<StackPanel>().Single();
    static Border ContentPanel(TabWindowView view) => (Border)Tabs(view).Template.FindName("contentPanel", Tabs(view));
    static void Preview(MainWindowView view, string path)
    {
        var bitmap = new RenderTargetBitmap(1350, 840, 144, 144, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render((Grid)view.Content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(new CroppedBitmap(bitmap, new Int32Rect(0, 0, 1350, 64))));
        using var file = File.Create(path); encoder.Save(file);
    }
    [STAThread] static int Main()
    {
        try
        {
            var app = new Application();
            app.Resources[Stylet.Xaml.View.ViewManagerResourceKey] = new Stylet.ViewManager(new Stylet.ViewManagerConfig
            {
                ViewFactory = type => Activator.CreateInstance(type)!,
                ViewAssemblies = new[] { typeof(TabWindowView).Assembly }.ToList()
            });
            foreach (var uri in new[] {
                "pack://application:,,,/Shawn.Utils.WpfResources;component/Converter/Converter.xaml",
                "pack://application:,,,/Shawn.Utils.WpfResources;component/Theme/Basic/Default.xaml",
                "pack://application:,,,/Shawn.Utils.WpfResources;component/Theme/DefaultTheme.xaml",
                "pack://application:,,,/1Remote;component/Resources/Converter/Converter.xaml",
                "pack://application:,,,/1Remote;component/Resources/Icons/SVG.xaml",
                "pack://application:,,,/1Remote;component/Resources/Languages/en-us.xaml",
                "pack://application:,,,/1Remote;component/Resources/Languages/zh-cn.xaml" })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(uri) });
            var data = Path.Combine(Environment.CurrentDirectory, "profile"); Directory.CreateDirectory(data);
            AppPathHelper.Instance = new AppPathHelper(data, data);
            var cfg = new ConfigurationService(new KeywordMatchService(), new Configuration());
            var general = new GeneralSettingViewModel(cfg, null!);
            var settings = new SettingsPageViewModel(null!, null!);
            // The settings page is outside the header test; keep its view empty.
            ((Stylet.IViewAware)settings).AttachView(new UserControl());
            var locality = new LocalityService();
            IoC.GetByType = (type, key) => type == typeof(ConfigurationService) ? cfg :
                type == typeof(GeneralSettingViewModel) ? general :
                type == typeof(SettingsPageViewModel) ? settings :
                type == typeof(LocalityService) ? locality : null;
            Check(JsonConvert.DeserializeObject<GeneralConfig>("{}")!.TabWindowShowTitleBar, "older configuration keeps title bar visible");
            var main = new MainWindowView(null!, locality, cfg) { DataContext = new MainContext { SettingViewModel = settings } };
            Layout(main, 900, 560);
            var toggle = (CheckBox)main.FindName("CbShowSessionTitleBar");
            Check(toggle.IsChecked == true && !string.IsNullOrEmpty(AutomationProperties.GetName(toggle)), "main title bar contains accessible visibility switch");
            var toolbar = (StackPanel)toggle.Parent;
            int position = toolbar.Children.IndexOf(toggle);
            Check(position > 0 && toolbar.Children[position - 1] is Grid searchGrid && searchGrid.Children.OfType<TextBox>().Any()
                && toolbar.Children[position + 1] is Grid menuGrid && menuGrid.Children.OfType<CheckBox>().Any(x => x.Name == "CbPopForMenu"),
                "switch is immediately after search and before menu");
            Check(BindingOperations.GetBinding(toggle, ToggleButton.IsCheckedProperty)!.Mode == BindingMode.TwoWay, "switch uses two-way settings binding");
            var first = new TabWindowView(); var second = new TabWindowView(); Layout(first); Layout(second);
            Check(Tabs(first).IsHeaderPanelVisible && Tabs(second).IsHeaderPanelVisible, "both session windows start with title bars");
            double before = ContentPanel(first).ActualHeight;
            Click(toggle); Layout(first); Layout(second); Layout(main, 900, 560);
            Check(!Tabs(first).IsHeaderPanelVisible && !Tabs(second).IsHeaderPanelVisible, "main switch hides title bars in both session windows");
            Check(Buttons(first).Visibility == Visibility.Collapsed && Buttons(second).Visibility == Visibility.Collapsed, "main switch also hides session window buttons");
            double after = ContentPanel(first).ActualHeight;
            Console.WriteLine($"Content height: {before} -> {after}");
            Check(Math.Abs(after - before - 30) < 0.01, "content gains the full 30 pixel title bar height");
            Check(first.WindowState == WindowState.Normal, "hiding keeps ordinary window mode");
            Check(!Configuration.Load(AppPathHelper.Instance.ProfileJsonPath)!.General.TabWindowShowTitleBar, "hidden preference persists to disk");
            Check(toggle.Visibility == Visibility.Visible && toggle.IsEnabled, "main switch remains available while session title bars are hidden");
            Check((string)toggle.ToolTip == "显示会话窗口标题栏", "hidden state tooltip describes show action without shortcut");
            Preview(main, "titlebar-hidden.png");
            var third = new TabWindowView(); Layout(third);
            Check(!Tabs(third).IsHeaderPanelVisible && Buttons(third).Visibility == Visibility.Collapsed, "new sessions inherit hidden preference");
            Click(toggle); Layout(first); Layout(second); Layout(third); Layout(main, 900, 560);
            Check(Tabs(first).IsHeaderPanelVisible && Tabs(second).IsHeaderPanelVisible && Tabs(third).IsHeaderPanelVisible
                && Buttons(first).Visibility == Visibility.Visible, "main switch restores title bars and buttons in all sessions");
            Check(Math.Abs(ContentPanel(first).ActualHeight - before) < 0.01, "restoring recovers original content layout");
            Check((string)toggle.ToolTip == "隐藏会话窗口标题栏", "visible state tooltip describes hide action without shortcut");
            Preview(main, "titlebar-visible.png");
            general.TabWindowShowTitleBar = false; Pump();
            Check(toggle.IsChecked == false, "external setting changes update main switch state");
            Check(!first.InputBindings.OfType<KeyBinding>().Any(x => x.Key == Key.F11 && x.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
                && typeof(TabWindowView).GetMethod("RegisterTitleBarHotkey", BindingFlags.Instance | BindingFlags.NonPublic) == null,
                "title bar keyboard shortcut and native registration are removed");
            first.GetViewModel().Dispose(); second.GetViewModel().Dispose(); third.GetViewModel().Dispose();
            Console.WriteLine($"SUCCESS: {checks} checks passed"); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
