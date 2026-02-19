namespace BrowserSmoothScroll;

internal sealed class SettingsForm : Form
{
    private readonly CheckBox _enabledCheckBox = new() { Text = "Enabled", AutoSize = true };
    private readonly CheckBox _autoStartCheckBox = new() { Text = "Auto start on login", AutoSize = true };
    private readonly CheckBox _allAppsCheckBox = new() { Text = "Enable for all apps by default", AutoSize = true };
    private readonly Label _processListLabel = new() { Text = "Process allow list (comma):", AutoSize = true, Margin = new Padding(0, 8, 8, 0) };
    private readonly TextBox _processListTextBox = new() { Width = 260 };
    private readonly Label _blockListLabel = new() { Text = "Process block list (comma):", AutoSize = true, Margin = new Padding(0, 8, 8, 0) };
    private readonly TextBox _blockListTextBox = new() { Width = 260 };

    private readonly NumericUpDown _stepSizeUpDown = NewNumeric(20, 600);
    private readonly NumericUpDown _animationTimeUpDown = NewNumeric(40, 2000);
    private readonly NumericUpDown _accelerationDeltaUpDown = NewNumeric(0, 500);
    private readonly NumericUpDown _accelerationMaxUpDown = NewNumeric(1, 20);
    private readonly NumericUpDown _tailToHeadRatioUpDown = NewNumeric(1, 10);

    private readonly CheckBox _animationEasingCheckBox = new() { Text = "Animation easing", AutoSize = true };
    private readonly CheckBox _shiftHorizontalCheckBox = new() { Text = "Shift key horizontal scrolling", AutoSize = true };
    private readonly CheckBox _horizontalSmoothnessCheckBox = new() { Text = "Horizontal smoothness", AutoSize = true };
    private readonly CheckBox _reverseDirectionCheckBox = new() { Text = "Reverse wheel direction", AutoSize = true };
    private readonly CheckBox _debugModeCheckBox = new() { Text = "Debug mode (write telemetry logs)", AutoSize = true };

    public AppSettings? UpdatedSettings { get; private set; }

    public SettingsForm(AppSettings currentSettings)
    {
        Text = "Browser Smooth Scroll Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(640, 700);

        BuildUi();
        LoadFromSettings(currentSettings);
        
        _allAppsCheckBox.CheckedChanged += (_, _) => UpdateListVisibility();
        UpdateListVisibility();
    }

    private void UpdateListVisibility()
    {
        var allApps = _allAppsCheckBox.Checked;
        _processListLabel.Visible = !allApps;
        _processListTextBox.Visible = !allApps;
        _blockListLabel.Visible = allApps;
        _blockListTextBox.Visible = allApps;
    }

    private void BuildUi()
    {
        // 1. Bottom Buttons
        var buttonRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            AutoSize = true,
            Padding = new Padding(15, 15, 15, 15),
            BackColor = SystemColors.ControlLight
        };

        var saveButton = new Button { Text = "Save", AutoSize = true, DialogResult = DialogResult.OK, MinimumSize = new Size(80, 30) };
        saveButton.Click += (_, _) => SaveAndClose();

