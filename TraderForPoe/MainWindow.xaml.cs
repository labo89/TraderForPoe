using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using TraderForPoe.Classes;
using TraderForPoe.Controls;
using TraderForPoe.Properties;
using TraderForPoe.Windows;
using WindowsInput;
using WindowsInput.Native;

namespace TraderForPoe
{
    public partial class MainWindow : Window
    {
        // Needed to prevent window getting focus
        const int GWL_EXSTYLE = -20;
        const int WS_EX_NOACTIVATE = 134217728;
        const int LSFW_LOCK = 1;

        // Get a handle to an application window.
        [DllImport("USER32.DLL", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        // Activate an application window.
        [DllImport("USER32.DLL")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        // Needed to prevent Application taking focus
        [DllImport("user32")]
        public static extern bool LockSetForegroundWindow(uint UINT);

        [DllImport("user32")]
        public static extern IntPtr SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        System.Windows.Forms.NotifyIcon nIcon = new System.Windows.Forms.NotifyIcon();

        System.Windows.Forms.ContextMenuStrip cMenu = new System.Windows.Forms.ContextMenuStrip();

        System.Windows.Forms.ToolStripMenuItem itmHistory = new System.Windows.Forms.ToolStripMenuItem("History");

        ClipboardMonitor clipMoni = new ClipboardMonitor();

        About aboutWindow = new About();

        UserSettings userSettings = new UserSettings();

        bool mainWindowCollapsed = false;

        DispatcherTimer dispatcherTimer = new DispatcherTimer();
        // Variables for reading the Client.txt

        long initialFileSize;

        long lastReadLength;

        Regex customerJoinedRegEx = new Regex(".* : (.*) has joined the area");

        Regex customerLeftRegEx = new Regex(".* : (.*) has left the area");

        

        public MainWindow()
        {
            CheckRunning();
            
            CheckForUpdates();

            SubscribeToEvents();

            InitializeComponent();

            CreateContextMenu();

            CheckForClientTxt();

            LoadSetting();

            StartFileMonitoring();
        }

        private void CheckRunning()
        {
            bool result;
            var mutex = new System.Threading.Mutex(true, Assembly.GetExecutingAssembly().GetName().ToString(), out result);
            if (!result)
            {
                MessageBox.Show("Another instance of the application is already running!");
                Application.Current.Shutdown();
            }
            GC.KeepAlive(mutex);
        }

        private void CheckForUpdates()
        {
            if (Settings.Default.CheckForUpdatesOnStart)
            {
                if (Updater.UpdateIsAvailable())
                {
                    if (MessageBox.Show("A new version is available. Do you want to Update? Application will be restarted.", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        Updater.StartUpdate();
                    }
                }
            }
        }

        private void CheckForClientTxt()
        {
            if (File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Grinding Gear Games\Path of Exile\logs\Client.txt")))
            {
                Settings.Default.PathToClientTxt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Grinding Gear Games\Path of Exile\logs\Client.txt");
                Settings.Default.Save();
                return;
            }

            if (File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Steam\steamapps\common\Path of Exile\logs\Client.txt")))
            {
                Settings.Default.PathToClientTxt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Steam\steamapps\common\Path of Exile\logs\Client.txt");
                Settings.Default.Save();
                return;
            }

            if (File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Grinding Gear Games\Path of Exile\logs\Client.txt")))
            {
                Settings.Default.PathToClientTxt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Grinding Gear Games\Path of Exile\logs\Client.txt");
                Settings.Default.Save();
                return;
            }

            if (File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Steam\steamapps\common\Path of Exile\logs\Client.txt")))
            {
                Settings.Default.PathToClientTxt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Steam\steamapps\common\Path of Exile\logs\Client.txt");
                Settings.Default.Save();
                return;
            }
        }

        private void SubscribeToEvents()
        {
            TradeItemControl.MoreThanThreeItems += TradeItemControl_MoreThanThreeItems;
            TradeItemControl.EqualThreeItems += TradeItemControl_LessThanThreeItems;
            CustMenuItem.OnItemCountExceed += CustMenuItem_OnItemCountExceed;
        }

        private void CustMenuItem_OnItemCountExceed(object sender, EventArgs e)
        {
            // remove the first item in history list, if limit is reached
            itmHistory.DropDownItems.RemoveAt(0);
        }

        private void TradeItemControl_MoreThanThreeItems(object sender, EventArgs e)
        {
            stk_ControlPnl.Visibility = Visibility.Visible;
            brd_collapseMainWindow.Visibility = Visibility.Visible;
            //btn_collapseMainWindow.Visibility = Visibility.Visible;
        }

        private void TradeItemControl_LessThanThreeItems(object sender, EventArgs e)
        {
            stk_ControlPnl.Visibility = Visibility.Collapsed;
            brd_collapseMainWindow.Visibility = Visibility.Collapsed;
            //btn_collapseMainWindow.Visibility = Visibility.Collapsed;
        }

        private void LoadSetting()
        {
            if (Settings.Default.UseClipboardMonitor == true)
            {
                clipMoni.OnClipboardContentChanged += ClipMoni_OnClipboardContentChanged;
                cMenu.Items[0].Text = "Stop Monitor";
            }
            else
            {
                cMenu.Items[0].Text = "Start Monitor";
            }

            // Subscribe to SetNoActiveWindow. Prevent window from focus
            Loaded += (object sender, RoutedEventArgs e) => SetNoActiveWindow();

            this.Top = Settings.Default.PosTop;

            this.Left = Settings.Default.PosLeft;
        }

        private string GetClipboardText()
        {
            string strClipboard = string.Empty;

            for (int i = 0; i < 10; i++)
            {
                try
                {
                    strClipboard = Clipboard.GetText(TextDataFormat.UnicodeText);
                    return strClipboard;
                }
                catch (System.Runtime.InteropServices.COMException ex)
                {
                    //fix for OpenClipboard Failed (Exception from HRESULT: 0x800401D0 (CLIPBRD_E_CANT_OPEN))
                    //https://stackoverflow.com/questions/12769264/openclipboard-failed-when-copy-pasting-data-from-wpf-datagrid
                    //https://stackoverflow.com/questions/68666/clipbrd-e-cant-open-error-when-setting-the-clipboard-from-net
                    if (ex.ErrorCode == -2147221040)
                        System.Threading.Thread.Sleep(10);
                    else
                        throw new Exception("Unable to get Clipboard text. Message: \n" + ex.Message);
                }
            }

            return strClipboard;
        }

        private void ClipMoni_OnClipboardContentChanged(object sender, EventArgs e)
        {
            string strClipboard = GetClipboardText();

            if (strClipboard.StartsWith("@"))
            {
                bool sendCustomWhisper3 = Keyboard.IsKeyDown(Key.RightCtrl);
                bool sendCustomWhisper4 = Keyboard.IsKeyDown(Key.LeftCtrl);
                
                try
                {
                    string message = Regex.Replace(strClipboard, @"^\@(.+?)\s+(.+)$",
                        match => ("@To " + match.Groups[1].Value + ": " + match.Groups[2].Value));

                    TradeItem tItem = new TradeItem(message, false);
                }
                catch
                {
                    SoundPlayer player = new SoundPlayer(Properties.Resources.error);
                    player.Play();
                    return;
                }
                
                // Get a handle to POE. The window class and window name were obtained using the Spy++ tool.
                IntPtr poeHandle = FindWindow("POEWindowClass", "Path of Exile");
                    
                // Verify that POE is a running process.
                if (poeHandle == IntPtr.Zero)
                {
                    // Show message box if POE is not running
                    MessageBox.Show("Path of Exile is not running.");
                    return;
                }

                InputSimulator iSim = new InputSimulator();

                // Need to press ALT because the SetForegroundWindow sometimes does not work
                iSim.Keyboard.KeyPress(VirtualKeyCode.MENU);

                // Make POE the foreground application and send input
                SetForegroundWindow(poeHandle);

                Thread.Sleep(100);
                    
                iSim.Keyboard.KeyPress(VirtualKeyCode.RETURN);
                iSim.Keyboard.TextEntry(strClipboard);
                iSim.Keyboard.KeyPress(VirtualKeyCode.RETURN);

                if (sendCustomWhisper3)
                {
                    Thread.Sleep(100);
                    iSim.Keyboard.KeyPress(VirtualKeyCode.RETURN);
                    iSim.Keyboard.TextEntry(Settings.Default.CustomWhisper3);
                    iSim.Keyboard.KeyPress(VirtualKeyCode.RETURN);
                }
                
                if (sendCustomWhisper4)
                {
                    Thread.Sleep(100);
                    iSim.Keyboard.KeyPress(VirtualKeyCode.RETURN);
                    iSim.Keyboard.TextEntry(Settings.Default.CustomWhisper4);
                    iSim.Keyboard.KeyPress(VirtualKeyCode.RETURN);
                }

                iSim = null;

                Clipboard.Clear();
            }
        }

        private void CreateContextMenu()
        {
            nIcon.MouseClick += NIcon_MouseClick;
            nIcon.MouseDoubleClick += NIcon_MouseDoubleClick;
            Stream iconStream = Application.GetResourceStream(new Uri("pack://application:,,,/Resources/ico_Application.ico")).Stream;
            nIcon.Icon = new System.Drawing.Icon(iconStream);
            nIcon.Visible = true;

            var monitor = cMenu.Items.Add("Monitor");
            monitor.Click += CMenu_ClipboardMonitor;

            cMenu.Items.Add(itmHistory);

            var settings = cMenu.Items.Add("Settings");
            settings.Click += CMenu_Settings;

            var update = cMenu.Items.Add("Update");
            update.Click += CMenu_Update;

            var about = cMenu.Items.Add("About");
            about.Click += CMenu_About;

            var exit = cMenu.Items.Add("Exit");
            exit.Click += CMenu_Close;

            nIcon.ContextMenuStrip = cMenu;
        }

        private void CMenu_Update(object sender, EventArgs e)
        {
            if (Updater.UpdateIsAvailable())
            {
                if (MessageBox.Show("A new version is available. Do you want to Update? Application will be restarted.", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    Updater.StartUpdate();
                }
            }
            else
            {
                System.Windows.Forms.MessageBox.Show("No updates available", "No updates", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Information);
            }
        }

        private void NIcon_MouseDoubleClick(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            userSettings.Show();
        }

        private void CMenu_ClipboardMonitor(object sender, EventArgs e)
        {
            if (cMenu.Items[0].Text.StartsWith("Start"))
            {
                clipMoni.OnClipboardContentChanged += ClipMoni_OnClipboardContentChanged;
                cMenu.Items[0].Text = "Stop Monitor";
            }
            else if (cMenu.Items[0].Text.StartsWith("Stop"))
            {
                clipMoni.OnClipboardContentChanged -= ClipMoni_OnClipboardContentChanged;
                cMenu.Items[0].Text = "Start Monitor";
            }
            else
            {
                throw new ArgumentException("Menu item text must start with \"Start\" or \"Stop\"");
            }
        }

        private void CMenu_Settings(object sender, EventArgs e)
        {
            userSettings.Show();
        }

        private void CMenu_About(object sender, EventArgs e)
        {
            aboutWindow.Show();
        }

        private void StartFileMonitoring()
        {
            string filePath = Settings.Default.PathToClientTxt;
            if (!String.IsNullOrEmpty(filePath))
            {
                try
                {
                    // Set variables befor Timer start
                    initialFileSize = new FileInfo(filePath).Length;

                    lastReadLength = initialFileSize;

                    dispatcherTimer.Tick += new EventHandler(DispatcherTimer_Tick);

                    dispatcherTimer.Interval = new TimeSpan(0, 0, 0, 0, 200);

                    dispatcherTimer.Start();
                }
                catch (FileNotFoundException ex)
                {
                    System.Windows.Forms.MessageBox.Show(ex.Message, "Exception", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                    userSettings.Show();
                }
            }
            else
            {
                System.Windows.Forms.MessageBox.Show("No Client.txt found \nPlease set the correct path in the settings and restart the application.", "File not found", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                userSettings.Show();
            }
        }

        private void DispatcherTimer_Tick(object sender, EventArgs e)
        {
            string filePath = Settings.Default.PathToClientTxt;

            CheckIfPoeIsForegroundWindow();

            if (lastReadLength < 0)
            {
                lastReadLength = 0;
            }

            try
            {
                var fileSize = new FileInfo(filePath).Length;
                if (fileSize > lastReadLength)
                {
                    using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        fs.Seek(lastReadLength, SeekOrigin.Begin);
                        var buffer = new byte[1024];

                        var bytesRead = fs.Read(buffer, 0, buffer.Length);
                        lastReadLength += bytesRead;


                        var text = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                        using (var reader = new StringReader(text))
                        {
                            for (string line = reader.ReadLine(); line != null; line = reader.ReadLine())
                            {
                                // Check for trade whispers
                                if (line.Contains(" @"))
                                {
                                    // Get a handle to POE. The window class and window name were obtained using the Spy++ tool.
                                    IntPtr poeHandle = FindWindow("POEWindowClass", "Path of Exile");

                                    // Verify that POE is a running process.
                                    if (poeHandle != IntPtr.Zero)
                                    {
                                        try
                                        {
                                            TradeItem tItem = new TradeItem(line);
                                            TradeItemControl uc = new TradeItemControl(tItem);
                                            //stk_MainPnl.Children.Add(uc);
                                            stk_MainPnl.Children.Insert(0, uc);
                                            var customMenuItem = new CustMenuItem(uc);
                                            customMenuItem.Click += CustomMenuItem_Click;
                                            itmHistory.DropDownItems.Add(customMenuItem);
                                        }
                                        catch (NoCurrencyBitmapFoundException)
                                        {
                                            //System.Windows.Forms.MessageBox.Show(ex.Message, "Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                                        }
                                        catch (NoCurrencyFoundException)
                                        {
                                            //System.Windows.Forms.MessageBox.Show(ex.Message, "Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                                        }
                                        catch (NoRegExMatchException)
                                        {
                                            //System.Windows.Forms.MessageBox.Show(ex.Message, "Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                                        }
                                        catch (TradeItemExistsException)
                                        {
                                            //System.Windows.Forms.MessageBox.Show(ex.Message, "Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                                        }
                                        catch (Exception ex)
                                        {
                                            System.Windows.Forms.MessageBox.Show(ex.Message, "Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                                        }
                                        
                                        Match match = Regex.Match(line, @"@(To|From)\s+(.+?):\s+");
                                        if (match.Success)
                                        {
                                            string customer = match.Groups[2].Value;
                                            TradeItemControl record = stk_MainPnl.Children.Cast<TradeItemControl>().FirstOrDefault(p => p.tItem.Customer == customer);
                                            if (record != default(TradeItemControl))
                                            {
                                                record.chatHistory += Regex.Replace(line, @"^.+\@(.+?)\s+(.+)$", m => (m.Groups[1].Value + " " + m.Groups[2].Value + "\n"));
                                                record.txt_Customer.ToolTip = record.chatHistory;
                                                stk_MainPnl.Children.Remove(record);
                                                stk_MainPnl.Children.Insert(0, record);
                                            }
                                        }
                                    }
                                }

                                // Check if customer joined or left
                                else if (customerJoinedRegEx.IsMatch(line))
                                {
                                    MatchCollection matches = Regex.Matches(line, customerJoinedRegEx.ToString());

                                    foreach (Match match in matches)
                                    {
                                        foreach (TradeItemControl item in stk_MainPnl.Children)
                                        {
                                            if (item.tItem.Customer == match.Groups[1].Value)
                                            {
                                                item.CustomerJoined();
                                            }
                                        }
                                    }
                                }

                                // Check if customer left
                                else if (customerLeftRegEx.IsMatch(line))
                                {
                                    MatchCollection matches = Regex.Matches(line, customerLeftRegEx.ToString());

                                    foreach (Match match in matches)
                                    {
                                        foreach (TradeItemControl item in stk_MainPnl.Children)
                                        {
                                            if (item.tItem.Customer == match.Groups[1].Value)
                                            {
                                                item.CustomerLeft();
                                            }
                                        }
                                    }
                                }

                            }
                        }

                    }
                }
            }
            catch (FileNotFoundException)
            {
                dispatcherTimer.Tick -= DispatcherTimer_Tick;
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(ex.Message, "Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);

            }
        }

        // Handle click event for the history menu item
        private void CustomMenuItem_Click(object sender, EventArgs e)
        {
            var s = sender as CustMenuItem;

            // Avoid exception by checking if the item is already in the panel
            if (!stk_MainPnl.Children.Contains(s.GetTradeItemCtrl))
            {
                stk_MainPnl.Children.Add(s.GetTradeItemCtrl);
            }
        }

        private void CheckIfPoeIsForegroundWindow()
        {
            if (Settings.Default.HideIfPoeNotForeGround)
            {
                if (GetForegroundWindow() != FindWindow("POEWindowClass", "Path of Exile"))
                    Hide();
                else
                    Show();
            }
        }

        private void CMenu_Close(object sender, EventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void NIcon_MouseClick(object sender, System.Windows.Forms.MouseEventArgs e)
        {

        }

        private void SetNoActiveWindow()
        {
            WindowInteropHelper helper = new WindowInteropHelper(this);
            SetWindowLong(helper.Handle, GWL_EXSTYLE, WS_EX_NOACTIVATE);
            LockSetForegroundWindow(LSFW_LOCK);
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                this.DragMove();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            nIcon.Visible = false;
            nIcon.Dispose();
            System.Windows.Forms.Application.DoEvents();
        }

        private void Window_LocationChanged(object sender, EventArgs e)
        {
            Settings.Default.PosLeft = this.Left;
            Settings.Default.PosTop = this.Top;
            Settings.Default.Save();
        }

        private void ClickCollapseExpandMainwindow(object sender, RoutedEventArgs e)
        {
            if (mainWindowCollapsed == false)
            {
                //btn_collapseMainWindow.Width = this.Width;
                btn_collapseMainWindow.Content = ">";
                stk_MainPnl.Visibility = Visibility.Collapsed;
                //stk_ControlPnl.Visibility = Visibility.Visible;
                mainWindowCollapsed = true;
            }
            else
            {
                //btn_collapseMainWindow.Width = btn_collapseMainWindow.MinWidth;
                btn_collapseMainWindow.Content = "^";
                stk_MainPnl.Visibility = Visibility.Visible;
                mainWindowCollapsed = false;
            }
        }
        
        private void ClickClearRecords(object sender, RoutedEventArgs e)
        {
            List<TradeItemControl> l = stk_MainPnl.Children.Cast<TradeItemControl>().ToList();
            l.ForEach(element => element.RemoveItem());
        }
    }
}
