﻿using Microsoft.Win32;
using PasswordManager.Repository;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PasswordManager
{
    public partial class MainWindow : Window
    {
        private int timeSecAutoHidePassword = 30; // hide all passwords after 30 seconds, 0 to disable
        private int timeSecReenterPassword = 300; // reenter password after 5 minutes being idle, 0 to disable

        private PasswordRepository passwordRepository;
        private string passwordFilename;
        private SecureString securePassword;
        private ThumbnailCache thumbnailCache;
        private KeyDirectoryCache keyDirectoryCache;

        private DispatcherTimer timer;
        private DateTime dateTimeShowPassword;
        private bool autoHidePassword = false;
        private DateTime dateTimeIdle = DateTime.Now;
        private bool reenterPassword = false;


        private static BitmapImage imageShow32x32 = new BitmapImage(new Uri("pack://application:,,,/Images/32x32/document-decrypt-3.png"));
        private static BitmapImage imageHide32x32 = new BitmapImage(new Uri("pack://application:,,,/Images/32x32/document-encrypt-3.png"));
        private static BitmapImage imageKey16x16 = new BitmapImage(new Uri("pack://application:,,,/Images/16x16/kgpg_info.png"));
        private static BitmapImage imageShow16x16 = new BitmapImage(new Uri("pack://application:,,,/Images/16x16/document-decrypt-3.png"));
        private static BitmapImage imageHide16x16 = new BitmapImage(new Uri("pack://application:,,,/Images/16x16/document-encrypt-3.png"));
        private static Image menuItemImageHide;
        private static Image menuItemImageShow;
        private static Image menuItemImageShowDisabled;
        private static Image contextMenuItemImageHide;
        private static Image contextMenuItemImageShow;

        public MainWindow()
        {
            InitializeComponent();
            timer = new DispatcherTimer();
            timer.Tick += new EventHandler(OnTimer);
            timer.Interval = new TimeSpan(0, 0, 1);
            timer.Start();
        }

        private void OnTimer(object obj, EventArgs args)
        {
            try
            {
                UpdateStatus();
                if (autoHidePassword && timeSecAutoHidePassword > 0)
                {
                    var ts = DateTime.Now - dateTimeShowPassword;
                    if (ts.TotalSeconds > timeSecAutoHidePassword)
                    {
                        autoHidePassword = false;
                        foreach (PasswordViewItem item in listView.Items)
                        {
                            item.HidePassword = true;
                        }
                        listView.Items.Refresh();
                        UpdateControls();
                    }
                }
                if (!reenterPassword && timeSecReenterPassword > 0)
                {
                    var tsidle = DateTime.Now - dateTimeIdle;
                    if (tsidle.TotalSeconds > timeSecReenterPassword)
                    {
                        reenterPassword = true;
                    }
                }
            }
            catch (Exception)
            {
                // ignored
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                Init();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                Close();
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            bool canceled = false;
            if (PromptSaveChanges(ref canceled))
            {
                if (!canceled)
                {
                    try
                    {
                        Properties.Settings.Default.Save();
                        if (thumbnailCache != null)
                        {
                            thumbnailCache.Save();
                        }
                        if (keyDirectoryCache != null)
                        {
                            keyDirectoryCache.Save();
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message);
                    }
                    return;
                }
            }
            e.Cancel = true;
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            dateTimeIdle = DateTime.Now;
        }

        private void ListView_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            listViewToggleShowPassword.Header = Properties.Resources.CMD_SHOW_PASSWORD;
            listViewToggleShowPassword.Icon = contextMenuItemImageShow;
            var item = listView.SelectedItem as PasswordViewItem;
            if (item != null && !item.HidePassword)
            {
                listViewToggleShowPassword.Header = Properties.Resources.CMD_HIDE_PASSWORD;
                listViewToggleShowPassword.Icon = contextMenuItemImageHide;
            }
        }

        private void ListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var mousePosition = e.GetPosition(listView);
            var lvitem = listView.GetItemAt(mousePosition);
            if (lvitem != null)
            {
                EditItemAsync(lvitem.Content as PasswordViewItem);
            }
        }

        private void ListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateControls();
        }

        private void Command_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            RoutedUICommand r = e.Command as RoutedUICommand;
            if (r == null) return;
            int selected = 0;
            string url = null;
            if (listView != null && listView.SelectedItems != null)
            {
                selected = listView.SelectedItems.Count;
            }
            bool passwordHidden = true;
            if (selected == 1)
            {
                var item = listView.SelectedItem as PasswordViewItem;
                if (item.Password != null)
                {
                    url = item.Password.Url;
                    passwordHidden = item.HidePassword;
                }
            }
            switch (r.Name)
            {
                case "New":
                case "Exit":
                case "Open":
                case "About":
                case "ShowLoginColumn":
                    e.CanExecute = true;
                    break;
                case "Save":
                    e.CanExecute = passwordRepository != null && passwordRepository.Changed;
                    break;
                case "SaveAs":
                case "Close":
                case "Properties":
                case "Add":
                case "ChangeKeyDirectory":
                    e.CanExecute = passwordRepository != null;
                    break;
                case "Edit":
                case "CopyLogin":
                    e.CanExecute = selected == 1;
                    break;
                case "Remove":
                case "TogglePassword":
                    e.CanExecute = selected >= 1;
                    break;
                case "OpenURL":
                    e.CanExecute = selected == 1 && !string.IsNullOrEmpty(url);
                    break;
                default:
                    break;
            }
        }


        private void Command_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            RoutedUICommand r = e.Command as RoutedUICommand;
            if (r == null) return;
            if (!ReenterPassword())
            {
                return;
            }
            switch (r.Name)
            {
                case "New":
                    CreateRepository();
                    break;
                case "Exit":
                    Close();
                    break;
                case "Open":
                    OpenRepository(null, false);
                    break;
                case "SaveAs":
                    SaveRepository(null);
                    break;
                case "Close":
                    CloseRepository();
                    break;
                case "Save":
                    SaveRepository(passwordFilename);
                    break;
                case "ChangeKeyDirectory":
                    ChangeKeyDirectory();
                    break;
                case "Properties":
                    ShowProperties();
                    break;
                case "Add":
                    AddItem();
                    break;
                case "Edit":
                    EditItem();
                    break;
                case "Remove":
                    RemoveItems();
                    break;
                case "OpenURL":
                    Open();
                    break;
                case "TogglePassword":
                    ToggleShowPassword();
                    break;
                case "CopyLogin":
                    CopyLogin();
                    break;
                case "About":
                    About();
                    break;
                case "ShowLoginColumn":
                    ShowLoginColumn(menuItemShowLoginColumn);
                    break;
                default:
                    break;
            }
        }

        // actions

        private void Init()
        {
            timeSecAutoHidePassword = Properties.Settings.Default.AutoHidePassword;
            timeSecReenterPassword = Properties.Settings.Default.ReenterPassword;
            menuItemImageShow = new Image{ Source = imageShow16x16, Height=16, Width=16 };
            menuItemImageHide = new Image{ Source = imageHide16x16, Height = 16, Width = 16 };
            menuItemImageShowDisabled = new Image { Source = imageShow16x16, Opacity = 0.5, Height = 16, Width = 16 };
            contextMenuItemImageShow = new Image { Source = imageShow16x16, Height = 16, Width = 16 };
            contextMenuItemImageHide = new Image { Source = imageHide16x16, Height = 16, Width = 16 };
            var cacheDirectory = Properties.Settings.Default.CacheDirectory.ReplaceSpecialFolder();
            PrepareDirectory(cacheDirectory);
            keyDirectoryCache = new KeyDirectoryCache(cacheDirectory);
            keyDirectoryCache.Load();
            PrepareDirectory(keyDirectoryCache.GetLastUsed());
            UpdateLoginColumn();
            SortListView();
            menuItemShowLoginColumn.IsChecked = Properties.Settings.Default.ShowLoginColumn;
            UpdateControls();
            var filename = Properties.Settings.Default.LastUsedRepositoryFile;
            if (!string.IsNullOrEmpty(filename))
            {
                OpenRepository(filename, true);
            }
            else
            {
                CreateRepository();
            }
        }

        private void UpdateLoginColumn()
        {
            GridView gv = listView.View as GridView;
            if (gv != null)
            {
                if (!Properties.Settings.Default.ShowLoginColumn)
                {
                    gv.Columns.Remove(gridViewColumnLogin);
                }
                else if (gv.Columns.Count == 2)
                {
                    gv.Columns.Insert(1, gridViewColumnLogin);
                }
            }
        }

        private void SortListView()
        {
            listView.Items.SortDescriptions.Clear();
            listView.Items.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Ascending));
            listView.Items.Refresh();
        }
  
        private static void PrepareDirectory(string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
            }
        }

        private void UpdateControls()
        {
            bool showEnabled = false;
            bool hideEnabled = false;
            int selected = listView.SelectedItems.Count;
            if (selected > 0)
            {
                var item = listView.SelectedItem as PasswordViewItem;
                showEnabled = item.HidePassword;
                hideEnabled = !showEnabled;
            }
            if (hideEnabled)
            {
                imageToggleShow.Source = imageHide32x32;
                imageToggleShow.ToolTip = Properties.Resources.CMD_HIDE_PASSWORD;
                menuItemTogglePassword.Icon = menuItemImageHide;
                menuItemTogglePassword.Header = Properties.Resources.CMD_HIDE_PASSWORD;
            }
            else
            {
                imageToggleShow.ToolTip = Properties.Resources.CMD_SHOW_PASSWORD;
                imageToggleShow.Source = imageShow32x32;
                menuItemTogglePassword.Header = Properties.Resources.CMD_SHOW_PASSWORD;
                menuItemTogglePassword.Icon = showEnabled ? menuItemImageShow : menuItemImageShowDisabled;
            }
            Title = Properties.Resources.PASSWORD_MANAGER;
            if (passwordRepository != null)
            {
                Title += $" - {passwordRepository.Name}";
                if (passwordRepository.Changed)
                {
                    Title += " *";
                }
            }
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            int selected = listView.SelectedItems.Count;
            int total = listView.Items.Count;
            string status = string.Empty;
            if (selected > 0)
            {
                if (total == 1)
                {
                    status = Properties.Resources.SELECTED_ONE;
                }
                else
                {
                    status = string.Format(Properties.Resources.SELECTED_0_OF_1, selected, total);
                }
            }
            else if (total > 0)
            {
                if (total == 1)
                {
                    status = Properties.Resources.TOTAL_ONE;
                }
                else
                {
                    status = string.Format(Properties.Resources.TOTAL_0, total);
                }
            }
            if (autoHidePassword)
            {
                TimeSpan ts = DateTime.Now - dateTimeShowPassword;
                int sec = Math.Max(0, 30 - (int)ts.TotalSeconds);
                if (sec > 0)
                {
                    string hidestr;
                    if (sec == 1)
                    {
                        hidestr = Properties.Resources.AUTO_HIDE_IN_ONE;
                    }
                    else
                    {
                        hidestr = string.Format(Properties.Resources.AUTO_HIDE_IN_0, sec);
                    }
                    status += " " + hidestr;
                }
            }
            textBlockStatus.Text = status;
        }

        private void Open()
        {
            Open(listView.SelectedItem as PasswordViewItem);
        }
 
        private void Open(PasswordViewItem item)
        {
            if (item != null && !string.IsNullOrEmpty(item.Password.Url))
            {
                string url = item.Password.Url.ToLowerInvariant();
                if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                {
                    url = $"https://{url}";
                }
                Process.Start(url);
            }
        }

        private async void AddItem()
        {
            try
            {
                EditWindow w = new EditWindow(Properties.Resources.CMD_ADD, imageKey16x16);
                if (w.ShowDialog() == true)
                {
                    passwordRepository.Add(w.Password);
                    var item = new PasswordViewItem(w.Password, imageKey16x16);
                    listView.Items.Add(item);
                    listView.SelectedItem = item;
                    listView.ScrollIntoView(item);
                    UpdateControls();
                    if (thumbnailCache != null &&
                        !string.IsNullOrEmpty(w.Password.Url))
                    {
                        var filename = await thumbnailCache.GetImageFileNameAsync(w.Password.Url);
                        if (!string.IsNullOrEmpty(filename))
                        {
                            item.Image = new BitmapImage(new Uri(filename));
                        }
                    }
                    SortListView();
                    listView.ScrollIntoView(item);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Properties.Resources.ERROR_OCCURRED_0, ex.Message), this.Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EditItem()
        {
            EditItemAsync(listView.SelectedItem as PasswordViewItem);
        }
  
        private async void EditItemAsync(PasswordViewItem item)
        {
            try
            {
                if (item == null) return;
                var oldurl = item.Password.Url;
                var w = new EditWindow(Properties.Resources.CMD_EDIT, item.Image, item.Password);
                if (w.ShowDialog() == true)
                {
                    passwordRepository.Update(w.Password);
                    item.Update(w.Password);
                    UpdateControls();
                    if (!string.Equals(oldurl, w.Password.Url))
                    {
                        if (thumbnailCache != null &&
                            !string.IsNullOrEmpty(item.Password.Url))
                        {
                            var image = imageKey16x16;
                            var filename = await thumbnailCache.GetImageFileNameAsync(item.Password.Url);
                            if (!string.IsNullOrEmpty(filename))
                            {
                                item.Image = new BitmapImage(new Uri(filename));
                            }
                        }
                    }
                    SortListView();
                    listView.ScrollIntoView(item);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Properties.Resources.ERROR_OCCURRED_0, ex.Message), this.Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RemoveItems()
        {
            try
            {
                if (MessageBox.Show(
                        Properties.Resources.QUESTION_DELETE_ITEMS,
                        Title,
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    var del = new List<PasswordViewItem>();
                    foreach (PasswordViewItem item in listView.SelectedItems)
                    {
                        del.Add(item);
                    }
                    int idx = listView.SelectedIndex;
                    foreach (var item in del)
                    {
                        listView.Items.Remove(item);
                        passwordRepository.Remove(item.Password);
                    }
                    idx = Math.Min(idx, listView.Items.Count - 1);
                    if (idx >= 0)
                    {
                        listView.SelectedIndex = idx;
                    }
                    UpdateControls();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Properties.Resources.ERROR_OCCURRED_0, ex.Message), this.Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ToggleShowPassword()
        {
            try
            {
                bool showEnabled = false;
                bool hideEnabled = false;
                int selected = listView.SelectedItems.Count;
                if (selected > 0)
                {
                    var item = listView.SelectedItem as PasswordViewItem;
                    showEnabled = item.HidePassword;
                    hideEnabled = !showEnabled;
                }
                if (showEnabled)
                {
                    foreach (PasswordViewItem item in listView.SelectedItems)
                    {
                        item.HidePassword = false;
                    }
                    listView.Items.Refresh();
                    dateTimeShowPassword = DateTime.Now;
                    autoHidePassword = true;
                }
                else if (hideEnabled)
                {
                    foreach (PasswordViewItem item in listView.SelectedItems)
                    {
                        item.HidePassword = true;
                    }
                    listView.Items.Refresh();
                    bool passwdshown = false;
                    foreach (PasswordViewItem item in listView.Items)
                    {
                        if (!item.HidePassword)
                        {
                            passwdshown = true;
                            break;
                        }
                    }
                    if (!passwdshown)
                    {
                        autoHidePassword = false;
                    }
                }
                UpdateControls();
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Properties.Resources.ERROR_OCCURRED_0, ex.Message), this.Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool ReenterPassword()
        {
            if (reenterPassword)
            {
                foreach (PasswordViewItem item in listView.Items)
                {
                    item.HidePassword = true;
                }
                listView.Items.Refresh();
                var dlg = new LoginWindow(Properties.Resources.VERIFY_PASSWORD, keyDirectoryCache, passwordFilename);
                dlg.SecurePassword = securePassword;
                if (dlg.ShowDialog() != true)
                {
                    return false;
                }
                reenterPassword = false;
                dateTimeIdle = DateTime.Now;
            }
            return true;
        }

        private void CopyLogin()
        {
            try
            {
                var item = listView.SelectedItem as PasswordViewItem;
                if (item != null && !string.IsNullOrEmpty(item.Login))
                {
                    Clipboard.SetText(item.Login);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Properties.Resources.ERROR_OCCURRED_0, ex.Message), this.Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void About()
        {
            try
            {
                var dlg = new AboutWindow(Properties.Resources.CMD_ABOUT);
                dlg.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Properties.Resources.ERROR_OCCURRED_0, ex.Message), this.Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void InitThumbnailCacheAsync(string cacheDirectory)
        {
            try
            {
                if (thumbnailCache == null)
                {
                    thumbnailCache = new ThumbnailCache(cacheDirectory);
                    thumbnailCache.Load();
                }
                foreach (PasswordViewItem item in listView.Items)
                {
                    var filename = await thumbnailCache.GetImageFileNameAsync(item.Password.Url);
                    if (!string.IsNullOrEmpty(filename))
                    {
                        item.Image = new BitmapImage(new Uri(filename));
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Properties.Resources.ERROR_OCCURRED_0, ex.Message), this.Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool PromptSaveChanges()
        {
            bool canceled = false;
            return PromptSaveChanges(ref canceled);
        }

        private bool PromptSaveChanges(ref bool canceled)
        {
            canceled = false;
            if (passwordRepository == null)
            {
                return true;
            }
            bool ret = false;
            try
            {
                if (passwordRepository.Changed)
                {
                    MessageBoxResult r = MessageBox.Show(this, Properties.Resources.QUESTION_SAVE_CHANGES,
                        this.Title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                    if (r == MessageBoxResult.Cancel)
                    {
                        canceled = true;
                        return false;
                    }
                    if (r == MessageBoxResult.Yes)
                    {
                        if (!ReenterPassword())
                        {
                            return false;
                        }
                        if (!SaveRepository(passwordFilename))
                        {
                            return false;
                        }
                    }
                }
                ret = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Properties.Resources.ERROR_OCCURRED_0, ex.Message), this.Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            UpdateControls();
            return ret;
        }

        private bool CloseRepository()
        {
            if (passwordRepository == null)
            {
                return true;
            }
            bool ret = false;
            try
            {
                if (PromptSaveChanges())
                {
                    passwordRepository = null;
                    securePassword.Clear();
                    listView.Items.Clear();
                    ret = true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Properties.Resources.ERROR_OCCURRED_0, ex.Message), this.Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            UpdateControls();
            return ret;
        }

        private void SaveAs()
        {
            SaveRepository(null);
        }
        private void Save()
        {
            SaveRepository(passwordFilename);
        }
        private bool SaveRepository(string filename)
        {
            bool ret = false;
            try
            {
                if (passwordRepository == null)
                {
                    return false;
                }
                if (string.IsNullOrEmpty(filename))
                {
                    SaveFileDialog dlg = new SaveFileDialog()
                    {
                        InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        Filter = Properties.Resources.FILE_DIALOG_FILTER
                    };
                    var initDir = Properties.Settings.Default.InitialDirectory.ReplaceSpecialFolder();
                    if (Directory.Exists(initDir))
                    {
                        dlg.InitialDirectory = initDir;
                    }
                    if (dlg.ShowDialog() != true)
                    {
                        return false;
                    }
                    filename = dlg.FileName;
                    Properties.Settings.Default.InitialDirectory = new FileInfo(filename).Directory.FullName;
                }
                var keyDirectory = keyDirectoryCache.Get(passwordRepository.Id);
                passwordRepository.Save(filename, keyDirectory, securePassword);
                passwordFilename = filename;
                Properties.Settings.Default.LastUsedRepositoryFile = filename;
                SortListView();
                ret = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Properties.Resources.ERROR_OCCURRED_0, ex.Message), this.Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            UpdateControls();
            return ret;
        }

        private bool CreateRepository()
        {
            bool ret = false;
            try
            {
                if (!PromptSaveChanges())
                {
                    return false;
                }
                PrepareWindow dlg = new PrepareWindow(Properties.Resources.NEW, keyDirectoryCache);
                if (dlg.ShowDialog() != true)
                {
                    return false;
                }
                if (!CloseRepository())
                {
                    return false;
                }
                securePassword = dlg.SecurePassword;
                passwordRepository = dlg.PasswordRepository;
                passwordFilename = null;
                ret = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Properties.Resources.ERROR_OCCURRED_0, ex.Message), this.Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            UpdateControls();
            var cacheDirectory = Properties.Settings.Default.CacheDirectory.ReplaceSpecialFolder();
            InitThumbnailCacheAsync(cacheDirectory);
            return ret;
        }

        private void OpenRepository()
        {
            OpenRepository(null, false);
        }

        private bool OpenRepository(string filename, bool silent)
        {
            bool ret = false;
            try
            {
                if (!PromptSaveChanges())
                {
                    return false;
                }
                if (string.IsNullOrEmpty(filename))
                {
                    OpenFileDialog opendlg = new OpenFileDialog()
                    {
                        InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        Filter = Properties.Resources.FILE_DIALOG_FILTER
                    };
                    var initDir = Properties.Settings.Default.InitialDirectory.ReplaceSpecialFolder();
                    if (Directory.Exists(initDir))
                    {
                        opendlg.InitialDirectory = initDir;
                    }
                    if (opendlg.ShowDialog() != true)
                    {
                        return false;
                    }
                    filename = opendlg.FileName;
                    Properties.Settings.Default.InitialDirectory = new FileInfo(filename).Directory.FullName;
                }
                LoginWindow dlg = new LoginWindow(
                    Properties.Resources.VERIFY_PASSWORD,
                    keyDirectoryCache,
                    filename);
                if (dlg.ShowDialog() == true)
                {
                    if (!CloseRepository())
                    {
                        return false;
                    }
                    passwordFilename = filename;
                    passwordRepository = dlg.PasswordRepository;
                    securePassword = dlg.SecurePassword;
                    Properties.Settings.Default.LastUsedRepositoryFile = passwordFilename;
                    foreach (var password in passwordRepository.Passwords)
                    {
                        listView.Items.Add(new PasswordViewItem(password, imageKey16x16));
                    }
                    SortListView();
                    var cacheDirectory = Properties.Settings.Default.CacheDirectory.ReplaceSpecialFolder();
                    InitThumbnailCacheAsync(cacheDirectory);
                    ret = true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Properties.Resources.ERROR_OCCURRED_0, ex.Message), this.Title, MessageBoxButton.OK, MessageBoxImage.Error);
                ret = false;
            }
            UpdateControls();
            return ret;
        }

        private void ChangeKeyDirectory()
        {
            try
            {
                if (passwordRepository == null) return;
                var keyDir = keyDirectoryCache.Get(passwordRepository.Id);
                var dlg = new System.Windows.Forms.FolderBrowserDialog()
                {
                    Description = Properties.Resources.LABEL_SELECT_KEY_DIRECTORY,
                    SelectedPath = keyDir
                };
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    if (!string.Equals(keyDir, dlg.SelectedPath, StringComparison.InvariantCultureIgnoreCase))
                    {
                        passwordRepository.MoveKey(keyDir, dlg.SelectedPath);
                        keyDirectoryCache.Set(passwordRepository.Id, dlg.SelectedPath);                        
                    }
                }
                Properties.Settings.Default.ShowLoginColumn = !Properties.Settings.Default.ShowLoginColumn;
                UpdateLoginColumn();
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Properties.Resources.ERROR_OCCURRED_0, ex.Message), Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowProperties()
        {
            try
            {
                var dlg = new PropertiesWindow(
                    Properties.Resources.CMD_PROPERTIES,
                    keyDirectoryCache,
                    passwordRepository,
                    passwordFilename);
                if (dlg.ShowDialog() == true)
                {
                    UpdateControls();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Properties.Resources.ERROR_OCCURRED_0, ex.Message), Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowLoginColumn(object sender)
        {
            try
            {
                MenuItem mi = sender as MenuItem;
                if (mi != null)
                {
                    Properties.Settings.Default.ShowLoginColumn = mi.IsChecked;
                    UpdateLoginColumn();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Properties.Resources.ERROR_OCCURRED_0, ex.Message), Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
