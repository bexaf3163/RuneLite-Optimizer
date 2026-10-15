using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Serialization;
using Microsoft.Win32;

namespace RuneLiteOptimizer
{
    public sealed class Preset
    {
        public string Name;
        public string Description;
        public Dictionary<string, string> Values;
        public override string ToString() { return Name; }
        public static Preset[] All()
        {
            return new[] {
                Make("Плавность · 60 FPS", "Небольшая дальность и отключённое сглаживание. Начните с этого профиля.", 25, 0, 60, "DISABLED", 1),
                Make("Баланс · 90 FPS", "Умеренная дальность. 90 FPS — цель, а не обещанная производительность.", 40, 1, 90, "MSAA_2", 2),
                Make("Экономия · 30 FPS", "Меньше графической нагрузки и энергопотребления; плавность ниже.", 20, 0, 30, "DISABLED", 1)
            };
        }
        private static Preset Make(string name, string description, int distance, int chunks, int fps, string aa, int filtering)
        {
            return new Preset { Name = name, Description = description, Values = new Dictionary<string, string> {
                {"gpu.drawDistance", distance.ToString()}, {"gpu.expandedMapLoadingChunks", chunks.ToString()},
                {"gpu.unlockFps", "true"}, {"gpu.fpsTarget", fps.ToString()}, {"gpu.vsyncMode", "OFF"},
                {"gpu.antiAliasingMode", aa}, {"gpu.anisotropicFilteringLevel", filtering.ToString()}
            }};
        }
    }

