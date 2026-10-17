using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Bladez.Core;

namespace Bladez.Host;

public sealed class HostForm : Form
{
    private readonly Color blue = Color.FromArgb(25, 105, 255), ink = Color.FromArgb(20, 30, 48), muted = Color.FromArgb(106, 119, 140);
    private readonly Panel body = new() { Dock = DockStyle.Fill, Padding = new Padding(36, 22, 36, 22), AutoScroll = true };
    private readonly TextBox log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Bottom, Height = 105, BorderStyle = BorderStyle.None, BackColor = Color.FromArgb(241, 245, 251), ForeColor = Color.FromArgb(70, 86, 110) };
    private readonly Label status = new() { AutoSize = true, Text = "●  Остановлен", ForeColor = Color.FromArgb(106, 119, 140) };
    private readonly NotifyIcon tray = new() { Icon = SystemIcons.Application, Text = "Bladez Host", Visible = false };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 4000 };
    private readonly string[] args;
    private string profile = HostSettings.DataDirectory;
    private ServerManager manager = null!;
    private bool busy, closing;
    private TextBox shop = null!, ownerName = null!, login = null!, password = null!, repeat = null!;
    private NumericUpDown port = null!, dbPort = null!;
    private CheckBox catalog = null!;

    public HostForm(string[] arguments)
    {
        args = arguments;
        var selection = Path.Combine(HostSettings.DataDirectory, "active-profile.txt");
        if (File.Exists(selection)) profile = Path.GetFullPath(File.ReadAllText(selection).Trim());
        Text = "Bladez Host — сервер вашего барбершопа"; StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1000, Math.Min(900, Screen.PrimaryScreen!.WorkingArea.Height - 70)); MinimumSize = new Size(860, 640); BackColor = Color.White;
        Font = new Font("Segoe UI", 10); ForeColor = ink;
        var header = new Panel { Dock = DockStyle.Top, Height = 110, BackColor = ink, Padding = new Padding(36, 24, 36, 16) };
        header.Controls.Add(new Label { Text = "BLADEZ  /  HOST", Font = new Font("Segoe UI", 24, FontStyle.Bold), ForeColor = Color.White, AutoSize = true, Location = new Point(32, 20) });
        header.Controls.Add(new Label { Text = "Отдельная база. Ваши данные. Понятное управление.", ForeColor = Color.FromArgb(181, 198, 224), AutoSize = true, Location = new Point(36, 73) });
        Controls.Add(body); Controls.Add(log); Controls.Add(header);
        tray.ContextMenuStrip = new ContextMenuStrip(); tray.ContextMenuStrip.Items.Add("Открыть Bladez Host", null, (_, _) => RestoreWindow());
        tray.ContextMenuStrip.Items.Add("Остановить и выйти", null, async (_, _) => await ExitAsync()); tray.DoubleClick += (_, _) => RestoreWindow();
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) { tray.Visible = true; Hide(); } };
        FormClosing += (_, e) => { if (!closing) { e.Cancel = true; if (!busy) _ = ExitAsync(); } };
        timer.Tick += (_, _) => UpdateStatus();
        ResetManager();
        Shown += async (_, _) =>
        {
            if (args.Length == 2 && args[0] == "--capture-setup")
            {
                using var image = new Bitmap(Width, Height); DrawToBitmap(image, new Rectangle(Point.Empty, Size)); image.Save(Path.GetFullPath(args[1])); closing = true; Close(); return;
            }
            if (manager.Configured) await Run(async () => { await RuntimeCheck.EnsureAsync(this); await manager.StartAsync(); });
            if (args.Length == 2 && args[0] == "--capture-dashboard")
            {
                UpdateStatus(); using var image = new Bitmap(Width, Height); DrawToBitmap(image, new Rectangle(Point.Empty, Size)); image.Save(Path.GetFullPath(args[1])); await manager.StopAsync(); closing = true; Close(); return;
            }
            timer.Start();
        };
    }

    private void ResetManager()
    {
        manager = new ServerManager(profile, Path.Combine(AppContext.BaseDirectory, "server"), new Progress<string>(WriteLog));
        if (manager.Configured) ShowDashboard(); else ShowSetup();
    }
    private void WriteLog(string text) { if (IsDisposed) return; log.AppendText($"{DateTime.Now:HH:mm}  {text}{Environment.NewLine}"); }
    private void Title(string title, string subtitle)
    {
        body.Controls.Clear();
        var heading = new Label { Text = title, Font = new Font(Font.FontFamily, 22, FontStyle.Bold), AutoSize = true, Location = new Point(34, 20) };
        var description = new Label { Text = subtitle, ForeColor = muted, AutoSize = false, Size = new Size(890, 48), Location = new Point(36, 69) };
        body.Controls.Add(heading); body.Controls.Add(description);
    }
    private TextBox Field(string label, int x, int y, string text = "", bool secret = false)
    {
        body.Controls.Add(new Label { Text = label, AutoSize = true, Location = new Point(x, y) });
        var input = new TextBox { Text = text, Location = new Point(x, y + 26), Width = 420, UseSystemPasswordChar = secret, MaxLength = secret ? 128 : 100, AccessibleName = label };
        body.Controls.Add(input); return input;
    }
    private Button Button(string text, int x, int y, EventHandler click, bool primary = false, int width = 245)
    {
        var button = new Button { Text = text, Location = new Point(x, y), Size = new Size(width, 44), FlatStyle = FlatStyle.Flat,
            BackColor = primary ? blue : Color.White, ForeColor = primary ? Color.White : ink, Cursor = Cursors.Hand };
        button.FlatAppearance.BorderColor = primary ? blue : Color.FromArgb(214, 222, 234); button.Click += click; body.Controls.Add(button); return button;
    }
    private void ShowSetup()
    {
        Title("Создадим ваш сервер", "Один раз укажите владельца. MySQL, база, ключи и схема настроятся автоматически.\nНужен интернет для первой загрузки MySQL (~281 МБ); приложение .NET уже включено.");
        shop = Field("Название барбершопа", 36, 132, "Bladez"); ownerName = Field("Имя владельца", 500, 132);
        login = Field("Логин владельца (латиница, 3–40 символов)", 36, 210); login.MaxLength = 40;
        password = Field("Пароль: от 12 символов, Aa и цифры", 36, 288, secret: true); repeat = Field("Повторите пароль", 500, 288, secret: true);
        body.Controls.Add(new Label { Text = "Порт сайта", AutoSize = true, Location = new Point(500, 210) });
        port = new NumericUpDown { Location = new Point(500, 238), Minimum = 1024, Maximum = 65535, Value = 5088, Width = 160 }; body.Controls.Add(port);
        body.Controls.Add(new Label { Text = "Порт отдельного MySQL", AutoSize = true, Location = new Point(700, 210) });
        dbPort = new NumericUpDown { Location = new Point(700, 238), Minimum = 1024, Maximum = 65535, Value = 33389, Width = 200 }; body.Controls.Add(dbPort);
        catalog = new CheckBox { Text = "Добавить пример каталога: 2 мастера и 3 услуги (без клиентов и записей)", AutoSize = true, Checked = true, Location = new Point(36, 374) }; body.Controls.Add(catalog);
        body.Controls.Add(new Label { Text = "Локальный режим: сайт доступен на этом компьютере. Для публичного сайта с HTTPS\nиспользуйте комплект Linux/VPS или настройку обратного прокси из руководства.", ForeColor = muted, AutoSize = true, Location = new Point(36, 418) });
        Button("Настроить и запустить →", 36, 488, async (_, _) => await Run(Initialize), true, 290);
        Button("Восстановить копию…", 350, 488, async (_, _) => await Run(RestoreBackup), width: 260);
        body.Controls.Add(new Label { Text = "Папка данных: " + profile, ForeColor = muted, AutoSize = false, AutoEllipsis = true, Size = new Size(890, 48), Location = new Point(36, 553) });
    }
    private async Task Initialize()
    {
        if (!Regex.IsMatch(login.Text, "^[a-zA-Z0-9._-]{3,40}$") || string.IsNullOrWhiteSpace(shop.Text) || string.IsNullOrWhiteSpace(ownerName.Text)) throw new ArgumentException("Укажите название, имя и корректный логин.");
        if (password.Text != repeat.Text || password.Text.Length < 12 || !Regex.IsMatch(password.Text, "[a-z]") || !Regex.IsMatch(password.Text, "[A-Z]") || !Regex.IsMatch(password.Text, "[0-9]") || password.Text.Distinct().Count() < 5) throw new ArgumentException("Пароли должны совпадать и содержать не менее 12 символов, большие и маленькие латинские буквы, цифры и 5 разных символов.");
        if (port.Value == dbPort.Value) throw new ArgumentException("Порты сайта и MySQL должны отличаться.");
        await RuntimeCheck.EnsureAsync(this);
        var owner = new { UserName = login.Text, DisplayName = ownerName.Text.Trim(), Password = password.Text, ShopName = shop.Text.Trim(), SeedCatalog = catalog.Checked };
        var settings = new HostSettings { ListenUrl = "http://127.0.0.1:" + port.Value, PublicUrl = "http://127.0.0.1:" + port.Value, DatabasePort = (int)dbPort.Value };
        await manager.InitializeAsync(owner, settings);
        password.Clear(); repeat.Clear(); ShowDashboard();
    }
    private void ShowDashboard()
    {
        Title("Сервер под контролем", "Windows Host работает, пока открыто это окно или значок в трее. Для круглосуточной\nработы без входа в Windows используйте Linux/VPS из комплекта релиза.");
        status.Location = new Point(36, 140); status.Font = new Font(Font.FontFamily, 17, FontStyle.Bold); body.Controls.Add(status);
        body.Controls.Add(new Label { Text = manager.Settings.PublicUrl, Location = new Point(36, 196), Font = new Font(Font.FontFamily, 15), AutoSize = true, ForeColor = blue });
        body.Controls.Add(new Label { Text = "Веб-сайт + отдельный MySQL 8.4\nAES-256-GCM для контактов • TLS к базе • индивидуальный пароль владельца\n\nДанные хранятся здесь:\n" + profile, AutoSize = false, AutoEllipsis = true, Size = new Size(890, 120), ForeColor = muted, Location = new Point(36, 245) });
        Button("Открыть сайт ↗", 36, 370, (_, _) => { if (manager.WebRunning) Process.Start(new ProcessStartInfo(manager.Settings.PublicUrl) { UseShellExecute = true }); else MessageBox.Show("Сначала запустите сервер."); }, true);
        Button("Запустить", 307, 370, async (_, _) => await Run(async () => await manager.StartAsync()));
        Button("Остановить", 578, 370, async (_, _) => await Run(async () => { await manager.StopAsync(); WriteLog("Сайт и отдельная база остановлены."); }));
        Button("Создать резервную копию…", 36, 438, async (_, _) => await Run(CreateBackup), width: 300);
        Button("Открыть папку данных", 362, 438, (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", profile) { UseShellExecute = true }), width: 280);
        Button("Свернуть в трей", 668, 438, (_, _) => WindowState = FormWindowState.Minimized, width: 230);
        body.Controls.Add(new Label { Text = "Резервная копия зашифрована отдельным паролем. На время копирования сервер\nостанавливается. Сохраните копию и пароль в разных местах; без них данные не восстановить.", AutoSize = true, ForeColor = muted, Location = new Point(36, 515) });
        Button("Восстановить доступ владельца…", 36, 575, async (_, _) => await Run(RecoverOwner), width: 360);
        UpdateStatus();
    }
    private void UpdateStatus()
    {
        if (!manager.Configured) return;
        var ready = manager.WebRunning && manager.DatabaseRunning;
        status.Text = ready ? "●  Работает: сайт и база" : "●  Остановлен / требуется запуск";
        status.ForeColor = ready ? Color.FromArgb(16, 139, 89) : muted;
    }
    private async Task Run(Func<Task> action)
    {
        if (busy) return; busy = true; body.Enabled = false; UseWaitCursor = true;
        try { await action(); }
        catch (Exception error) { WriteLog("Операция не завершена. Данные сохранены."); MessageBox.Show(error is CryptographicException ? "Неверный пароль копии или файл повреждён. Данные не изменены." : error.Message, "Bladez Host", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { busy = false; body.Enabled = true; UseWaitCursor = false; UpdateStatus(); }
    }
    private async Task CreateBackup()
    {
        using var save = new SaveFileDialog { Filter = "Bladez encrypted backup (*.bladez)|*.bladez", FileName = "Bladez-" + DateTime.Now.ToString("yyyy-MM-dd-HHmm") + ".bladez", OverwritePrompt = true };
        if (save.ShowDialog(this) != DialogResult.OK) return;
        if (File.Exists(save.FileName)) throw new ArgumentException("Выберите новое имя файла: существующие копии не перезаписываются.");
        var passphrase = AskPassword("Пароль новой копии", true); if (passphrase is null) return;
        var restart = manager.WebRunning;
        await manager.StopAsync();
        try { WriteLog("Создаём зашифрованную копию…"); await Task.Run(() => ProfileBackup.Create(profile, save.FileName, passphrase)); WriteLog("Копия сохранена. Проверьте восстановление в отдельной папке."); }
        finally { if (restart) await manager.StartAsync(); }
    }
    private async Task RestoreBackup()
    {
        using var open = new OpenFileDialog { Filter = "Bladez encrypted backup (*.bladez)|*.bladez" }; if (open.ShowDialog(this) != DialogResult.OK) return;
        using var folder = new FolderBrowserDialog { Description = "Выберите новую пустую папку для восстановления. Старые данные не перезаписываются.", UseDescriptionForTitle = true };
        if (folder.ShowDialog(this) != DialogResult.OK) return;
        var passphrase = AskPassword("Пароль резервной копии", false); if (passphrase is null) return;
        WriteLog("Проверяем и восстанавливаем копию…");
        await Task.Run(() => ProfileBackup.Restore(open.FileName, folder.SelectedPath, passphrase));
        profile = folder.SelectedPath;
        // Persist selection privately for the next launch; the profile contains its own OS-wrapped key.
        PrivateFiles.Directory(HostSettings.DataDirectory);
        PrivateFiles.Write(Path.Combine(HostSettings.DataDirectory, "active-profile.txt"), System.Text.Encoding.UTF8.GetBytes(profile));
        ResetManager(); await manager.StartAsync();
    }
    private string? AskPassword(string title, bool confirm)
    {
        using var dialog = new Form { Text = title, ClientSize = new Size(520, confirm ? 240 : 180), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, Font = Font };
        var label = new Label { Text = "От 12 символов. Пароль нельзя восстановить.", Location = new Point(24, 22), AutoSize = true };
        var first = new TextBox { Location = new Point(24, 58), Width = 470, UseSystemPasswordChar = true, MaxLength = 128 };
        var second = new TextBox { Location = new Point(24, 105), Width = 470, UseSystemPasswordChar = true, MaxLength = 128, PlaceholderText = "Повторите пароль" };
        var ok = new Button { Text = "Продолжить", Location = new Point(324, confirm ? 170 : 110), Size = new Size(170, 40), DialogResult = DialogResult.OK };
        dialog.Controls.AddRange([label, first, ok]); if (confirm) dialog.Controls.Add(second); dialog.AcceptButton = ok;
        if (dialog.ShowDialog(this) != DialogResult.OK) return null;
        if (first.Text.Length < 12 || (confirm && first.Text != second.Text)) throw new ArgumentException("Пароли должны совпадать и содержать минимум 12 символов.");
        return first.Text;
    }
    private void RestoreWindow() { Show(); WindowState = FormWindowState.Normal; Activate(); tray.Visible = false; }
    private async Task RecoverOwner()
    {
        using var dialog = new Form { Text = "Локальное восстановление владельца", ClientSize = new Size(530, 220), StartPosition = FormStartPosition.CenterParent, Font = Font };
        var label = new Label { Text = "Только для владельца с доступом к компьютеру сервера.\nСтарые сеансы, приложение MFA и резервные коды будут сброшены.\nВведите существующий логин владельца:", AutoSize = true, Location = new Point(22, 20) };
        var name = new TextBox { Location = new Point(22, 100), Width = 480, MaxLength = 40 };
        var ok = new Button { Text = "Продолжить", DialogResult = DialogResult.OK, Location = new Point(320, 156), Size = new Size(180, 40) };
        dialog.Controls.AddRange([label, name, ok]); dialog.AcceptButton = ok;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var secret = AskPassword("Новый пароль владельца (Aa, цифры, 12+)", true); if (secret is null) return;
        await manager.RecoverOwnerAsync(name.Text, secret);
    }
    private async Task ExitAsync()
    {
        if (busy || closing) return;
        await Run(async () => await manager.StopAsync()); closing = true; timer.Stop(); tray.Visible = false; tray.Dispose(); Close();
    }
}
