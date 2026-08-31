using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.Infrastructure;
using PdfCorrectorium.ProjectFormat;

namespace PdfCorrectorium.App;

public partial class App
{
    /// <summary>実際の画面テンプレート、アクセスキー、Tab順序、可変ヒントを検証します。</summary>
    private async Task RunKeyboardTestAsync(MainWindow main, string[] args, int index)
    {
        string? output = null;
        var checks = new List<string>();
        var windows = new List<Window>();
        var originalLanguage = LocalizationService.CurrentLanguage;
        try
        {
            if (args.Length <= index + 1) throw new ArgumentException("A new keyboard-test output directory is required.");
            output = Path.GetFullPath(args[index + 1]);
            if (Directory.Exists(output)) throw new IOException("Use a new output directory.");
            Directory.CreateDirectory(output);
            void Check(bool value, string message)
            {
                if (!value) throw new InvalidOperationException(message);
                checks.Add("PASS: " + message);
            }
            var paths = new ApplicationPaths(StorageMode.Portable, Path.Combine(output, "config"),
                Path.Combine(output, "logs"), Path.Combine(output, "cache"), Path.Combine(output, "work"));
            ApplicationPathResolver.EnsureDirectories(paths);
            var vm = new MainWindowViewModel(new ProjectPackageService(), new PdfPreviewService(), new PdfExportService(),
                new NdlOcrCompanionService(), new DiagnosticLog(paths.LogDirectory), paths, () => { });
            main.DataContext = vm;
            main.ClosePromptOverride = () => MessageBoxResult.No;
            vm.ErrorDialogOverride = (message, error) => throw new InvalidOperationException(message, error);
            var settings = new ApplicationSettingsWindow(new ApplicationSettings(), "Portable", Path.Combine(output, "settings.json"));
            var search = new OcrSearchReplaceWindow(vm);
            var analysis = new PdfImageOptimizationAnalysis(1, 1, 100, 50, .5, 100, 50, "Fixture");
            windows.AddRange([
                settings, search, new DocumentPropertiesWindow(vm), new OcrQualityAnalysisWindow(vm),
                new BatchCharacterAdjustmentWindow(2, 1, [1, 2]), new RepeatedRegionPropagationOptionsWindow(2, 1, [2]),
                new RepeatedRegionCandidateWindow([], new RepeatedRegionPropagationOptions([2], 70, true, RepeatedRegionPropagationMode.ReplaceStructure), vm),
                new DocumentImageOptimizationWindow(new PdfDocumentImageOptimizationAnalysis([analysis], 1, 100, 50)),
                new ImageOptimizationPreviewWindow(BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 255, 255, 255, 255 }, 4), analysis, .1),
                new BatchCharacterAdjustmentProgressWindow(2), new RepeatedRegionSearchProgressWindow(2),
            ]);
            async Task Layout(Window host)
            {
                var root = (FrameworkElement)host.Content;
                var width = double.IsFinite(host.Width) ? host.Width : 640;
                var height = double.IsFinite(host.Height) ? host.Height : 480;
                root.Measure(new Size(width, height));
                root.Arrange(new Rect(0, 0, width, height));
                root.UpdateLayout();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            }
            void Snapshot(Window host, string name)
            {
                var root = (FrameworkElement)host.Content;
                var bitmap = new RenderTargetBitmap((int)host.Width, (int)host.Height, 96, 96, PixelFormats.Pbgra32);
                var visual = new DrawingVisual();
                using (var drawing = visual.RenderOpen())
                {
                    drawing.DrawRectangle(host.Background ?? Brushes.White, null, new Rect(0, 0, host.Width, host.Height));
                    drawing.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, host.Width, host.Height));
                }
                bitmap.Render(visual);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(output, name + ".png"));
                encoder.Save(stream);
            }
            foreach (var language in new[] { "ja-JP", "en-US" })
            {
                LocalizationService.SetLanguage(language);
                Check(LocalizationService.Translate("ファイル(_F)") == (language == "en-US" ? "_File" : "ファイル(_F)"), language + " existing menu mnemonic survives translation.");
                Check(LocalizationService.Translate("表示言語(_L)") == (language == "en-US" ? "Display language(_L)" : "表示言語(_L)"), language + " new label mnemonic survives translation.");
                foreach (var host in windows.Append(main))
                {
                    LocalizationService.Apply(host);
                    var tabs = KeyboardElements(host).OfType<TabControl>().Where(t => t.Items.Count > 0).ToArray();
                    var count = tabs.Length == 0 ? 1 : tabs[0].Items.Count;
                    for (var tab = 0; tab < count; tab++)
                    {
                        if (tabs.Length > 0) tabs[0].SelectedIndex = tab;
                        await Layout(host);
                        CheckSemanticMnemonics(host, Check);
                        var elements = KeyboardElements(host).OfType<FrameworkElement>().ToArray();
                        foreach (var label in elements.OfType<Label>().Where(l => l.Content is string text && text.Contains("(_")))
                            Check(label.Target is Control or UIElement && ReferenceEquals(Window.GetWindow(label.Target), host), $"{host.GetType().Name}/{language}/{tab}: label target {label.Content} resolves in its window.");
                        var keys = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                        foreach (var element in elements.Where(IsKeyboardBranchActive))
                        {
                            var caption = element switch
                            {
                                Label l => l.Content as string,
                                TabItem t => t.Header as string,
                                ButtonBase b => b.Content is AccessText access ? access.Text : b.Content as string,
                                _ => null,
                            };
                            var key = caption is null ? KeyboardAccess.GetKey(element) : Regex.Match(caption, @"_([A-Za-z0-9])").Groups[1].Value;
                            if (string.IsNullOrEmpty(key)) continue;
                            object target = element is Label label ? label.Target : element;
                            if (target is UIElement { IsEnabled: false }) continue;
                            Check(!keys.TryGetValue(key, out var other) || ReferenceEquals(other, target), $"{host.GetType().Name}/{language}/{tab}: mnemonic {key} is unique in the active view.");
                            keys[key] = target;
                        }
                        Check(elements.OfType<Button>().Where(b => b.IsCancel || b.Content is "OK" or "キャンセル" or "Cancel")
                            .All(b => KeyboardAccess.GetKey(b) is null && !(b.Content as string ?? "").Contains("(_")), host.GetType().Name + " OK/Cancel do not receive mnemonics.");
                        if (host == settings || host is DocumentPropertiesWindow || host == search)
                            Snapshot(host, host.GetType().Name + "-" + language + "-" + tab);
                    }
                }
            }
            // Exercise real WPF access-key routing without sending system-wide keyboard input.
            LocalizationService.SetLanguage("ja-JP");
            LocalizationService.Apply(search);
            search.WindowStartupLocation = WindowStartupLocation.Manual;
            search.Left = -20000; search.Top = -20000;
            search.ShowInTaskbar = false; search.ShowActivated = false;
            search.Show();
            await Layout(search);
            var find = (TextBox)search.FindName("SearchTextBox");
            var replace = (TextBox)search.FindName("ReplacementTextBox");
            var scope = PresentationSource.FromVisual(search);
            AccessKeyManager.ProcessKey(scope, "R", false);
            await File.WriteAllTextAsync(Path.Combine(output, "routing.txt"),
                $"R registered: {AccessKeyManager.IsKeyRegistered(scope, "R")}; F registered: {AccessKeyManager.IsKeyRegistered(scope, "F")}; focus: {Keyboard.FocusedElement}; logical: {FocusManager.GetFocusedElement(search)}\n" +
                string.Join("\n", KeyboardElements(search).OfType<Label>().Select(l => $"{l.Content}: visible={l.IsVisible}; target={l.Target}; template={l.Template}; access={string.Join(',', KeyboardElements(l).OfType<AccessText>().Select(a => a.Text))}")));
            Check(replace.IsKeyboardFocusWithin || FocusManager.GetFocusedElement(search) == replace, "Alt+R routes through Label.Target to the replacement input.");
            AccessKeyManager.ProcessKey(scope, "F", false);
            Check(find.IsKeyboardFocusWithin || FocusManager.GetFocusedElement(search) == find, "Alt+F routes to the search input.");
            find.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            Check(ReferenceEquals(FocusManager.GetFocusedElement(search), search.FindName("SearchButton")), "Tab moves from search input to Search button in visual order.");
            ((UIElement)FocusManager.GetFocusedElement(search)).MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous));
            Check(ReferenceEquals(FocusManager.GetFocusedElement(search), find), "Shift+Tab reverses the input/button order.");
            var caseBox = (CheckBox)search.FindName("MatchCaseCheckBox");
            var wasChecked = caseBox.IsChecked;
            AccessKeyManager.ProcessKey(scope, AutomationProperties.GetAccessKey(caseBox)[4..], false);
            Check(caseBox.IsChecked != wasChecked, "A checkbox mnemonic toggles the actual check state.");
            KeyboardAccess.SetKey(replace, "Z");
            Check(AccessKeyManager.IsKeyRegistered(scope, "Z"), "Direct-control access key registers when loaded.");
            find.Focus();
            AccessKeyManager.ProcessKey(scope, "Z", false);
            Check(replace.IsKeyboardFocusWithin, "Direct-control access key focuses its input.");
            replace.IsEnabled = false;
            Check(!AccessKeyManager.IsKeyRegistered(scope, "Z"), "Disabled input is not an access-key target.");
            replace.IsEnabled = true;
            replace.ClearValue(KeyboardAccess.KeyProperty);
            Check(!AccessKeyManager.IsKeyRegistered(scope, "Z"), "Clearing an access key unregisters it.");
            search.Hide();

            settings.WindowStartupLocation = WindowStartupLocation.Manual;
            settings.Left = -20000; settings.Top = -20000;
            settings.ShowInTaskbar = false; settings.ShowActivated = false;
            settings.Show();
            var settingsTabs = KeyboardElements(settings).OfType<TabControl>().First();
            var shortcutTab = (TabItem)settingsTabs.Items[2];
            AccessKeyManager.ProcessKey(PresentationSource.FromVisual(settings), AutomationProperties.GetAccessKey(shortcutTab)[4..], false);
            Check(settingsTabs.SelectedIndex == 2, "Tab-header mnemonic selects the shortcuts tab using the custom template.");
            var capture = (TextBox)settings.FindName("PreviousCharacterShortcutTextBox");
            capture.Focus();
            foreach (var navigationKey in new[] { Key.Tab, Key.Escape, Key.Return })
            {
                var text = capture.Text;
                var input = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(settings), 0, navigationKey);
                typeof(ApplicationSettingsWindow).GetMethod("ShortcutTextBox_OnPreviewKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(settings, [capture, input]);
                Check(!input.Handled && capture.Text == text, navigationKey + " is not captured as a custom shortcut.");
            }
            settingsTabs.SelectedIndex = 3;
            await Layout(settings);
            var lastField = (TextBox)settings.FindName("BackupGenerationCountTextBox");
            lastField.Focus();
            lastField.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            Check(FocusManager.GetFocusedElement(settings) is Button, "Tab leaves the final settings input for the bottom action row.");
            settings.Hide();

            var fixture = Path.Combine(output, "keyboard-source.pdf");
            WriteDocumentUiTestPdf(fixture);
            await vm.LoadPdfForDiagnosticsAsync(fixture);
            main.WindowStartupLocation = WindowStartupLocation.Manual;
            main.Left = -20000; main.Top = -20000; main.ShowActivated = false; main.ShowInTaskbar = false;
            main.Show();
            await Layout(main);
            var keyboardRegion = vm.AddManualOcrRegion(new Rect(20, 20, 140, 30));
            Check(keyboardRegion is not null, "The PDF fixture exposes an OCR region for loaded-view checks.");
            keyboardRegion!.Text = "Keyboard test";
            vm.SetOverlaySelection([keyboardRegion], keyboardRegion);
            foreach (var language in new[] { "ja-JP", "en-US" })
            foreach (var mode in new[] { 0, 1, 2 })
            foreach (var unit in new[] { 0, 1, 2 })
            {
                LocalizationService.SetLanguage(language);
                LocalizationService.Apply(main);
                vm.EditorModeIndex = mode;
                vm.EditUnitIndex = unit;
                foreach (var navigationTab in new[] { 0, 1 })
                {
                    ((TabControl)main.FindName("KeyboardNavigationTabs")).SelectedIndex = navigationTab;
                    await Layout(main);
                    CheckActiveMainMnemonics(main, Check);
                    CheckSemanticMnemonics(main, Check);
                }
            }
            vm.EditorModeIndex = 0;
            vm.EditUnitIndex = 0;
            LocalizationService.SetLanguage("ja-JP");
            LocalizationService.Apply(main);
            await Layout(main);
            // Menu captions/scopes/command bindings are checked below without activating
            // Windows menu mode in the user's foreground application. Hidden-window tests
            // cannot certify the complete physical Alt+F -> O/S/A interaction.
            var toolbar = (ToolBar)main.FindName("MainToolbarPanel");
            toolbar.Items.OfType<Button>().First(b => b.IsEnabled).Focus();
            main.MoveKeyboardPane(false);
            Check(((TabControl)main.FindName("KeyboardNavigationTabs")).IsKeyboardFocusWithin, "F6 moves from the toolbar to the navigation pane.");
            main.MoveKeyboardPane(true);
            Check(toolbar.IsKeyboardFocusWithin, "Shift+F6 returns to the toolbar.");
            main.Hide();

            foreach (var gesture in new[] { "Alt+A", "Alt+D1", "Ctrl+Tab", "Alt+F4", "F6", "Ctrl+C" })
                Check(EditorShortcutService.IsReserved(gesture), gesture + " is reserved for navigation or standard editing.");
            Check(!EditorShortcutService.IsReserved("Ctrl+Shift+Q") && EditorShortcutService.TryNormalize("Alt+Left", out _), "Non-conflicting custom shortcuts and existing defaults remain available.");

            var pairs = new[]
            {
                ("PreviousCharacterShortcut", "PreviousCharacterToolTip"), ("NextCharacterShortcut", "NextCharacterToolTip"),
                ("DecreaseCharacterAdvanceShortcut", "DecreaseCharacterAdvanceToolTip"), ("IncreaseCharacterAdvanceShortcut", "IncreaseCharacterAdvanceToolTip"),
                ("EstimateCharacterAdvancesShortcut", "EstimateCharacterAdvancesToolTip"), ("EstimateCharacterSuffixAdvancesShortcut", "EstimateCharacterSuffixAdvancesToolTip"),
                ("EqualizeCharacterAdvancesShortcut", "EqualizeCharacterAdvancesToolTip"), ("RestoreOriginalCharacterAdvancesShortcut", "RestoreOriginalCharacterAdvancesToolTip"),
            };
            var tooltipButtons = pairs.Select(pair =>
            {
                var button = new Button { DataContext = vm };
                button.SetBinding(FrameworkElement.ToolTipProperty, new Binding(pair.Item2));
                return button;
            }).ToArray();
            foreach (var language in new[] { "ja-JP", "en-US" })
            foreach (var empty in new[] { false, true })
            {
                LocalizationService.SetLanguage(language);
                var custom = new ApplicationSettings { UiLanguage = language, AutoSaveEnabled = false };
                for (var i = 0; i < pairs.Length; i++)
                    typeof(ApplicationSettings).GetProperty(pairs[i].Item1)!.SetValue(custom, empty ? "" : "Ctrl+Shift+F" + (i + 1));
                Check(await vm.ApplyApplicationSettingsAsync(custom), "Custom settings save to isolated test directory.");
                await Layout(main);
                for (var i = 0; i < pairs.Length; i++)
                {
                    var expected = (string)typeof(MainWindowViewModel).GetProperty(pairs[i].Item2)!.GetValue(vm)!;
                    Check(Equals(tooltipButtons[i].ToolTip, expected), pairs[i].Item2 + " binding refreshes immediately.");
                    Check(empty ? !expected.Contains("Ctrl+") : expected.Contains("Ctrl+Shift+F" + (i + 1)), pairs[i].Item2 + " displays the current assignment, not a stale default.");
                    var command = typeof(MainWindowViewModel).GetProperty(pairs[i].Item2.Replace("ToolTip", "Command"))!.GetValue(vm);
                    var actualButtons = KeyboardElements(main).OfType<Button>().Where(b => ReferenceEquals(b.Command, command)).ToArray();
                    Check(actualButtons.Length > 0 && actualButtons.All(b => Equals(b.ToolTip, expected)), pairs[i].Item2 + " is bound on the actual toolbar buttons.");
                    if (empty) Check(expected.Contains(language == "en-US" ? "Unassigned" : "割り当てなし"), pairs[i].Item2 + " localizes the unassigned state.");
                }
            }
            // A live, existing tooltip binding must survive adding its Alt hint.
            var zoom = new Slider { DataContext = vm };
            zoom.SetBinding(FrameworkElement.ToolTipProperty, new Binding(nameof(vm.ZoomDisplay)));
            KeyboardAccess.SetKey(zoom, "Z"); KeyboardAccess.RefreshHint(zoom);
            vm.ZoomPercent = 175;
            BindingOperations.GetMultiBindingExpression(zoom, FrameworkElement.ToolTipProperty)!.UpdateTarget();
            Check(zoom.ToolTip.ToString()!.Contains("175%") && zoom.ToolTip.ToString()!.Contains("Alt+Z"), "Access hint preserves dynamic tooltip binding updates.");

            await File.WriteAllLinesAsync(Path.Combine(output, "checks.txt"), checks);
            _diagnostics?.Write("keyboard-test.passed", $"{checks.Count} checks passed. {output}");
            Shutdown(0);
        }
        catch (Exception error)
        {
            if (output is not null)
            {
                await File.WriteAllLinesAsync(Path.Combine(output, "checks.txt"), checks);
                await File.WriteAllTextAsync(Path.Combine(output, "failure.txt"), error.ToString());
            }
            _diagnostics?.Write("keyboard-test.failed", error.ToString());
            Shutdown(1);
        }
        finally { LocalizationService.SetLanguage(originalLanguage); }
    }

