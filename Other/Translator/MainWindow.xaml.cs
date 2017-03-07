﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Navigation;
using System.Windows.Threading;
using System.Xml.Linq;
using System.Xml.XPath;
using Microsoft.Win32;
using XamlReader = System.Windows.Markup.XamlReader;

namespace Translator
{
    public partial class MainWindow : Window
    {
        private string TempPath => Path.Combine(".", "ScreenToGif", "Resources");

        private readonly List<ResourceDictionary> _resourceList = new List<ResourceDictionary>();
        private ObservableCollection<Translation> _translationList = new ObservableCollection<Translation>();
        private string _resourceTemplate;

        public MainWindow()
        {
            InitializeComponent();
        }

        #region Events

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            StatusBand.Info("Dowloading available translations...");

            if (!Directory.Exists(TempPath))
                Directory.CreateDirectory(TempPath);

            await DownloadResources();

            var languageList = CultureInfo.GetCultures(CultureTypes.AllCultures).Select(x => new Culture { Code = x.IetfLanguageTag, Name = x.EnglishName }).ToList();
            languageList.RemoveAt(0);

            FromComboBox.ItemsSource = languageList;
            ToComboBox.ItemsSource = languageList;

            HeaderLabel.Content = "Translator";
            MainGrid.IsEnabled = true;

            FromComboBox.SelectedValue = "en";

            StatusBand.Hide();
            ToComboBox.Focus();
        }

