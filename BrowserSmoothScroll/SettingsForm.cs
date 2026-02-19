namespace BrowserSmoothScroll;

internal sealed class SettingsForm : Form
{
    private readonly CheckBox _enabledCheckBox = new() { Text = "Enabled", AutoSize = true };
    private readonly CheckBox _autoStartCheckBox = new() { Text = "Auto start on login", AutoSize = true };
    private readonly CheckBox _allAppsCheckBox = new() { Text = "Enable for all apps by default", AutoSize = true };
    private readonly TextBox _processListTextBox = new() { Width = 260 };

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
        ClientSize = new Size(640, 640);

        BuildUi();
        LoadFromSettings(currentSettings);
    }

    private void BuildUi()
    {
        var buttonRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            AutoSize = false,
            Height = 44,
            Padding = new Padding(12, 8, 12, 8)
        };

        var saveButton = new Button
        {
            Text = "Save",
            AutoSize = true
        };
        saveButton.Click += (_, _) => SaveAndClose();

        var cancelButton = new Button
        {
            Text = "Cancel",
            AutoSize = true
        };
        cancelButton.Click += (_, _) => Close();

        var resetButton = new Button
        {
            Text = "Reset Defaults",
            AutoSize = true
        };
        resetButton.Click += (_, _) => LoadFromSettings(new AppSettings());

        buttonRow.Controls.Add(saveButton);
        buttonRow.Controls.Add(cancelButton);
        buttonRow.Controls.Add(resetButton);

        AcceptButton = saveButton;
        CancelButton = cancelButton;

        var contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(12)
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
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var profileGroup = new GroupBox
        {
            Text = "Profile",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        var profileLayout = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            Padding = new Padding(12, 10, 12, 10)
        };

        var processRow = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false
        };
        processRow.Controls.Add(new Label
        {
            Text = "Process allow list (comma):",
            AutoSize = true,
            Margin = new Padding(0, 8, 8, 0)
        });
        processRow.Controls.Add(_processListTextBox);

        profileLayout.Controls.Add(_enabledCheckBox);
        profileLayout.Controls.Add(_autoStartCheckBox);
        profileLayout.Controls.Add(_allAppsCheckBox);
        profileLayout.Controls.Add(processRow);
        profileGroup.Controls.Add(profileLayout);

        var tuneGroup = new GroupBox
        {
            Text = "Tuning",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };

        var tuneLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Padding = new Padding(12, 10, 12, 10),
            ColumnCount = 2
        };
        tuneLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        tuneLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        AddTuningRow(tuneLayout, "Step size [delta]", _stepSizeUpDown, 0);
        AddTuningRow(tuneLayout, "Animation time [ms]", _animationTimeUpDown, 1);
        AddTuningRow(tuneLayout, "Acceleration delta [ms]", _accelerationDeltaUpDown, 2);
        AddTuningRow(tuneLayout, "Acceleration max", _accelerationMaxUpDown, 3);
        AddTuningRow(tuneLayout, "Tail to head ratio [x]", _tailToHeadRatioUpDown, 4);
        tuneGroup.Controls.Add(tuneLayout);

        var behaviorGroup = new GroupBox
        {
            Text = "Behavior",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        var behaviorLayout = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            Padding = new Padding(12, 10, 12, 10)
        };
        behaviorLayout.Controls.Add(_animationEasingCheckBox);
        behaviorLayout.Controls.Add(_shiftHorizontalCheckBox);
        behaviorLayout.Controls.Add(_horizontalSmoothnessCheckBox);
        behaviorLayout.Controls.Add(_reverseDirectionCheckBox);
        behaviorLayout.Controls.Add(_debugModeCheckBox);
        behaviorGroup.Controls.Add(behaviorLayout);

        var noteLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Text = "Tip: Disable browser native smooth scroll at chrome://flags/#smooth-scrolling and edge://flags/#smooth-scrolling to avoid double smoothing."
        };

        root.Controls.Add(profileGroup, 0, 0);
        root.Controls.Add(tuneGroup, 0, 1);
        root.Controls.Add(behaviorGroup, 0, 2);
        root.Controls.Add(noteLabel, 0, 3);

        contentPanel.Controls.Add(root);
        Controls.Add(contentPanel);
        Controls.Add(buttonRow);
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
