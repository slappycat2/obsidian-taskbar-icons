using System.Diagnostics;

namespace ObsidianTaskbarIcons;

internal sealed class MainForm : Form
{
    private readonly ComboBox _vault = new() { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill };
    private readonly TextBox _name = new() { Dock = DockStyle.Fill };
    private readonly TextBox _icon = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly PictureBox _preview = new() { Size = new Size(48, 48), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(6, 3, 3, 3) };
    private readonly Button _browseVault = new() { Text = "Browse…", AutoSize = true };
    private readonly Button _browseIcon = new() { Text = "Browse…", AutoSize = true };
    private readonly Button _create = new() { Text = "Create shortcut && launch", AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
    private readonly Label _guide = new() { AutoSize = true, MaximumSize = new Size(680, 0), Visible = false, Padding = new Padding(8), BorderStyle = BorderStyle.FixedSingle };
    private readonly ListView _list = new() { View = View.Details, FullRowSelect = true, MultiSelect = false, Dock = DockStyle.Fill, HideSelection = false };
    private readonly Button _launch = new() { Text = "Launch", AutoSize = true, Enabled = false };
    private readonly Button _openFolder = new() { Text = "Open shortcut folder", AutoSize = true };
    private readonly Button _retag = new() { Text = "Re-tag open windows", AutoSize = true };
    private readonly Button _remove = new() { Text = "Remove", AutoSize = true, Enabled = false };
    private readonly Label _status = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Padding = new Padding(4) };

    private readonly string? _obsidianExe = ObsidianInfo.FindObsidianExe();
    private string? _iconSource;
    private bool _nameEdited;

    public MainForm()
    {
        Text = "Obsidian Taskbar Icons";
        Icon = AppIcon.Load(32);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(720, 580);
        Size = new Size(780, 640);
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildNewGroup(), 0, 1);
        root.Controls.Add(_guide, 0, 2);
        root.Controls.Add(BuildProfilesGroup(), 0, 3);
        root.Controls.Add(_status, 0, 4);
        Controls.Add(root);