        private void TutorialHyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start("https://github.com/NickeManarin/ScreenToGif/wiki/Localization");
            }
            catch (Exception ex)
            {
                Dialog.Ok("Translator", "Tutorial", "Error while trying to open the tutorial link");
            }
        }

        private void NewLineHyperlink_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText("&#x0d;");
        }

        private void ComboBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return || e.Key == Key.Enter)
            {
                var aux = sender as UIElement;

                aux?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Left));
            }
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            ShowTranslations();
        }

        private void Itens_GotFocus(object sender, RoutedEventArgs e)
        {
            var ue = e.OriginalSource as TextBox;
            if (ue == null) return;

            ue.Dispatcher.BeginInvoke(DispatcherPriority.Send, (Action)(() => ue.SelectAll()));

            BaseDataGrid.SelectedItem = ((FrameworkElement)sender).DataContext;
        }

        private void Item_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            //Importante: Este evento é utilizado por todos os campos editáveis da DataGrid.

            var source = e.OriginalSource as TextBox;

            if (source == null)
                return;

            //Back, up.
            if (e.Key == Key.Up || (e.Key == Key.Enter || e.Key == Key.Return) && (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)))
            {
                source.MoveFocus(new TraversalRequest(FocusNavigationDirection.Up));
                BaseDataGrid.BeginEdit();

                var current = DataGridHelper.GetDataGridCell(BaseDataGrid.CurrentCell);

                current?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Up));

                e.Handled = true;
                return;
            }

            //Back, left.
            if ((e.Key == Key.Left && (source.CaretIndex == 0 || source.IsReadOnly)) || (e.Key == Key.Tab && (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))))
            {
                source.MoveFocus(new TraversalRequest(FocusNavigationDirection.Left));
                BaseDataGrid.BeginEdit();

                var current = DataGridHelper.GetDataGridCell(BaseDataGrid.CurrentCell);

                current?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Left));

                e.Handled = true;
                return;
            }

            //Next, down.
            if (e.Key == Key.Down || e.Key == Key.Enter || e.Key == Key.Return)
            {
                source.MoveFocus(new TraversalRequest(FocusNavigationDirection.Down));
                BaseDataGrid.BeginEdit();

                var current = DataGridHelper.GetDataGridCell(BaseDataGrid.CurrentCell);

                current?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Down));

                e.Handled = true;
                return;
            }

            //Next, right.
            if ((e.Key == Key.Right && (source.CaretIndex == source.Text.Length - 1 || source.IsReadOnly)) || e.Key == Key.Tab)
            {
                source.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                BaseDataGrid.BeginEdit();

                var current = DataGridHelper.GetDataGridCell(BaseDataGrid.CurrentCell);

                current?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));

                e.Handled = true;
                return;
            }
        }

        private void Load_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = true;
        }

        private void Export_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = ToComboBox.SelectedValue != null && BaseDataGrid.Items.Count > 0;
        }

        private void Load_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                AddExtension = true,
                CheckFileExists = true,
                Title = "Open a Resource Dictionary",
                Filter = "Resource Dictionay (*.xaml)|*.xaml;",
                InitialDirectory = Path.GetFullPath(TempPath)
            };

            var result = ofd.ShowDialog();

            if (!result.HasValue || !result.Value) return;

            //Replaces the special chars.
            var text = File.ReadAllText(ofd.FileName, Encoding.UTF8).Replace("&#", "&amp;#");
            File.WriteAllText(ofd.FileName, text, Encoding.UTF8);

            var dictionary = new ResourceDictionary { Source = new Uri(Path.GetFullPath(ofd.FileName), UriKind.Absolute) };
            _resourceList.Add(dictionary);

            var name = Path.GetFileName(ofd.FileName);
            name = name.Replace("StringResources.", "").Replace(".xaml", "");

            ToComboBox.SelectedValue = name;
            ShowTranslations();
        }

        private async void Export_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            var sfd = new SaveFileDialog
            {
                AddExtension = true,
                Filter = "Resource Dictionary (*.xaml)|*.xaml",
                Title = "Save Resource Dictionary",
                FileName = $"StringResources.{ToComboBox.SelectedValue}.xaml"
            };

            var result = sfd.ShowDialog();

            if (!result.HasValue || !result.Value) return;

            MainGrid.IsEnabled = false;
            StatusBand.Info("Exporting translation...");

            var saved = await Task.Factory.StartNew(() => ExportTranslation(sfd.FileName));

            MainGrid.IsEnabled = true;

            if (saved)
                StatusBand.Info("Translation salved!");
            else
                StatusBand.Hide();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (BaseDataGrid.Items.Count > 0 && !Dialog.Ask("Translator", "Do you really wish to close?", "Don't forget to export your translation, if you started translating but not exported yet."))
                e.Cancel = true;
        }

        #endregion

        #region Methods

        private async Task DownloadResources()
        {
            try
            {
                var request = (HttpWebRequest) WebRequest.Create("https://api.github.com/repos/NickeManarin/ScreenToGif/contents/ScreenToGif/Resources/Localization");
                request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/51.0.2704.79 Safari/537.36 Edge/14.14393";

                var response = (HttpWebResponse) await request.GetResponseAsync();

                using (var resultStream = response.GetResponseStream())
                {
                    if (resultStream == null)
                        return;

                    using (var reader = new StreamReader(resultStream))
                    {
                        var result = reader.ReadToEnd();

                        var jsonReader = JsonReaderWriterFactory.CreateJsonReader(Encoding.UTF8.GetBytes(result),
                            new System.Xml.XmlDictionaryReaderQuotas());

                        var json = await Task<XElement>.Factory.StartNew(() => XElement.Load(jsonReader));

                        foreach (var element in json.XPathSelectElement("/").Elements())
                        {
                            var downloadUrl = element.XPathSelectElement("download_url").Value;
                            var name = element.XPathSelectElement("name").Value;

                            await DownloadFileAsync(new Uri(downloadUrl), name);
                        }

                        CommandManager.InvalidateRequerySuggested();
                    }
                }
            }
            catch (WebException web)
            {
                Dispatcher.Invoke(() => Dialog.Ok("Translator", "Translator - Downloading Resources", web.Message + 
                    Environment.NewLine + "Trying to load files already downloaded."));

                await LoadFilesAsync();
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => Dialog.Ok("Translator", "Translator - Downloading Resources", ex.Message));
            }

            GC.Collect();
        }

        private async Task DownloadFileAsync2(Uri uri, string name)
        {
            try
            {
                var file = Path.Combine(Dispatcher.Invoke(() => TempPath), name);

                if (File.Exists(file))
                    File.Delete(file);

                using (var webClient = new WebClient { Credentials = CredentialCache.DefaultNetworkCredentials })
                    await webClient.DownloadFileTaskAsync(uri, file);

                //Saves the template for later, when exporting the translation.
                if (name.EndsWith("en.xaml"))
                    _resourceTemplate = File.ReadAllText(file);

                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var dictionary = (ResourceDictionary)XamlReader.Load(fs, new ParserContext { XmlSpace = "preserve" });
                    //var dictionary = new ResourceDictionary();
                    dictionary.Source = new Uri(Path.GetFullPath(file), UriKind.Absolute);

                    _resourceList.Add(dictionary);

                    if (name.EndsWith("en.xaml"))
                        Application.Current.Resources.MergedDictionaries.Add(dictionary);
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => Dialog.Ok("Translator", "Translator - Downloading File", ex.Message));
            }
        }

        private async Task DownloadFileAsync(Uri uri, string name)
        {
            try
            {
                var file = Path.Combine(Dispatcher.Invoke(() => TempPath), name);

                if (File.Exists(file))
                    File.Delete(file);

                using (var webClient = new WebClient { Credentials = CredentialCache.DefaultNetworkCredentials })
                    await webClient.DownloadFileTaskAsync(uri, file);

                //Replaces the special chars.
                var text = File.ReadAllText(file, Encoding.UTF8).Replace("&#", "&amp;#");
                File.WriteAllText(file, text, Encoding.UTF8);

                //Saves the template for later, when exporting the translation.
                if (name.EndsWith("en.xaml"))
                    _resourceTemplate = text;

                var dictionary = new ResourceDictionary {Source = new Uri(Path.GetFullPath(file), UriKind.Absolute)};
                
                _resourceList.Add(dictionary);

                //if (name.EndsWith("en.xaml"))
                //    Application.Current.Resources.MergedDictionaries.Add(dictionary);

                //using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(text)))
                //{
                //    var dictionary = (ResourceDictionary)System.Windows.Markup.XamlReader.Load(stream, new ParserContext { XmlSpace = "preserve" });
                //}
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => Dialog.Ok("Translator", "Translator - Downloading File", ex.Message));
            }
        }

        private async Task LoadFilesAsync()
        {
            try
            {
                var files = await Task.Factory.StartNew(() => Directory.EnumerateFiles(TempPath, "*.xaml"));

                foreach (var file in files)
                {
                    //Replaces the special chars.
                    var text = File.ReadAllText(file, Encoding.UTF8).Replace("&#", "&amp;#");
                    File.WriteAllText(file, text, Encoding.UTF8);

                    //Saves the template for later, when exporting the translation.
                    if (file.EndsWith("en.xaml"))
                        _resourceTemplate = text;

                    var dictionary = new ResourceDictionary { Source = new Uri(Path.GetFullPath(file), UriKind.Absolute) };

                    _resourceList.Add(dictionary);
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => Dialog.Ok("Translator", "Translator - Loading Offline File", ex.Message));
            }
        }

        private void ShowTranslations()
        {
            //var baseCulture = FromComboBox.SelectionBoxItem as Culture;
            //var specificCulture = ToComboBox.SelectionBoxItem as Culture;

            var baseCulture = FromComboBox.SelectedValue as string;
            var specificCulture = ToComboBox.SelectedValue as string;
            
            if (baseCulture == null)
            {
                _translationList = null;
                BaseDataGrid.ItemsSource = null;
                return;
            }

            var baseResource = _resourceList.FirstOrDefault(x => x.Source?.OriginalString.EndsWith(baseCulture + ".xaml") ?? false);
            //var baseResource = Application.Current.Resources.MergedDictionaries.FirstOrDefault(x => x.Source?.OriginalString.EndsWith(baseCulture + ".xaml") ?? false);

            if (baseResource == null)
                return;

            if (specificCulture == null)
            {
                _translationList = new ObservableCollection<Translation>(baseResource.Keys.Cast<string>().Select(y => new Translation
                {
                    Key = y,
                    BaseText = (string)baseResource[y]
                }).OrderBy(o => o.Key).ToList());

                BaseDataGrid.ItemsSource = _translationList;
                return;
            }

            var specificResource = _resourceList.LastOrDefault(x => x.Source?.OriginalString.EndsWith(specificCulture + ".xaml") ?? false);

            if (specificResource == null)
            {
                _translationList = new ObservableCollection<Translation>(baseResource.Keys.Cast<string>().Select(y => new Translation
                {
                    Key = y,
                    BaseText = (string)baseResource[y]
                }).OrderBy(o => o.Key).ToList());

                BaseDataGrid.ItemsSource = _translationList;
                return;
            }

            _translationList = new ObservableCollection<Translation>(baseResource.Keys.Cast<string>().Select(y => new Translation
            {
                Key = y,
                BaseText = (string)baseResource[y],
                SpecificText = (string)specificResource[y]
            }).OrderBy(o => o.Key).ToList());

            BaseDataGrid.ItemsSource = _translationList;
        }

        private bool ExportTranslation(string path)
        {
            try
            {
                var lines = _resourceTemplate.Split('\n');

                for (var i = 0; i < lines.Length; i++)
                {
                    var keyIndex = lines[i].IndexOf(":Key=", StringComparison.Ordinal);

                    if (keyIndex == -1)
                        continue;

                    var keyAux = lines[i].Substring(keyIndex + 6);
                    var key = keyAux.Substring(0, keyAux.IndexOf("\"", StringComparison.Ordinal));

                    var translated = _translationList.FirstOrDefault(x => x.Key == key);

                    //"    <s:String x:Key=\"Size\">Size</s:String>"
                    if (string.IsNullOrWhiteSpace(translated?.SpecificText))
                        lines[i] = $"    <!--{lines[i].TrimStart()}-->"; //Comment the line.
                    else
                        lines[i] = $"    <s:String x:Key=\"{key}\">{translated.SpecificText}</s:String>";
                }

                if (File.Exists(path))
                    File.Delete(path);

                File.WriteAllText(path, string.Join(Environment.NewLine, lines), Encoding.UTF8);
                return true;
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => Dialog.Ok("Translator", "Translator - Saving Translation", ex.Message));
                return false;
            }
        }

        #endregion
    }

    internal class Culture
    {
        public string Code { get; set; }
        public string Name { get; set; }
        public string CodeName => Code.PadRight(3) + " - " + Name;
    }

    internal class Translation
    {
        public string Key { get; set; }
        public string BaseText { get; set; }
        public string SpecificText { get; set; }
    }
}
