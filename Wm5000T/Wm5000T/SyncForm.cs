using System;
using System.IO;
using System.Windows.Forms;

namespace Wm5000T
{
    public partial class SyncForm : Form
    {
        private WMWEBUSBLib.Wwusb wmport;
        private long opened = -1;

        private Timer usbCheckTimer;
        private int loopInterval = 5000;

        private bool isConnected = false;

        private string lastDeviceTermno = "";
        private bool hasFetchedRecords = false;

        private Timer countdownTimer;
        private int countdownRemaining;

        private ToolStripMenuItem startScanMenuItem;
        private ToolStripMenuItem stopScanMenuItem;

        public SyncForm()
        {
            InitializeComponent();

            this.Resize += SyncForm_Resize;
            this.Load += SycnForm_Load; // <- penting agar SycnForm_Load terpanggil
            notifyIcon1.DoubleClick += NotifyIcon1_DoubleClick;

            // Optional: Tambahkan context menu
            ContextMenuStrip trayMenu = new ContextMenuStrip();

            trayMenu.Items.Add("Open", null, ShowForm);

            startScanMenuItem = new ToolStripMenuItem("Start Scan", null, (s, e) => StartScan());
            stopScanMenuItem = new ToolStripMenuItem("Stop Scan", null, (s, e) => StopScan());

            trayMenu.Items.Add(startScanMenuItem);
            trayMenu.Items.Add(stopScanMenuItem);

            trayMenu.Items.Add("Adjust Time", null, AdjustTime);
            trayMenu.Items.Add("Exit", null, ExitApp);
            notifyIcon1.ContextMenuStrip = trayMenu;
        }

        private void SyncForm_Resize(object sender, EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized)
            {
                this.Hide();
                notifyIcon1.BalloonTipTitle = "Wm5000TUniGuard Sync";
                notifyIcon1.BalloonTipText = "App is running in background.";
                notifyIcon1.ShowBalloonTip(3000);
            }
        }

        private void NotifyIcon1_DoubleClick(object sender, EventArgs e)
        {
            ShowMainWindow();
        }

        private void ShowForm(object sender, EventArgs e)
        {
            ShowMainWindow();
        }

        private void ExitApp(object sender, EventArgs e)
        {
            notifyIcon1.Visible = false;
            Application.Exit();
        }