private static string MnemonicOf(FrameworkElement element)
    {
        var caption = element switch
        {
            Label label => label.Content as string,
            HeaderedContentControl header => header.Header as string,
            HeaderedItemsControl header => header.Header as string,
            ButtonBase button => button.Content is AccessText access ? access.Text : button.Content as string,
            _ => null,
        };
        return caption is null ? KeyboardAccess.GetKey(element) ?? "" : Regex.Match(caption, @"_([A-Za-z0-9])").Groups[1].Value.ToUpperInvariant();
    }

    private static void CheckActiveMainMnemonics(MainWindow main, Action<bool, string> check)
    {
        var keys = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in KeyboardElements(main).OfType<FrameworkElement>().Where(IsKeyboardBranchActive))
        {
            if (element is MenuItem menu && ItemsControl.ItemsControlFromItemContainer(menu) is not Menu) continue;
            var key = MnemonicOf(element);
            if (key.Length == 0) continue;
            object target = element is Label label ? label.Target : element;
            if (target is UIElement { IsEnabled: false }) continue;
            check(!keys.TryGetValue(key, out var other) || ReferenceEquals(other, target), "Loaded main view: " + key + " does not conflict with another active control.");
            keys[key] = target;
        }
    }

    private static void CheckSemanticMnemonics(Window host, Action<bool, string> check)
    {
        var elements = KeyboardElements(host).OfType<FrameworkElement>().ToArray();
        void Caption(string original, string key, bool header = false)
        {
            var translated = LocalizationService.StripMnemonic(LocalizationService.Translate(original + "(_" + key + ")"));
            var matches = elements.Where(element =>
            {
                var text = header ? (element as TabItem)?.Header as string : (element as ButtonBase)?.Content as string;
                return text is not null && LocalizationService.StripMnemonic(text) == translated;
            }).ToArray();
            check(matches.Length > 0 && matches.All(element => MnemonicOf(element) == key), host.GetType().Name + ": " + original + " uses semantic mnemonic " + key + ".");
        }
        void Target(string name, string key)
        {
            var target = (FrameworkElement)host.FindName(name);
            var label = elements.OfType<Label>().Single(item => ReferenceEquals(item.Target, target));
            check(MnemonicOf(label) == key && AutomationProperties.GetAccessKey(target) == "Alt+" + key, name + ": label and automation agree on " + key + ".");
        }
        if (host is ApplicationSettingsWindow)
        {
            Caption("保存", "S"); Caption("既定値に戻す", "D");
            Caption("表示", "V", true); Caption("編集", "E", true); Caption("ショートカット", "K", true);
            Caption("保存・復旧", "R", true); Caption("管理", "G", true);
            Caption("取り込み", "I"); Caption("書き出し", "X");
            Caption("登録・更新", "U"); Caption("選択を適用", "A"); Caption("削除", "L");
            Target("WorkspacePresetComboBox", "P"); Target("WorkspacePresetNameTextBox", "N");
            Target("RecentFileLimitTextBox", "C"); Caption("履歴をクリア", "H");
            Target("UiLanguageComboBox", "L"); Target("PreviousCharacterShortcutTextBox", "P"); Target("NextCharacterShortcutTextBox", "N");
        }
        else if (host is DocumentPropertiesWindow)
        {
            Caption("適用", "A"); Caption("概要", "D", true); Caption("セキュリティ", "S", true);
            Caption("フォント", "F", true); Caption("カスタム", "C", true); Caption("詳細設定", "V", true);
            Target("TitleTextBox", "T"); Target("AuthorTextBox", "U"); Target("DocumentLanguageComboBox", "L");
        }
        else if (host is OcrSearchReplaceWindow)
        {
            Caption("検索", "S"); Caption("すべて置換", "A"); Caption("前へ", "P"); Caption("次へ", "N");
            Target("SearchTextBox", "F"); Target("ReplacementTextBox", "R");
        }
        else if (host is RepeatedRegionCandidateWindow)
        {
            Caption("すべて選択", "A"); Caption("すべて解除", "D"); Caption("前の候補", "P"); Caption("次の候補", "N");
        }
        else if (host is DocumentImageOptimizationWindow)
        {
            Caption("すべて選択", "A"); Caption("選択解除", "D"); Caption("選択ページを順に確認", "R");
        }
        else if (host is ImageOptimizationPreviewWindow)
        {
            Caption("すべて置換", "R"); Caption("すべて保持", "K"); Caption("この内容で実行", "E");
        }
        else if (host is OcrQualityAnalysisWindow)
        {
            Caption("文書全体を分析", "A"); Caption("選択箇所へ移動", "G");
        }
        if (host is MainWindow)
        {
            var vm = (MainWindowViewModel)host.DataContext;
            foreach (var (command, key) in new (ICommand, string)[] { (vm.OpenPdfCommand, "O"), (vm.SaveProjectCommand, "S"), (vm.SaveProjectAsCommand, "A") })
            {
                var commandItems = elements.OfType<MenuItem>().Where(item => ReferenceEquals(item.Command, command)).ToArray();
                check(commandItems.Length > 0 && commandItems.All(item => MnemonicOf(item) == key), "Menu key " + key + " is bound to its intended command.");
            }
            var expected = new Dictionary<string, string>
            {
                ["PDFを開く..."] = "O", ["プロジェクトを開く..."] = "P", ["NDLOCR-Liteデータを読み込む..."] = "I",
                ["最近開いたファイル"] = "R",
                ["文書のプロパティ..."] = "D", ["プロジェクトを上書き保存"] = "S", ["プロジェクトを別名で保存..."] = "A",
                ["編集済みPDFを別名で出力..."] = "E", ["終了"] = "X", ["元に戻す"] = "U", ["やり直す"] = "R",
                ["ページを挿入..."] = "I", ["選択ページを削除"] = "D", ["選択ページを左へ90°回転"] = "L", ["選択ページを右へ90°回転"] = "R",
                ["しおりをインポート..."] = "I", ["しおりをエクスポート..."] = "E",
            };
            var menus = elements.OfType<MenuItem>().ToArray();
            foreach (var (caption, key) in expected)
            {
                var translated = LocalizationService.StripMnemonic(LocalizationService.Translate(caption));
                var matches = menus.Where(item => LocalizationService.StripMnemonic(item.Header as string ?? "") == translated).ToArray();
                check(matches.Length > 0 && matches.All(item => MnemonicOf(item) == key), caption + " uses conventional menu key " + key + ".");
            }
            foreach (var menu in menus.Where(item => item.HasItems).Cast<ItemsControl>().Concat(elements.OfType<ContextMenu>()))
            {
                var keys = menu.Items.OfType<MenuItem>().Where(item => item.Header is string).Select(MnemonicOf).Where(key => key.Length > 0).ToArray();
                check(keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() == keys.Length, "Each menu popup has unique mnemonics: " + (menu is MenuItem item ? item.Header : "context menu"));
            }
        }
    }

    private static IEnumerable<DependencyObject> KeyboardElements(DependencyObject root)
    {
        var visited = new HashSet<DependencyObject>();
        var pending = new Stack<DependencyObject>(); pending.Push(root);
        while (pending.TryPop(out var current))
        {
            if (!visited.Add(current)) continue;
            yield return current;
            if (current is FrameworkElement { ContextMenu: { } contextMenu }) pending.Push(contextMenu);
            foreach (var child in LogicalTreeHelper.GetChildren(current).OfType<DependencyObject>()) pending.Push(child);
            if (current is Visual)
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(current); i++) pending.Push(VisualTreeHelper.GetChild(current, i));
        }
    }

    private static bool IsKeyboardBranchActive(FrameworkElement element)
    {
        for (DependencyObject? current = element; current is not null; current = LogicalTreeHelper.GetParent(current))
        {
            if (current is ContextMenu) return false;
            if (current is not Window && current is FrameworkElement { Visibility: not Visibility.Visible }) return false;
            if (current != element && current is TabItem { IsSelected: false }) return false;
        }
        return true;
    }
}
