using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dragablz;
using Newtonsoft.Json;
using _1RM;
using _1RM.Model.Protocol;
using _1RM.Service;
using _1RM.Service.Locality;
using _1RM.View;
using _1RM.View.Host;
using _1RM.View.Host.ProtocolHosts;
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

sealed class TitleTestHost : HostBase
{
    public TitleTestHost(RDP server) : base(server) { }
    public override void Conn() { }
    public override void ReConn() { }
    public override ProtocolHostType GetProtocolHostType() => ProtocolHostType.Native;
    public override IntPtr GetHostHwnd() => IntPtr.Zero;
}

static class Smoke
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);
    static string NativeTitle(Window window)
    {
        var interop = new WindowInteropHelper(window);
        if (interop.Handle == IntPtr.Zero)
        {
            // Use the normal window initialization path without displaying or activating the test window.
            window.Opacity = 0;
            window.ShowActivated = false;
            window.ShowInTaskbar = false;
            window.Show();
            Pump();
        }
        var text = new StringBuilder(1024);
        GetWindowText(interop.Handle, text, text.Capacity);
        return text.ToString();
    }
    static TabItemViewModel Session(string name)
    {
        var server = new RDP { DisplayName = name };
        return new TabItemViewModel(new TitleTestHost(server), name);
    }
    static void Select(TabWindowView view, TabItemViewModel session)
    {
        view.GetViewModel().Items.Add(session);
        view.GetViewModel().SelectedItem = session;
        Pump();
    }
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
            Check(JsonConvert.DeserializeObject<GeneralConfig>("{}")!.TabWindowShowWindowTitle, "older configuration keeps the 1Remote title suffix visible");
            var main = new MainWindowView(null!, locality, cfg) { DataContext = new MainContext { SettingViewModel = settings } };
            Layout(main, 900, 560);
            var toggle = (CheckBox)main.FindName("CbShowSessionTitleBar");
            var titleToggle = (CheckBox)main.FindName("CbShowSessionWindowTitle");
            Check(toggle.IsChecked == true && !string.IsNullOrEmpty(AutomationProperties.GetName(toggle)), "main title bar contains accessible visibility switch");
            var toolbar = (StackPanel)toggle.Parent;
            int position = toolbar.Children.IndexOf(toggle);
            Check(position > 0 && toolbar.Children[position - 1] is Grid searchGrid && searchGrid.Children.OfType<TextBox>().Any()
                && toolbar.Children[position + 1] == titleToggle
                && toolbar.Children[position + 2] is Grid menuGrid && menuGrid.Children.OfType<CheckBox>().Any(x => x.Name == "CbPopForMenu"),
                "title bar and window title switches are adjacent between search and menu");
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
            Check(titleToggle.IsChecked == true && !string.IsNullOrEmpty(AutomationProperties.GetName(titleToggle)),
                "title suffix switch is accessible and enabled by default");
            Check(BindingOperations.GetBinding(titleToggle, ToggleButton.IsCheckedProperty)!.Mode == BindingMode.TwoWay,
                "title suffix switch uses two-way settings binding");
            var code = Session("code"); var database = Session("database");
            Select(first, code); Select(second, database);
            Check(NativeTitle(first) == "code - 1Remote" && NativeTitle(second) == "database - 1Remote",
                "native Windows captions initially contain the selected session names");
            var fullscreen = (FullScreenWindowView)Activator.CreateInstance(typeof(FullScreenWindowView), true)!;
            var fullscreenSession = Session("fullscreen");
            var fullscreenHost = typeof(FullScreenWindowView).GetProperty(nameof(FullScreenWindowView.Host))!;
            fullscreenHost.SetValue(fullscreen, fullscreenSession.Content); Pump();
            string fullscreenTitle = fullscreenSession.Content.ProtocolServer.DisplayName + " - "
                + fullscreenSession.Content.ProtocolServer.SubTitle;
            Check(NativeTitle(fullscreen) == fullscreenTitle, "fullscreen host initially exposes its original system caption");
            Click(titleToggle);
            Check(first.Title == "code" && second.Title == "database" && NativeTitle(first) == "code" && NativeTitle(second) == "database",
                "title suffix switch changes code - 1Remote to code and preserves session names in all tab windows");
            Check(fullscreen.Title == fullscreenTitle && NativeTitle(fullscreen) == fullscreenTitle,
                "title suffix switch preserves the fullscreen caption without an automatic 1Remote suffix");
            Check(!general.TabWindowShowTitleBar && !Tabs(first).IsHeaderPanelVisible,
                "title suffix switch leaves the independent title bar preference intact");
            Check(!Configuration.Load(AppPathHelper.Instance.ProfileJsonPath)!.General.TabWindowShowWindowTitle,
                "hidden title suffix preference persists to disk");
            Check((string)titleToggle.ToolTip == "显示标题中的 1Remote 后缀", "hidden suffix tooltip describes restoring the 1Remote suffix");
            code.Content.ProtocolServer.DisplayName = "renamed"; Pump();
            Check(code.DisplayName == "renamed" && NativeTitle(first) == "renamed",
                "renaming a session updates its native caption while the suffix stays hidden");
            var switched = Session("switched"); Select(first, switched);
            Check(NativeTitle(first) == "switched", "switching tabs preserves the selected session name with the suffix hidden");
            var fourth = new TabWindowView(); Select(fourth, Session("new session"));
            Check(fourth.Title == "new session" && NativeTitle(fourth) == "new session", "new windows retain session names with the suffix hidden");
            Select(third, Session("custom - 1Remote"));
            Check(NativeTitle(third) == "custom - 1Remote", "hiding removes only the appended suffix and preserves matching text in the session name");
            Layout(main, 900, 560); Preview(main, "window-title-hidden.png");
            Click(titleToggle);
            Check(NativeTitle(first) == "switched - 1Remote" && NativeTitle(second) == "database - 1Remote"
                && NativeTitle(fourth) == "new session - 1Remote" && NativeTitle(third) == "custom - 1Remote - 1Remote",
                "restoring the suffix uses each window's latest selected session and preserves its full name");
            first.GetViewModel().SelectedItem = code; Pump();
            Check(NativeTitle(first) == "renamed - 1Remote", "restoring retains session renames made while hidden");
            Check(NativeTitle(fullscreen) == fullscreenTitle, "restoring the suffix leaves the original fullscreen caption intact");
            Check((string)titleToggle.ToolTip == "隐藏标题中的 1Remote 后缀", "visible suffix tooltip describes hiding the 1Remote suffix");
            Check(Configuration.Load(AppPathHelper.Instance.ProfileJsonPath)!.General.TabWindowShowWindowTitle,
                "restored title suffix preference persists to disk");
            Click(toggle); Pump();
            Check(NativeTitle(first) == "renamed - 1Remote" && general.TabWindowShowWindowTitle,
                "the original title bar switch leaves the title suffix preference unchanged");
            general.TabWindowShowWindowTitle = false; Pump();
            Check(titleToggle.IsChecked == false && NativeTitle(first) == "renamed" && NativeTitle(fullscreen) == fullscreenTitle,
                "external title suffix setting updates the switch and preserves all session names");
            Layout(main, 900, 560); Preview(main, "window-title-visible-header.png");
            fullscreenHost.SetValue(fullscreen, null); fullscreen.Close();
            first.GetViewModel().Dispose(); second.GetViewModel().Dispose(); third.GetViewModel().Dispose(); fourth.GetViewModel().Dispose();
            Console.WriteLine($"SUCCESS: {checks} checks passed"); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