        var cancelButton = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel, MinimumSize = new Size(80, 30) };
        cancelButton.Click += (_, _) => Close();

        var resetButton = new Button { Text = "Defaults", AutoSize = true, MinimumSize = new Size(80, 30) };
        resetButton.Click += (_, _) => { LoadFromSettings(new AppSettings()); };

        buttonRow.Controls.Add(saveButton);
        buttonRow.Controls.Add(cancelButton);
        buttonRow.Controls.Add(resetButton);

        AcceptButton = saveButton;
        CancelButton = cancelButton;

        // 2. Main Content
        var contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(20)
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 4
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // 3. Tip Banner
        var tipPanel = new Panel
        {
            AutoSize = true,
            BackColor = Color.FromArgb(240, 255, 240), // Very light green
            Padding = new Padding(15),
            Margin = new Padding(0, 0, 0, 20),
            Dock = DockStyle.Fill
        };
        var tipLabel = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Text = "Tip: Disable browser native smooth scroll (chrome://flags/#smooth-scrolling) to avoid double smoothing.",
            ForeColor = Color.DarkGreen,
            Font = new Font(Font, FontStyle.Bold),
            MaximumSize = new Size(560, 0) // Limit width for wrapping
        };
        tipPanel.Controls.Add(tipLabel);

        // 4. Profile Group
        var profileGroup = CreateGroup("Profile");
        var profileFlow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        
        _enabledCheckBox.Margin = new Padding(0, 0, 0, 8);
        _autoStartCheckBox.Margin = new Padding(0, 0, 0, 8);
        _allAppsCheckBox.Margin = new Padding(0, 0, 0, 15);

        profileFlow.Controls.Add(_enabledCheckBox);
        profileFlow.Controls.Add(_autoStartCheckBox);
        profileFlow.Controls.Add(_allAppsCheckBox);

        var listLayout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Width = 540 };
        listLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        
        _processListLabel.AutoSize = true; _processListTextBox.Dock = DockStyle.Fill;
        _blockListLabel.AutoSize = true; _blockListTextBox.Dock = DockStyle.Fill;

        listLayout.Controls.Add(_processListLabel, 0, 0);
        listLayout.Controls.Add(_processListTextBox, 0, 1);
        listLayout.Controls.Add(_blockListLabel, 0, 2);
        listLayout.Controls.Add(_blockListTextBox, 0, 3);
        
        profileFlow.Controls.Add(listLayout);
        profileGroup.Controls.Add(profileFlow);

        // 5. Tuning Group
        var tuneGroup = CreateGroup("Tuning");
        var tuneTable = new TableLayoutPanel 
        { 
            Dock = DockStyle.Fill, 
            AutoSize = true, 
            ColumnCount = 2, 
            Padding = new Padding(5)
        };
        tuneTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        tuneTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));

        AddTuningRow(tuneTable, "Step Size (px)", _stepSizeUpDown, 0);
        AddTuningRow(tuneTable, "Animation Time (ms)", _animationTimeUpDown, 1);
        AddTuningRow(tuneTable, "Acceleration Delta (ms)", _accelerationDeltaUpDown, 2);
        AddTuningRow(tuneTable, "Max Acceleration (x)", _accelerationMaxUpDown, 3);
        AddTuningRow(tuneTable, "Tail-to-Head Ratio", _tailToHeadRatioUpDown, 4);
        tuneGroup.Controls.Add(tuneTable);

        // 6. Behavior Group
        var behaviorGroup = CreateGroup("Behavior");
        var behaviorFlow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, Dock = DockStyle.Fill, AutoSize = true };
        _animationEasingCheckBox.Margin = new Padding(0, 5, 0, 5);
        _shiftHorizontalCheckBox.Margin = new Padding(0, 5, 0, 5);
        _horizontalSmoothnessCheckBox.Margin = new Padding(0, 5, 0, 5);
        _reverseDirectionCheckBox.Margin = new Padding(0, 5, 0, 5);
        _debugModeCheckBox.Margin = new Padding(0, 5, 0, 5);

        behaviorFlow.Controls.Add(_animationEasingCheckBox);
        behaviorFlow.Controls.Add(_shiftHorizontalCheckBox);
        behaviorFlow.Controls.Add(_horizontalSmoothnessCheckBox);
        behaviorFlow.Controls.Add(_reverseDirectionCheckBox);
        behaviorFlow.Controls.Add(_debugModeCheckBox);
        behaviorGroup.Controls.Add(behaviorFlow);

        // Assemble
        root.Controls.Add(tipPanel, 0, 0);
        root.Controls.Add(profileGroup, 0, 1);
        root.Controls.Add(tuneGroup, 0, 2);
        root.Controls.Add(behaviorGroup, 0, 3);

        contentPanel.Controls.Add(root);
        Controls.Add(contentPanel);
        Controls.Add(buttonRow);
    }

    private GroupBox CreateGroup(string title)
    {
        return new GroupBox
        {
            Text = title,
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(15, 25, 15, 15), // Top padding accounts for group title
            Margin = new Padding(0, 0, 0, 15),
            Font = new Font(Font.FontFamily, 10f, FontStyle.Regular) // Slightly larger font for group
        };
    }

    private static NumericUpDown NewNumeric(int min, int max)
    {
        return new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Width = 120
        };
    }

    private static void AddTuningRow(TableLayoutPanel table, string labelText, Control input, int row)
    {
        table.RowCount = Math.Max(table.RowCount, row + 1);
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        table.Controls.Add(new Label
        {
            Text = labelText,
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            Margin = new Padding(0, 8, 8, 8)
        }, 0, row);

        input.Anchor = AnchorStyles.Left;
        input.Margin = new Padding(0, 4, 0, 4);
        table.Controls.Add(input, 1, row);
    }

    private void LoadFromSettings(AppSettings settings)
    {
        settings.Normalize();
        _enabledCheckBox.Checked = settings.Enabled;
        _autoStartCheckBox.Checked = settings.AutoStartOnLogin;
        _allAppsCheckBox.Checked = settings.EnableForAllAppsByDefault;
        _processListTextBox.Text = settings.ProcessAllowList;
        _blockListTextBox.Text = settings.ProcessBlockList;
        _stepSizeUpDown.Value = settings.StepSize;
        _animationTimeUpDown.Value = settings.AnimationTimeMs;
        _accelerationDeltaUpDown.Value = settings.AccelerationDeltaMs;
        _accelerationMaxUpDown.Value = settings.AccelerationMax;
        _tailToHeadRatioUpDown.Value = settings.TailToHeadRatio;
        _animationEasingCheckBox.Checked = settings.AnimationEasing;
        _shiftHorizontalCheckBox.Checked = settings.ShiftKeyHorizontalScrolling;
        _horizontalSmoothnessCheckBox.Checked = settings.HorizontalSmoothness;
        _reverseDirectionCheckBox.Checked = settings.ReverseWheelDirection;
        _debugModeCheckBox.Checked = settings.DebugMode;
    }

    private void SaveAndClose()
    {
        var updated = new AppSettings
        {
            Enabled = _enabledCheckBox.Checked,
            AutoStartOnLogin = _autoStartCheckBox.Checked,
            EnableForAllAppsByDefault = _allAppsCheckBox.Checked,
            ProcessAllowList = _processListTextBox.Text,
            ProcessBlockList = _blockListTextBox.Text,
            StepSize = decimal.ToInt32(_stepSizeUpDown.Value),
            AnimationTimeMs = decimal.ToInt32(_animationTimeUpDown.Value),
            AccelerationDeltaMs = decimal.ToInt32(_accelerationDeltaUpDown.Value),
            AccelerationMax = decimal.ToInt32(_accelerationMaxUpDown.Value),
            TailToHeadRatio = decimal.ToInt32(_tailToHeadRatioUpDown.Value),
            AnimationEasing = _animationEasingCheckBox.Checked,
            ShiftKeyHorizontalScrolling = _shiftHorizontalCheckBox.Checked,
            HorizontalSmoothness = _horizontalSmoothnessCheckBox.Checked,
            ReverseWheelDirection = _reverseDirectionCheckBox.Checked,
            DebugMode = _debugModeCheckBox.Checked
        };

        updated.Normalize();
        if (!updated.EnableForAllAppsByDefault && updated.AllowedProcessNames.Length == 0)
        {
            MessageBox.Show(
                "Provide at least one process name, for example: chrome,msedge",
                "Validation",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        UpdatedSettings = updated;
        DialogResult = DialogResult.OK;
        Close();
    }
}