        LoadVaults();
        RefreshProfiles();
        UpdateDefaultIconPreview();
        SetStatus(_obsidianExe is null
            ? "Warning: Obsidian.exe was not found; the obsidian:// URI handler must still be registered."
            : "Ready.");
    }

    /// <summary>The app's own icon with its name and version, across the top of the window.</summary>
    private static Control BuildHeader()
    {
        var logo = new PictureBox { Size = new Size(48, 48), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 0, 10, 8) };
        using (var icon = AppIcon.Load(48)) logo.Image = icon.ToBitmap();

        var title = new Label { Text = "Obsidian Taskbar Icons", AutoSize = true, Font = new Font("Segoe UI Semibold", 14f), Margin = new Padding(0) };
        var subtitle = new Label
        {
            Text = $"Version {AppIcon.Version}  •  one taskbar button per vault, each with its own icon",
            AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(1, 0, 0, 0),
        };
        var text = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0), Anchor = AnchorStyles.Left };
        text.Controls.AddRange(new Control[] { title, subtitle });

        var header = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 6) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.Controls.Add(logo, 0, 0);
        header.Controls.Add(text, 1, 0);
        return header;
    }

    private GroupBox BuildNewGroup()
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 4, AutoSize = true, Padding = new Padding(6) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        grid.Controls.Add(new Label { Text = "Vault folder", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        grid.Controls.Add(_vault, 1, 0);
        grid.Controls.Add(_browseVault, 2, 0);

        grid.Controls.Add(new Label { Text = "Taskbar name", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        grid.Controls.Add(_name, 1, 1);

        grid.Controls.Add(new Label { Text = "Icon", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        grid.Controls.Add(_icon, 1, 2);
        grid.Controls.Add(_browseIcon, 2, 2);
        grid.Controls.Add(_preview, 3, 2);
        grid.SetRowSpan(_preview, 2);

        var actions = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 6, 0, 0) };
        actions.Controls.Add(_create);
        grid.Controls.Add(actions, 1, 3);
        grid.SetColumnSpan(actions, 2);

        _vault.TextChanged += (_, _) => OnVaultChanged();
        _vault.SelectedIndexChanged += (_, _) => OnVaultChanged();
        _name.TextChanged += (_, _) => { if (_name.Focused) _nameEdited = _name.Text.Length > 0; };
        _browseVault.Click += (_, _) => BrowseVault();
        _browseIcon.Click += (_, _) => BrowseIcon();
        _create.Click += (_, _) => CreateAndLaunch();

        return new GroupBox { Text = "New taskbar icon", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6), Controls = { grid } };
    }

    private GroupBox BuildProfilesGroup()
    {
        _list.Columns.Add("Taskbar name", 180);
        _list.Columns.Add("Vault", 320);
        _list.Columns.Add("Identity", 200);
        _list.SelectedIndexChanged += (_, _) =>
        {
            var has = _list.SelectedItems.Count > 0;
            _launch.Enabled = has;
            _remove.Enabled = has;
        };
        _list.DoubleClick += (_, _) => LaunchSelected();

        _launch.Click += (_, _) => LaunchSelected();
        _openFolder.Click += (_, _) => OpenShortcutFolder();
        _retag.Click += (_, _) => Retag();
        _remove.Click += (_, _) => RemoveSelected();

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 6, 0, 0) };
        buttons.Controls.AddRange(new Control[] { _launch, _openFolder, _retag, _remove });

        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
        panel.Controls.Add(_list);
        panel.Controls.Add(buttons);

        return new GroupBox { Text = "Existing taskbar icons", Dock = DockStyle.Fill, Controls = { panel } };
    }

    // ----- new-icon flow -------------------------------------------------------------------------

    private void LoadVaults()
    {
        _vault.Items.Clear();
        foreach (var v in ObsidianInfo.KnownVaults())
        {
            _vault.Items.Add(v.Path);
        }

        if (_vault.Items.Count > 0) _vault.SelectedIndex = 0;
    }

    private void OnVaultChanged()
    {
        var path = _vault.Text.Trim();
        if (path.Length == 0) return;
        if (!_nameEdited)
        {
            _name.Text = ObsidianInfo.VaultName(path);
        }
    }

    private void BrowseVault()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Select an Obsidian vault folder (it contains a .obsidian subfolder)",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
        };
        if (Directory.Exists(_vault.Text.Trim())) dlg.InitialDirectory = _vault.Text.Trim();
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _vault.Text = dlg.SelectedPath;
    }

    private void BrowseIcon()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Choose an icon (ico, png, jpg, bmp, exe or dll)",
            Filter = IconConverter.FileFilter,
            CheckFileExists = true,
        };
        var startIn = _iconSource is not null ? Path.GetDirectoryName(_iconSource) : AppPaths.SampleIcons;
        if (startIn is not null) dlg.InitialDirectory = startIn;
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (!IconConverter.IsSupported(dlg.FileName))
        {
            MessageBox.Show(this, "That file type is not supported. Choose an .ico, .png, .jpg, .bmp, .exe or .dll.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _iconSource = dlg.FileName;
        _icon.Text = dlg.FileName;
        SetPreview(IconConverter.Preview(dlg.FileName, 48));
    }

    private void UpdateDefaultIconPreview()
    {
        if (_iconSource is not null) return;
        _icon.Text = _obsidianExe is null ? "" : "(Obsidian's own icon)";
        SetPreview(_obsidianExe is null ? null : IconConverter.Preview(_obsidianExe, 48));
    }

    private void SetPreview(Image? image)
    {
        var old = _preview.Image;
        _preview.Image = image;
        old?.Dispose();
    }

    private void CreateAndLaunch()
    {
        var vaultPath = _vault.Text.Trim().Trim('"');
        if (vaultPath.Length == 0 || !Directory.Exists(vaultPath))
        {
            MessageBox.Show(this, "Pick an existing vault folder first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!ObsidianInfo.LooksLikeVault(vaultPath))
        {
            var r = MessageBox.Show(this, "That folder has no .obsidian subfolder, so it does not look like a vault. Continue anyway?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;
        }

        if (!ObsidianInfo.IsRegistered(vaultPath))
        {
            var r = MessageBox.Show(this,
                "Obsidian does not list this folder in its vault switcher yet, so the obsidian:// link cannot open it until you open it once from inside Obsidian.\n\nCreate the taskbar icon anyway?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r != DialogResult.Yes) return;
        }

        var vaultName = ObsidianInfo.VaultName(vaultPath);
        var displayName = _name.Text.Trim();
        if (displayName.Length == 0) displayName = vaultName;

        var iconSource = _iconSource ?? _obsidianExe;
        if (iconSource is null)
        {
            MessageBox.Show(this, "Choose an icon file; Obsidian's own icon could not be located as a default.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var slug = ProfileStore.MakeSlug(vaultName, vaultPath);
        var existing = ProfileStore.Find(slug);
        if (existing is not null)
        {
            var r = MessageBox.Show(this, $"A taskbar icon for '{existing.DisplayName}' already exists for this vault. Replace it?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;
        }

        try
        {
            var profile = ProfileBuilder.Create(vaultPath, displayName, iconSource);
            RefreshProfiles();

            ShowGuide(profile);
            RunLaunch(profile);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not create the taskbar icon:\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowGuide(VaultProfile profile)
    {
        _guide.Text =
            $"'{profile.DisplayName}' is opening with its new icon.\n\n" +
            "To pin it:  right-click the vault's new taskbar button and choose \"Pin to taskbar\".\n" +
            $"It is also in the Start menu as \"Obsidian - {profile.DisplayName}\" (right-click, More, Pin to taskbar works there too).\n\n" +
            "From now on that button opens exactly this vault, and the vault's window groups under it.";
        _guide.Visible = true;
    }

    // ----- existing profiles ---------------------------------------------------------------------

    private void RefreshProfiles()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var p in ProfileStore.Load())
        {
            _list.Items.Add(new ListViewItem(new[] { p.DisplayName, p.VaultPath, p.Aumid }) { Tag = p });
        }

        _list.EndUpdate();
        _launch.Enabled = _remove.Enabled = false;
    }

    private VaultProfile? Selected => _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as VaultProfile : null;

    private void LaunchSelected()
    {
        if (Selected is { } p) RunLaunch(p);
    }

    private void RunLaunch(VaultProfile profile)
    {
        SetStatus($"Opening '{profile.VaultName}'…");
        UseWaitCursor = true;
        Task.Run(() => Launcher.Launch(profile)).ContinueWith(t =>
        {
            UseWaitCursor = false;
            if (t.IsFaulted)
            {
                SetStatus("Launch failed: " + t.Exception?.GetBaseException().Message);
                return;
            }

            SetStatus(t.Result.Message);
            if (!t.Result.Success)
            {
                MessageBox.Show(this, t.Result.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void OpenShortcutFolder()
    {
        var target = Selected?.ShortcutPath;
        var args = target is not null && File.Exists(target)
            ? $"/select,\"{target}\""
            : $"\"{AppPaths.StartMenuPrograms}\"";
        Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
    }

    private void Retag()
    {
        var n = Launcher.TagAll();
        SetStatus(n == 0
            ? "No open Obsidian windows matched a saved taskbar icon."
            : $"Re-tagged {n} open window(s); the icon watcher keeps their window icons in place.");
    }

    private void RemoveSelected()
    {
        if (Selected is not { } p) return;
        var r = MessageBox.Show(this,
            $"Remove the taskbar icon for '{p.DisplayName}'?\n\nThis deletes its Start-menu shortcut and icon file. If you pinned it, unpin it from the taskbar yourself afterwards.",
            Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (r != DialogResult.Yes) return;

        TryDelete(p.ShortcutPath);
        TryDelete(p.IconPath);
        ProfileStore.Remove(p.Slug);
        RefreshProfiles();
        SetStatus($"Removed '{p.DisplayName}'.");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // best effort; the user can delete a locked file by hand
        }
    }

    private void SetStatus(string text) => _status.Text = text;
}