    public static class Settings
    {
        public static readonly string[] Keys = Preset.All()[0].Values.Keys.ToArray();
        // Latin-1 is a byte-preserving transport, not a reinterpretation of user text.
        public static readonly Encoding Bytes = Encoding.GetEncoding(28591);
        private static Regex Pattern(string key)
        {
            return new Regex(@"^\s*" + Regex.Escape(key) + @"\s*[=:]\s*(.*)$");
        }
        public static Dictionary<string, string> Read(byte[] bytes)
        {
            var result = new Dictionary<string, string>();
            foreach (string line in Bytes.GetString(bytes).Split('\n'))
            {
                foreach (string key in Keys)
                {
                    Match match = Pattern(key).Match(line.TrimEnd('\r'));
                    if (match.Success) result[key] = match.Groups[1].Value;
                }
            }
            return result;
        }
        public static byte[] Patch(byte[] original, Dictionary<string, string> values)
        {
            foreach (var pair in values)
                if (!Keys.Contains(pair.Key) || !Preset.All().Any(p => p.Values[pair.Key] == pair.Value))
                    throw new InvalidOperationException("Недопустимый параметр профиля.");
            string text = Bytes.GetString(original);
            if (text.Replace("\r\n", "").Contains("\n") && text.Contains("\r\n"))
                throw new InvalidOperationException("Смешанные окончания строк: используйте ручную настройку.");
            // Complex Java-properties syntax requires manual configuration, never guess.
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                string trimmed = line.TrimStart();
                int separator = line.IndexOfAny(new[] { '=', ':', ' ', '\t' });
                if (!trimmed.StartsWith("#") && !trimmed.StartsWith("!") &&
                    (separator < 0 ? line : line.Substring(0, separator)).Contains("\\"))
                    throw new InvalidOperationException("Экранированные имена параметров: используйте ручную настройку.");
                foreach (string key in values.Keys)
                {
                    string escaped = key.Replace(".", "\\.");
                    if (line.TrimStart().StartsWith(escaped, StringComparison.Ordinal) ||
                        line.TrimStart().StartsWith("\\u", StringComparison.Ordinal) ||
                        (Pattern(key).IsMatch(line) && line.EndsWith("\\", StringComparison.Ordinal)))
                        throw new InvalidOperationException("Сложный формат properties: настройте GPU вручную по памятке.");
                    if (Regex.IsMatch(line, @"^\s*" + Regex.Escape(key) + @"\s+[^=: \t]"))
                        throw new InvalidOperationException("Нестандартный разделитель properties: используйте ручную настройку.");
                }
                // A continued property could incorporate a following controlled key.
                if (line.EndsWith("\\", StringComparison.Ordinal) && !line.TrimStart().StartsWith("#") && !line.TrimStart().StartsWith("!"))
                    throw new InvalidOperationException("Файл содержит переносы properties. Автоматическое изменение отключено.");
            }
            string newline = text.Contains("\r\n") ? "\r\n" : "\n";
            string[] lines = text.Split(new[] { newline }, StringSplitOptions.None);
            var seen = new HashSet<string>();
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (var pair in values)
                {
                    if (Pattern(pair.Key).IsMatch(lines[i]))
                    {
                        lines[i] = pair.Key + "=" + pair.Value;
                        seen.Add(pair.Key);
                    }
                }
            }
            string updated = string.Join(newline, lines);
            if (updated.Length > 0 && !updated.EndsWith("\n", StringComparison.Ordinal)) updated += newline;
            foreach (var pair in values)
                if (!seen.Contains(pair.Key)) updated += pair.Key + "=" + pair.Value + newline;
            return Bytes.GetBytes(updated);
        }
        public static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
        }
        public static byte[] RestoreBytes(Backup backup, byte[] current)
        {
            if (Hash(current) != backup.AppliedHash) throw new InvalidOperationException("Настройки изменились после применения. Полный откат отменён, чтобы не затереть более новые изменения. Копия сохранена.");
            byte[] original = ProtectedData.Unprotect(backup.ProtectedOriginal, null, DataProtectionScope.CurrentUser);
            if (Hash(original) != backup.OriginalHash) throw new InvalidOperationException("Контрольная сумма резервной копии не совпадает.");
            return original;
        }
    }

    public sealed class Backup
    {
        public string Path;
        public string OriginalHash;
        public string AppliedHash;
        public byte[] ProtectedOriginal;
        public DateTime CreatedUtc;
    }

    public static class Changes
    {
        public static string BackupFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RuneLiteOptimizer", "Backups");
        private static void EnsureOffline()
        {
            if (Diagnostics.Clients().Count != 0)
                throw new InvalidOperationException("Закройте RuneLite перед изменением или откатом. Запускайте игру после этого через OSRS Path, если используете его плагин.");
            // Hidden Java processes cannot always be identified; fail closed for writes.
            if (Process.GetProcessesByName("java").Length > 0 || Process.GetProcessesByName("javaw").Length > 0)
                throw new InvalidOperationException("Работает Java-процесс. Автоматическое изменение отключено: закройте Java-приложения или используйте памятку вручную.");
        }
        public static string Apply(string path, byte[] reviewed, Dictionary<string, string> values)
        {
            EnsureOffline();
            path = System.IO.Path.GetFullPath(path);
            if (!path.EndsWith(".properties", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Выберите файл .properties активного профиля RuneLite.");
            byte[] current = File.ReadAllBytes(path);
            if (Settings.Hash(current) != Settings.Hash(reviewed)) throw new InvalidOperationException("Файл изменился после просмотра. Откройте его заново и проверьте изменения.");
            byte[] updated = Settings.Patch(current, values);
            var backup = new Backup { Path = path, OriginalHash = Settings.Hash(current), AppliedHash = Settings.Hash(updated),
                CreatedUtc = DateTime.UtcNow, ProtectedOriginal = ProtectedData.Protect(current, null, DataProtectionScope.CurrentUser) };
            Directory.CreateDirectory(BackupFolder);
            string record = System.IO.Path.Combine(BackupFolder, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N") + ".xml");
            using (var stream = File.Create(record)) new XmlSerializer(typeof(Backup)).Serialize(stream, backup);
            Replace(path, current, updated);
            return record;
        }
        private static void Replace(string path, byte[] expected, byte[] updated)
        {
            string temporary = path + ".optimizer-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, updated);
                EnsureOffline();
                if (Settings.Hash(File.ReadAllBytes(path)) != Settings.Hash(expected)) throw new InvalidOperationException("Файл изменился. Операция отменена.");
                File.Replace(temporary, path, null);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public static void Restore(string record)
        {
            EnsureOffline();
            Backup backup;
            using (var stream = File.OpenRead(record)) backup = (Backup)new XmlSerializer(typeof(Backup)).Deserialize(stream);
            byte[] current = File.ReadAllBytes(backup.Path);
            byte[] original = Settings.RestoreBytes(backup, current);
            Replace(backup.Path, current, original);
        }
    }

    public sealed class Client
    {
        public int Id;
        public string Name;
        public double MemoryMb;
        public double CpuMs;
        public override string ToString() { return Name + " · PID " + Id + " · " + MemoryMb.ToString("F0") + " МБ"; }
    }
    public static class Diagnostics
    {
        [StructLayout(LayoutKind.Sequential)]
        private sealed class MemoryStatus
        {
            public uint Length = (uint)Marshal.SizeOf(typeof(MemoryStatus));
            public uint Load;
            public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtended;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DisplayDevice
        {
            public int Size;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
            public int Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key;
        }
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatus status);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumDisplayDevices(string device, uint index, ref DisplayDevice result, uint flags);
        private static string DisplayAdapters()
        {
            try
            {
                var names = new List<string>();
                for (uint i = 0; i < 16; i++)
                {
                    var adapter = new DisplayDevice { Size = Marshal.SizeOf(typeof(DisplayDevice)) };
                    if (!EnumDisplayDevices(null, i, ref adapter, 0)) break;
                    if (!string.IsNullOrWhiteSpace(adapter.Description)) names.Add(adapter.Description);
                }
                return names.Count == 0 ? "Недоступно" : string.Join(", ", names.Distinct().ToArray()) + " (Windows Display API)";
            }
            catch { return "Недоступно"; }
        }
        public static List<Client> Clients()
        {
            var clients = new List<Client>();
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    string name = process.ProcessName;
                    if (process.Id == Process.GetCurrentProcess().Id || name.Equals("RuneLite-Optimizer", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!(name.IndexOf("runelite", StringComparison.OrdinalIgnoreCase) >= 0 || name == "java" || name == "javaw")) continue;
                    bool matched = name.IndexOf("runelite", StringComparison.OrdinalIgnoreCase) >= 0 || process.MainWindowTitle.IndexOf("RuneLite", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!matched)
                    {
                        try { matched = process.MainModule.FileName.IndexOf("runelite", StringComparison.OrdinalIgnoreCase) >= 0; } catch { }
                    }
                    if (matched) clients.Add(new Client { Id = process.Id, Name = name, MemoryMb = process.WorkingSet64 / 1048576.0, CpuMs = process.TotalProcessorTime.TotalMilliseconds });
                }
                catch { }
                finally { process.Dispose(); }
            }
            return clients;
        }
        private static string Query(string query, string field)
        {
            try
            {
                var options = new EnumerationOptions { Timeout = TimeSpan.FromSeconds(3), ReturnImmediately = false };
                using (var search = new ManagementObjectSearcher(new ManagementScope("root\\cimv2"), new ObjectQuery(query), options))
                using (var rows = search.Get())
                {
                    var values = new List<string>();
                    foreach (ManagementObject row in rows) { using (row) { values.Add(Convert.ToString(row[field])); } }
                    return string.Join(", ", values.ToArray());
                }
            }
            catch { return "Недоступно (WMI)"; }
        }
        public static string Report()
        {
            string cpu = "Не определён";
            try { using (var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0")) cpu = Convert.ToString(key.GetValue("ProcessorNameString")); } catch { }
            string ram = Query("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem", "TotalPhysicalMemory");
            double ramBytes;
            if (double.TryParse(ram, out ramBytes)) ram = (ramBytes / 1073741824.0).ToString("F1") + " ГБ";
            else
            {
                try { var memory = new MemoryStatus(); if (GlobalMemoryStatusEx(memory)) ram = (memory.TotalPhysical / 1073741824.0).ToString("F1") + " ГБ · свободно " + (memory.AvailablePhysical / 1073741824.0).ToString("F1") + " ГБ"; } catch { }
            }
            string gpu = Query("SELECT Name FROM Win32_VideoController", "Name");
            if (gpu.StartsWith("Недоступно")) gpu = DisplayAdapters();
            var before = Clients();
            var stopwatch = Stopwatch.StartNew();
            System.Threading.Thread.Sleep(1000);
            var after = Clients();
            double elapsed = stopwatch.Elapsed.TotalMilliseconds;
            var lines = new List<string> { "ДИАГНОСТИКА · RuneLite Optimizer 0.1.0", "",
                "Windows: " + Environment.OSVersion.VersionString, "CPU: " + cpu.Trim(), "Логических процессоров: " + Environment.ProcessorCount,
                "Оперативная память: " + ram, "Видеоадаптер: " + gpu,
                "Драйвер GPU: " + Query("SELECT DriverVersion FROM Win32_VideoController", "DriverVersion"), "",
                "ПРОЦЕССЫ RUNELITE · замер около 1 секунды" };
            foreach (var client in after)
            {
                var first = before.FirstOrDefault(c => c.Id == client.Id);
                string cpuLoad = first == null ? "неизвестно" : Math.Max(0, Math.Min(100, (client.CpuMs - first.CpuMs) / elapsed / Environment.ProcessorCount * 100)).ToString("F1") + "% всей системы";
                lines.Add(client.ToString() + " · CPU " + cpuLoad);
            }
            if (after.Count == 0) lines.Add("Клиент не найден либо процесс недоступен. Игра автоматически не запускается.");
            lines.AddRange(new[] { "", "FPS / frametime: нет прямой телеметрии. Включите Draw FPS в RuneLite и проведите сравнение ниже.",
                "Температуры, загрузка GPU, VRAM, OpenGL и причина лагов этим замером не определяются.", "",
                "РЕКОМЕНДАЦИИ",
                "1. Для обычного рендера проверьте GPU plugin. С 117 HD параметры GPU plugin не являются настройками HD.",
                "2. Выберите профиль и просмотрите план. Начните с меньшей дальности и без MSAA.",
                "3. Проверьте FPS Control: его Global FPS limit может ограничивать выбранную цель GPU.",
                "4. В GPU Unlock FPS снимает ограничение камеры; высокий целевой FPS не гарантирует его достижение.",
                "5. При 117 HD вручную сравните меньшую дальность, тени и сглаживание. Программа не отключает плагины.",
                "6. Сравнивайте одну сцену/мир/размер окна и одинаковую камеру до и после. Сетевую задержку FPS-настройки не исправляют.",
                "7. Сохраняйте запуск через OSRS Path, если он нужен вашему плагину. Safe mode для этой задачи не включается автоматически."
            });
            return string.Join(Environment.NewLine, lines.ToArray());
        }
    }

    public sealed class MainWindow : Form
    {
        private readonly Color background = Color.FromArgb(13, 18, 27), surface = Color.FromArgb(24, 32, 45), foreground = Color.FromArgb(230, 235, 245);
        private ComboBox presets;
        private TextBox plan, report, configPath;
        private Button scan, apply;
        private Label status;
        private byte[] reviewed;
        private NumericUpDown beforeFps, afterFps;
        public MainWindow()
        {
            Text = "RuneLite Optimizer 0.1.0"; Size = new Size(1080, 790); MinimumSize = new Size(800, 620);
            BackColor = background; ForeColor = foreground; Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), RowCount = 4, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            var title = new Label { Text = "RuneLite Optimizer", Font = new Font("Segoe UI Semibold", 23), Dock = DockStyle.Fill, AutoSize = false };
            layout.Controls.Add(title, 0, 0);
            layout.Controls.Add(new Label { Text = "Диагностика · Профили GPU · Откат изменений", Dock = DockStyle.Fill }, 0, 1);
            var tabs = new TabControl { Dock = DockStyle.Fill, Font = Font };
            var overview = Page("Диагностика"); var profiles = Page("Настройки GPU"); var comparison = Page("До / после");
            tabs.TabPages.AddRange(new[] { overview, profiles, comparison });
            var overviewLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            overviewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); overviewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var tools = new FlowLayoutPanel { Dock = DockStyle.Fill };
            scan = Button("Проверить компьютер", delegate { Scan(); });
            tools.Controls.Add(scan); tools.Controls.Add(Button("Сохранить отчёт", delegate { SaveReport(); }));
            overviewLayout.Controls.Add(tools, 0, 0); report = TextArea(); report.Text = "Нажмите «Проверить компьютер». Личные настройки и игровые данные не отправляются в сеть.";
            overviewLayout.Controls.Add(report, 0, 1); overview.Controls.Add(overviewLayout);

            var profileLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1 };
            profiles.AutoScroll = true;
            profileLayout.Dock = DockStyle.Top;
            profileLayout.AutoSize = true;
            profileLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            profileLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); profileLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 65));
            profileLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); profileLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); profileLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
            profileLayout.RowStyles[1].SizeType = SizeType.Absolute;
            profileLayout.RowStyles[1].Height = 65;
            profileLayout.RowStyles[2].SizeType = SizeType.AutoSize;
            profileLayout.RowStyles[4].SizeType = SizeType.AutoSize;
            profileLayout.RowStyles[3].SizeType = SizeType.Absolute;
            profileLayout.RowStyles[3].Height = 240;
            presets = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = surface, ForeColor = foreground };
            presets.Items.AddRange(Preset.All()); presets.SelectedIndex = 0; presets.SelectedIndexChanged += delegate { RefreshPlan(); }; profileLayout.Controls.Add(presets, 0, 0);
            profileLayout.Controls.Add(new Label { Text = "Стандартный GPU plugin · не 117 HD.\nВыберите файл активного профиля. Детали — в памятке.", Dock = DockStyle.Fill, AutoSize = false, Padding = new Padding(0, 8, 0, 8) }, 0, 1);
            var fileRow = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink }; fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            configPath = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = surface, ForeColor = foreground }; fileRow.Controls.Add(configPath, 0, 0);
            fileRow.Controls.Add(Button("Выбрать файл", delegate { SelectConfig(); }), 1, 0); profileLayout.Controls.Add(fileRow, 0, 2);
            plan = TextArea(); profileLayout.Controls.Add(plan, 0, 3);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(0, 5, 0, 5) };
            profileLayout.SizeChanged += delegate { actions.MaximumSize = new Size(Math.Max(100, profileLayout.ClientSize.Width), 0); };
            apply = Button("Применить к файлу", delegate { Apply(); }); apply.Enabled = false; actions.Controls.Add(apply);
            actions.Controls.Add(Button("Откатить изменения", delegate { Restore(); }));
            actions.Controls.Add(Button("Копировать памятку", delegate { Clipboard.SetText(plan.Text); status.Text = "Памятка скопирована."; }));
            profileLayout.Controls.Add(actions, 0, 4); profiles.Controls.Add(profileLayout);

            var compareLayout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(12) };
            compareLayout.Controls.Add(new Label { Text = "Сравните средний FPS одной сцены", AutoSize = true, Font = new Font("Segoe UI Semibold", 16) });
            compareLayout.Controls.Add(new Label { Text = "Это ручной замер, не автоматическая телеметрия.\nВ RuneLite включите Draw FPS. Наблюдайте одинаковую сцену около 60 секунд.\nСохраните мир, размер окна и камеру. Повторите после применения профиля.\nНе сравнивайте средний FPS с мгновенным максимумом.", AutoSize = true, Margin = new Padding(3, 15, 3, 18) });
            beforeFps = Numeric(); afterFps = Numeric();
            compareLayout.Controls.Add(new Label { Text = "Средний FPS до", AutoSize = true }); compareLayout.Controls.Add(beforeFps);
            compareLayout.Controls.Add(new Label { Text = "Средний FPS после", AutoSize = true }); compareLayout.Controls.Add(afterFps);
            var result = new Label { Text = "Введите результаты двух замеров.", AutoSize = true, MaximumSize = new Size(650, 0) };
            compareLayout.Controls.Add(Button("Сравнить", delegate {
                if (beforeFps.Value <= 0 || afterFps.Value <= 0) { result.Text = "Нужны два ненулевых замера."; return; }
                decimal change = (afterFps.Value / beforeFps.Value - 1) * 100;
                result.Text = "Разница: " + change.ToString("+0.0;-0.0;0.0") + "%. Это сравнение введённых значений, а не доказательство причины изменения. Повторите замер для проверки стабильности.";
            })); compareLayout.Controls.Add(result); comparison.Controls.Add(compareLayout);
            layout.Controls.Add(tabs, 0, 2); status = new Label { Text = "Готово. Изменения ещё не применялись.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true }; layout.Controls.Add(status, 0, 3);
            Controls.Add(layout); RefreshPlan();
        }
        private NumericUpDown Numeric() { return new NumericUpDown { Minimum = 0, Maximum = 9999, DecimalPlaces = 1, Width = 160, BackColor = surface, ForeColor = foreground }; }
        private TabPage Page(string name) { return new TabPage(name) { BackColor = background, ForeColor = foreground, Padding = new Padding(14) }; }
        private TextBox TextArea() { return new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, BackColor = surface, ForeColor = foreground, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 10), WordWrap = true }; }
        private Button Button(string text, EventHandler handler)
        {
            var button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(130, 36), FlatStyle = FlatStyle.Flat, BackColor = surface, ForeColor = foreground, Padding = new Padding(7, 3, 7, 3), Cursor = Cursors.Hand };
            button.Click += handler; return button;
        }
        private void Error(Exception error) { status.Text = "Операция не выполнена."; MessageBox.Show(this, error.Message, "RuneLite Optimizer", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        private void Scan()
        {
            scan.Enabled = false; status.Text = "Проверяю доступные показатели…";
            Task.Factory.StartNew(delegate { return Diagnostics.Report(); }).ContinueWith(task => {
                if (IsDisposed || !IsHandleCreated) return;
                try { BeginInvoke((Action)delegate { scan.Enabled = true; if (task.IsFaulted) Error(task.Exception.GetBaseException()); else { report.Text = task.Result; status.Text = "Диагностика готова. Недоступные показатели не заменены догадками."; } }); } catch (InvalidOperationException) { }
            });
        }
        private void SaveReport()
        {
            using (var dialog = new SaveFileDialog { Filter = "Текстовый отчёт|*.txt", FileName = "RuneLite-diagnostics.txt" })
                if (dialog.ShowDialog(this) == DialogResult.OK) try { File.WriteAllText(dialog.FileName, report.Text, new UTF8Encoding(true)); status.Text = "Отчёт сохранён локально."; } catch (Exception e) { Error(e); }
        }
        private void SelectConfig()
        {
            using (var dialog = new OpenFileDialog { Filter = "RuneLite properties|*.properties", InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".runelite"), Title = "Выберите файл активного локального профиля RuneLite" })
                if (dialog.ShowDialog(this) == DialogResult.OK) try { reviewed = File.ReadAllBytes(dialog.FileName); configPath.Text = dialog.FileName; RefreshPlan(); apply.Enabled = true; } catch (Exception e) { reviewed = null; apply.Enabled = false; Error(e); }
        }
        private void RefreshPlan()
        {
            if (plan == null) return;
            var preset = (Preset)presets.SelectedItem;
            var current = reviewed == null ? new Dictionary<string, string>() : Settings.Read(reviewed);
            var lines = new List<string> { preset.Description, "", "ПЛАН ИЗМЕНЕНИЙ · только семь параметров GPU" };
            var labels = new Dictionary<string, string> {
                { "gpu.drawDistance", "Дальность прорисовки" }, { "gpu.expandedMapLoadingChunks", "Дополнительная загрузка карты" },
                { "gpu.unlockFps", "Разблокировать FPS камеры" }, { "gpu.fpsTarget", "Целевой FPS" },
                { "gpu.vsyncMode", "Вертикальная синхронизация" }, { "gpu.antiAliasingMode", "Сглаживание" },
                { "gpu.anisotropicFilteringLevel", "Анизотропная фильтрация" }
            };
            foreach (var pair in preset.Values)
            {
                string existing; if (!current.TryGetValue(pair.Key, out existing)) existing = reviewed == null ? "файл не выбран" : "не задано / значение по умолчанию";
                lines.Add(labels[pair.Key] + ": " + existing + " → " + pair.Value);
            }
            lines.AddRange(new[] { "", "Перед применением закройте RuneLite. Остальные параметры сохраняются.",
                "Резервная копия шифруется Windows для текущего пользователя. Откат откажется затирать более новые настройки.",
                "GPU plugin должен быть включён вручную и поддерживаться драйвером. FPS Control может задавать дополнительный лимит.",
                "Если используете 117 HD: настройте дальность/тени/AA в самом HD, этот профиль к нему не применяется.",
                "После применения запустите игру обычным способом, через OSRS Path, если используете его. Проверьте значения в GPU: активный/облачный профиль может их заменить.",
                "Рост FPS не гарантируется; проверьте сравнение до / после. Реестр, службы, драйверы и приоритеты процессов не меняются." });
            plan.Text = string.Join(Environment.NewLine, lines.ToArray());
        }
        private void Apply()
        {
            if (reviewed == null) return;
            if (MessageBox.Show(this, "Изменить семь параметров GPU в выбранном файле?\n\nRuneLite должен быть закрыт. Убедитесь, что выбран активный локальный профиль стандартного GPU plugin. Перед записью будет создана зашифрованная резервная копия.", "Применить профиль", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try { Changes.Apply(configPath.Text, reviewed, ((Preset)presets.SelectedItem).Values); reviewed = File.ReadAllBytes(configPath.Text); RefreshPlan(); status.Text = "Профиль записан. Запустите клиент и проверьте GPU-настройки; рост FPS ещё не измерен."; } catch (Exception e) { Error(e); }
        }
        private void Restore()
        {
            using (var dialog = new OpenFileDialog { Filter = "Резервная копия Optimizer|*.xml", InitialDirectory = Changes.BackupFolder })
                if (dialog.ShowDialog(this) == DialogResult.OK && MessageBox.Show(this, "Вернуть настройки из выбранной копии? RuneLite должен быть закрыт. Если файл менялся после применения, откат будет отменён.", "Откат", MessageBoxButtons.YesNo) == DialogResult.Yes)
                    try { Changes.Restore(dialog.FileName); reviewed = null; configPath.Clear(); apply.Enabled = false; RefreshPlan(); status.Text = "Исходный файл восстановлен."; } catch (Exception e) { Error(e); }
        }
        public void RenderSmoke(string folder)
        {
            Directory.CreateDirectory(folder);
            ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; Location = new Point(-20000, -20000);
            Show();
            var tabs = Controls[0].Controls.OfType<TabControl>().First();
            foreach (Size size in new[] { new Size(1080, 790), new Size(800, 620) })
            {
                Size = size;
                for (int i = 0; i < tabs.TabPages.Count; i++)
                {
                    tabs.SelectedIndex = i; PerformLayout(); Application.DoEvents();
                    using (var bitmap = new Bitmap(Width, Height))
                    {
                        DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
                        bitmap.Save(Path.Combine(folder, "ui-" + size.Width + "-" + i + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
            }
        }
    }

    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Contains("--self-test"))
            {
                try { SelfTest.Run(); return 0; } catch (Exception e) { File.WriteAllText("self-test-failure.txt", e.ToString()); return 1; }
            }
            if (args.Contains("--diagnose"))
            {
                File.WriteAllText("diagnostics.txt", Diagnostics.Report(), new UTF8Encoding(true));
                return 0;
            }
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if (args.Contains("--ui-smoke"))
            {
                using (var window = new MainWindow()) window.RenderSmoke("test-results");
                return 0;
            }
            Application.Run(new MainWindow()); return 0;
        }
    }
    public static class SelfTest
    {
        private static int count;
        private static void Check(bool value, string name) { if (!value) throw new Exception(name); count++; }
        public static void Run()
        {
            var preset = Preset.All()[0].Values;
            Check(Diagnostics.Clients().All(c => c.Id != Process.GetCurrentProcess().Id), "Optimizer is never its own game client");
            byte[] fixture = Encoding.UTF8.GetBytes("# Тест\r\nother.secret=untouched\r\ngpu.drawDistance=90\r\ngpu.drawDistance=80\r\ncustom.osrsPath=true\r\n");
            byte[] patched = Settings.Patch(fixture, preset);
            string updated = Encoding.UTF8.GetString(patched);
            Check(updated.StartsWith("# Тест\r\nother.secret=untouched\r\n"), "Preserve UTF-8 and unrelated bytes");
            Check(updated.Contains("custom.osrsPath=true\r\n"), "Preserve other plugin settings");
            Check(Settings.Read(patched)["gpu.drawDistance"] == "25", "Change selected value");
            Check(updated.Split(new[] { "gpu.drawDistance=25" }, StringSplitOptions.None).Length == 3, "Handle duplicate keys consistently");
            Check(Settings.Hash(patched) == Settings.Hash(Settings.Patch(patched, preset)), "Patch is idempotent");
            Check(Settings.Read(patched).Count == 7, "Only allowlisted values read");
            Check(Settings.Read(Settings.Patch(Settings.Bytes.GetBytes("unrelated=value"), preset)).Count == 7, "Append keys to file without final newline");
            Check(!Settings.Bytes.GetString(Settings.Patch(Settings.Bytes.GetBytes("# gpu.drawDistance=90\n"), preset)).Contains("\r\n"), "Preserve LF");
            bool rejected = false;
            try { Settings.Patch(fixture, new Dictionary<string, string> { { "runelite.gpu", "true" } }); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Reject non-allowlisted plugin activation");
            rejected = false;
            try { Settings.Patch(fixture, new Dictionary<string, string> { { "gpu.fpsTarget", "999999" } }); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Reject out-of-policy values");
            rejected = false;
            try { Settings.Patch(Settings.Bytes.GetBytes("gpu.drawDistance=5\\\ncontinued\n"), preset); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Reject continued properties");
            rejected = false;
            try { Settings.Patch(Settings.Bytes.GetBytes("gpu.drawDistance 90\n"), preset); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Reject unsupported separator");
            rejected = false;
            try { Settings.Patch(Settings.Bytes.GetBytes("g\\u0070u.drawDistance=90\n"), preset); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Reject escaped keys");
            rejected = false;
            try { Settings.Patch(Settings.Bytes.GetBytes("gpu.drawDistance=90\r\nother=1\n"), preset); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Reject mixed line endings");
            byte[] encrypted = ProtectedData.Protect(fixture, null, DataProtectionScope.CurrentUser);
            Check(Settings.Hash(ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser)) == Settings.Hash(fixture), "Backup encryption roundtrip");
            var restoreFixture = new Backup { ProtectedOriginal = encrypted, OriginalHash = Settings.Hash(fixture), AppliedHash = Settings.Hash(patched) };
            Check(Settings.Hash(Settings.RestoreBytes(restoreFixture, patched)) == Settings.Hash(fixture), "Exact restoration");
            rejected = false;
            try { Settings.RestoreBytes(restoreFixture, fixture); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Reject restoration over later edits");
            var serializer = new XmlSerializer(typeof(Backup));
            using (var stream = new MemoryStream())
            {
                serializer.Serialize(stream, new Backup { Path = "fixture.properties", ProtectedOriginal = encrypted, OriginalHash = Settings.Hash(fixture), AppliedHash = Settings.Hash(patched) });
                stream.Position = 0; var back = (Backup)serializer.Deserialize(stream);
                Check(back.AppliedHash == Settings.Hash(patched), "Backup metadata roundtrip");
            }
            string transactionResult = "Skipped offline-write transactions: a game/Java process is running.";
            if (Diagnostics.Clients().Count == 0 && Process.GetProcessesByName("java").Length == 0 && Process.GetProcessesByName("javaw").Length == 0)
            {
                string originalFolder = Changes.BackupFolder;
                string fixtureFolder = Path.Combine(Environment.CurrentDirectory, "transactions-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(fixtureFolder);
                try
                {
                    Changes.BackupFolder = Path.Combine(fixtureFolder, "backups");
                    string file = Path.Combine(fixtureFolder, "fixture.properties");
                    File.WriteAllBytes(file, fixture);
                    string record = Changes.Apply(file, fixture, preset);
                    Check(File.Exists(record) && Settings.Hash(File.ReadAllBytes(file)) == Settings.Hash(patched), "Apply creates encrypted backup and writes expected bytes");
                    Changes.Restore(record);
                    Check(Settings.Hash(File.ReadAllBytes(file)) == Settings.Hash(fixture), "Full offline apply/restore transaction");
                    record = Changes.Apply(file, fixture, preset);
                    File.AppendAllText(file, "user.laterEdit=true\n");
                    string laterHash = Settings.Hash(File.ReadAllBytes(file));
                    rejected = false;
                    try { Changes.Restore(record); } catch (InvalidOperationException) { rejected = true; }
                    Check(rejected && Settings.Hash(File.ReadAllBytes(file)) == laterHash, "Restore conflict preserves later user changes");
                    rejected = false;
                    try { Changes.Apply(file, fixture, preset); } catch (InvalidOperationException) { rejected = true; }
                    Check(rejected && Settings.Hash(File.ReadAllBytes(file)) == laterHash, "Stale preview cannot overwrite file");
                    transactionResult = "Offline-write transaction tests passed on isolated fixtures.";
                }
                finally { Changes.BackupFolder = originalFolder; }
            }
            File.WriteAllText("self-test-result.txt", "PASS: " + count + " checks. No real RuneLite settings changed.\n" + transactionResult);
        }
    }
}