        private void ShowMainWindow()
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.BringToFront();
        }

        protected override void SetVisibleCore(bool value)
        {
            // Sembunyikan form saat pertama kali run
            if (!IsHandleCreated)
            {
                value = false;
                CreateHandle(); // Buat handle agar tidak crash
            }
            base.SetVisibleCore(value);
        }

        private void SycnForm_Load(object sender, EventArgs e)
        {

            AddMessage("Initializing...");

            wmport = new WMWEBUSBLib.Wwusb();

            lbLog.Text = Environment.GetEnvironmentVariable("LOG_PATH");
            lbInterval.Text = $"{Environment.GetEnvironmentVariable("INTERVAL_LOOP")} seconds";
            lbDtype.Text = Environment.GetEnvironmentVariable("DATA_TYPE");
            lbRtype.Text = Environment.GetEnvironmentVariable("RECORDER_TYPE");

            string loopEnv = Environment.GetEnvironmentVariable("INTERVAL_LOOP");
            if (!string.IsNullOrEmpty(loopEnv) && int.TryParse(loopEnv, out int interval))
            {
                loopInterval = interval * 1000;
            }

            //usbCheckTimer = new Timer();
            //usbCheckTimer.Interval = loopInterval; // e.g. 5000 ms
            //usbCheckTimer.Tick += UsbCheckTimer_Tick;
            //usbCheckTimer.Start();

            //countdownRemaining = loopInterval / 1000;

            //countdownTimer = new Timer();
            //countdownTimer.Interval = 1000; // 1 detik
            //countdownTimer.Tick += CountdownTimer_Tick;
            //countdownTimer.Start();

            //AddMessage("Scanning started");

            //// Set state awal menu
            //startScanMenuItem.Enabled = false; // karena scan langsung dimulai
            //stopScanMenuItem.Enabled = true;

            usbCheckTimer = new Timer();
            usbCheckTimer.Interval = loopInterval;
            usbCheckTimer.Tick += UsbCheckTimer_Tick;

            countdownTimer = new Timer();
            countdownTimer.Interval = 1000;
            countdownTimer.Tick += CountdownTimer_Tick;

            countdownRemaining = loopInterval / 1000;

            StartScan(); // <-- gunakan fungsi existing

        }

        private void StartScan()
        {
            if (usbCheckTimer == null) return;

            if (!usbCheckTimer.Enabled)
            {
                usbCheckTimer.Start();
                countdownTimer?.Start();

                startScanMenuItem.Enabled = false;
                stopScanMenuItem.Enabled = true;
            }
        }

        private void StopScan()
        {
            usbCheckTimer?.Stop();
            countdownTimer?.Stop();
            AddMessage("⛔ Scan stopped.");

            startScanMenuItem.Enabled = true;
            stopScanMenuItem.Enabled = false;
        }

        private void AdjustTime(object sender, EventArgs e)
        {
            if (opened < 0)
            {
                try
                {
                    opened = wmport.OpenUsb(2050);
                }
                catch (Exception ex)
                {
                    AddMessage($"❌ Failed to open USB: {ex.Message}");
                    MessageBox.Show("Failed to open USB connection.");
                    return;
                }
            }

            if (opened >= 0)
            {
                DateTime now = DateTime.Now;
                long result = wmport.SetDateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second);

                if (result >= 0)
                {
                    AddMessage($"🕒 Time adjusted to {now:yyyy-MM-dd HH:mm:ss}");
                    MessageBox.Show($"Device time synced to {now:yyyy-MM-dd HH:mm:ss}", "Adjust Time", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    AddMessage("❌ Failed to adjust time");
                    MessageBox.Show("Failed to adjust time on device.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else
            {
                MessageBox.Show("Device is not connected.", "Adjust Time", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void CountdownTimer_Tick(object sender, EventArgs e)
        {
            if (countdownRemaining > 0)
            {
                countdownRemaining--;
            }

            int totalSeconds = loopInterval / 1000;
            lbInterval.Text = $"{totalSeconds} seconds ({totalSeconds - countdownRemaining}/{totalSeconds})";
        }

        private void AddMessage(string message, bool withTime = false)
        {
            if (withTime)
                listMessage.Items.Add($"{message} at {DateTime.Now:HH:mm:ss}");
            else
                listMessage.Items.Add(message);

            listMessage.TopIndex = listMessage.Items.Count - 1;
        }

        private void UsbCheckTimer_Tick(object sender, EventArgs e)
        {
            usbCheckTimer.Stop(); // Hindari overlap

            bool wasConnected = isConnected;
            bool nowConnected = false;

            try
            {
                if (opened < 0)
                {
                    opened = wmport.OpenUsb(2050);
                }

                if (opened >= 0)
                {
                    string termno = wmport.GetTermno();
                    if (!string.IsNullOrEmpty(termno))
                    {
                        nowConnected = true;

                        if (!wasConnected && nowConnected)
                        {
                            AddMessage("Device connected");
                        }

                        // Cek apakah TermNo berubah
                        if (termno != lastDeviceTermno)
                        {
                            lastDeviceTermno = termno;
                            AddMessage($"Device found (TermNo: {termno})");
                            hasFetchedRecords = false;
                        }
                    }

                    if (nowConnected && !hasFetchedRecords)
                    {
                        AddMessage("Fetching records");

                        string rawData = wmport.GetRecords();
                        string[] lines = rawData.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                        if (lines.Length > 0)
                        {
                            string logPath = Environment.GetEnvironmentVariable("LOG_PATH") ?? @"C:\Patrol_Log\Logs";

                            if (!Directory.Exists(logPath))
                            {
                                Directory.CreateDirectory(logPath);
                            }
                            else
                            {
                                foreach (var file in Directory.GetFiles(logPath))
                                {
                                    try { File.Delete(file); } catch { /* Optional: log error */ }
                                }
                            }

                            string dataType = Environment.GetEnvironmentVariable("DATA_TYPE") ?? "1";
                            string recorderType = Environment.GetEnvironmentVariable("RECORDER_TYPE") ?? "5";

                            string filePath = Path.Combine(logPath, $"patrol_log_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt");

                            AddMessage($"Saving {lines.Length} record(s) to file: {filePath}");

                            DateTime? lastValidDate = null;

                            using (StreamWriter writer = new StreamWriter(filePath, true))
                            {
                                foreach (var line in lines)
                                {
                                    string[] parts = line.Split(',');
                                    if (parts.Length == 3)
                                    {
                                        string deviceId = parts[0];
                                        string tagRaw = parts[1];
                                        string trimmed = tagRaw.TrimStart('0');
                                        string tag = trimmed.Length > 10
                                            ? trimmed.Substring(trimmed.Length - 10)
                                            : trimmed.PadLeft(10, '0');
                                        ulong tagDecimal = Convert.ToUInt64(tag, 16);

                                        string datetime = parts[2];

                                        string time = datetime.Substring(8, 2) + ":" +
                                                      datetime.Substring(10, 2) + ":" +
                                                      datetime.Substring(12, 2);

                                        string date;

                                        if (TryParseDeviceDate(datetime, out DateTime validDate))
                                        {
                                            lastValidDate = validDate;
                                            date = validDate.ToString("dd/MM/yyyy");
                                        }
                                        else
                                        {
                                            if (lastValidDate.HasValue)
                                            {
                                                date = lastValidDate.Value.ToString("dd/MM/yyyy");
                                            }
                                            else
                                            {
                                                date = DateTime.Now.ToString("dd/MM/yyyy");
                                            }
                                        }

                                        string logLine = $"{dataType},{recorderType},{tagDecimal},{date},{time},{deviceId}";
                                        writer.WriteLine(logLine);
                                    }
                                }
                            }

                            AddMessage("Erasing records from device...");
                            wmport.ErasureRecords();

                            DateTime now = DateTime.Now;
                            wmport.SetDateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second);
                            AddMessage($"Syncing device time to {now:yyyy-MM-dd HH:mm:ss}");

                            AddMessage("✅ Data saved and device synced", true);

                            hasFetchedRecords = true;
                        }
                        else
                        {
                            // Tambahkan pesan hanya jika belum ada sebelumnya untuk menghindari spam
                            string lastItem = listMessage.Items.Count > 0 ? listMessage.Items[listMessage.Items.Count - 1].ToString() : "";
                            if (!lastItem.StartsWith("No records found"))
                            {
                                AddMessage("No records found");
                            }

                            hasFetchedRecords = true;
                        }
                    }
                }

                // Handle perubahan koneksi
                if (wasConnected && !nowConnected)
                {
                    AddMessage($"Device disconnected");
                    lastDeviceTermno = "";
                    hasFetchedRecords = false;
                }
                else if (!nowConnected)
                {
                    string scanningMessage = "Scanning...";
                    string lastItem = listMessage.Items.Count > 0 ? listMessage.Items[listMessage.Items.Count - 1].ToString() : "";

                    if (!lastItem.StartsWith("Scanning"))
                    {
                        AddMessage(scanningMessage);
                    }
                }

                isConnected = nowConnected;
            }
            catch (Exception ex)
            {
                AddMessage($"❌ Error: {ex.Message}");
            }

            countdownRemaining = loopInterval / 1000;
            usbCheckTimer.Start();
        }

        private bool TryParseDeviceDate(string datetime, out DateTime parsedDate)
        {
            parsedDate = DateTime.MinValue;

            if (datetime.Length < 14)
                return false;

            string yearStr = datetime.Substring(0, 4);
            string monthStr = datetime.Substring(4, 2);
            string dayStr = datetime.Substring(6, 2);

            if (!int.TryParse(yearStr, out int year) ||
                !int.TryParse(monthStr, out int month) ||
                !int.TryParse(dayStr, out int day))
                return false;

            if (month < 1 || month > 12 || day < 1 || day > 31)
                return false;

            // reject tahun aneh (contoh: 2080, 2090, 20C0)
            if (year < 2000 || year > 2035)
                return false;

            try
            {
                parsedDate = new DateTime(year, month, day);
                return true;
            }
            catch
            {
                return false;
            }
        }


        private void ShowState(long result)
        {
            if (result >= 0)
                MessageBox.Show("Success");
            else
                MessageBox.Show("Failure");
        }

    }
}
