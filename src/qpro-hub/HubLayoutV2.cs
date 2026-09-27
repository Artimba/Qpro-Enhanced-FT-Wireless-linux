using System.Drawing;
using System.Diagnostics;

namespace QproFaceTracking.Hub;

internal sealed partial class HubForm
{
    private Control BuildLayout()
    {
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = Background,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 206));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));

        var sidebarViewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Panel, Margin = Padding.Empty };
        var sidebar = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 472,
            ColumnCount = 1,
            RowCount = 7,
            BackColor = Panel,
            Padding = new Padding(12, 24, 12, 16),
            Margin = Padding.Empty,
        };
        sidebar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        for (var row = 1; row <= 5; row++) sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        var brand = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        brand.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        brand.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        brand.Controls.Add(new Label { Text = "QPRO HUB", Dock = DockStyle.Fill, Font = new Font(UiFontName, 14F, FontStyle.Bold), ForeColor = Color.White, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true }, 0, 0);
        brand.Controls.Add(new Label { Text = "Face tracking control", Dock = DockStyle.Fill, ForeColor = Muted, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        sidebar.Controls.Add(brand, 0, 0);

        var liveTab = NavigationButton("Live tracking");
        var setupTab = NavigationButton("First-time setup");
        var personalizationTab = NavigationButton("Personalize");
        var modelsTab = NavigationButton("Model manager");
        var activityTab = NavigationButton("Activity");
        var tabs = new[] { setupTab, liveTab, personalizationTab, modelsTab, activityTab };
        for (var index = 0; index < tabs.Length; index++) sidebar.Controls.Add(tabs[index], 0, index + 1);
        sidebar.Controls.Add(new Label
        {
            Text = "ROOTED QUEST PRO REQUIRED\nGaze and pupil are experimental",
            Dock = DockStyle.Fill,
            ForeColor = Muted,
            TextAlign = ContentAlignment.BottomLeft,
            Font = new Font(UiFontName, 8.5F),
        }, 0, 6);
        sidebarViewport.Controls.Add(sidebar);
        shell.Controls.Add(sidebarViewport, 0, 0);
        shell.SizeChanged += (_, _) =>
        {
            var dpiScale = shell.DeviceDpi / 96F;
            var sidebarWidth = Math.Clamp((int)(shell.ClientSize.Width * 0.2), (int)(180 * dpiScale), (int)(226 * dpiScale));
            if ((int)shell.ColumnStyles[0].Width != sidebarWidth)
                shell.ColumnStyles[0].Width = sidebarWidth;
        };

        var main = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Background, Margin = Padding.Empty };
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(24, 16, 24, 7) };
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
        var pageTitle = new Label { Dock = DockStyle.Fill, Font = new Font(UiFontName, 20F, FontStyle.Bold), ForeColor = Color.White, TextAlign = ContentAlignment.MiddleLeft };
        var pageSubtitle = new Label { Dock = DockStyle.Fill, ForeColor = Muted, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
        header.Controls.Add(pageTitle, 0, 0);
        header.Controls.Add(pageSubtitle, 0, 1);
        main.Controls.Add(header, 0, 0);
        var pages = new Panel { Dock = DockStyle.Fill, BackColor = Background, Margin = Padding.Empty };
        main.Controls.Add(pages, 0, 1);
        shell.Controls.Add(main, 1, 0);

        Panel NewPage()
        {
            var page = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Background, Padding = new Padding(22, 8, 22, 20), Visible = false, Tag = "hub-page" };
            pages.Controls.Add(page);
            return page;
        }

        var livePage = NewPage();
        var liveLayout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 3, Padding = Padding.Empty, Margin = Padding.Empty };
        liveLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        liveLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        liveLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        liveLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        livePage.Controls.Add(liveLayout);
        var statuses = Card();
        statuses.Dock = DockStyle.Top;
        statuses.ColumnCount = 2;
        statuses.RowCount = 9;
        for (var column = 0; column < 2; column++) statuses.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        statuses.Controls.Add(SectionTitle("Connection and readiness"), 0, 0);
        statuses.SetColumnSpan(statuses.GetControlFromPosition(0, 0)!, 2);
        var statusItems = new[]
        {
            ("Quest ADB", _usbStatus), ("SteamVR", _steamStatus), ("VRCFaceTracking", _vrcftStatus),
            ("Combined bridge", _bridgeStatus), ("PC runtime", _runtimeStatus), ("Gaze support", _gazeStatus),
            ("Tongue inference", _inferenceStatus), ("Pupil processing", _pupilStatus),
        };
        for (var index = 0; index < statusItems.Length; index++)
        {
            var row = 1 + (index / 2) * 2;
            var column = index % 2;
            statuses.Controls.Add(new Label { Text = statusItems[index].Item1, AutoSize = true, ForeColor = Muted, Margin = new Padding(8, 2, 8, 2) }, column, row);
            statuses.Controls.Add(statusItems[index].Item2, column, row + 1);
        }
        liveLayout.Controls.Add(statuses);

        var tracking = Card();
        tracking.Dock = DockStyle.Top;
        tracking.ColumnCount = 2;
        tracking.RowCount = 14;
        tracking.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        tracking.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tracking.Controls.Add(SectionTitle("Choose tracking features"), 0, 0);
        tracking.SetColumnSpan(tracking.GetControlFromPosition(0, 0)!, 2);
        tracking.Controls.Add(Info("Choose one or more features, then use the fixed action bar below to apply them."), 0, 1);
        tracking.SetColumnSpan(tracking.GetControlFromPosition(0, 1)!, 2);
        tracking.Controls.Add(_gaze, 0, 2); tracking.SetColumnSpan(_gaze, 2);
        tracking.Controls.Add(new Label { Text = "Eye profile", AutoSize = true, ForeColor = Muted, Margin = new Padding(24, 9, 10, 4) }, 0, 3);
        tracking.Controls.Add(_eyeProfiles, 1, 3);
        tracking.Controls.Add(_tongue, 0, 4); tracking.SetColumnSpan(_tongue, 2);
        tracking.Controls.Add(new Label { Text = "Tongue model", AutoSize = true, ForeColor = Muted, Margin = new Padding(24, 9, 10, 4) }, 0, 5);
        tracking.Controls.Add(_tongueModels, 1, 5);
        tracking.Controls.Add(new Label { Text = "FPS cap", AutoSize = true, ForeColor = Muted, Margin = new Padding(24, 9, 10, 4) }, 0, 6);
        tracking.Controls.Add(_fps, 1, 6);
        tracking.Controls.Add(_tongueModelNote, 0, 7); tracking.SetColumnSpan(_tongueModelNote, 2);
        var tuning = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(22, 4, 0, 7) };
        tuning.Controls.Add(new Label { Text = "Motion smoothing", AutoSize = true, ForeColor = Muted, Margin = new Padding(0, 8, 5, 0) });
        tuning.Controls.Add(_smoothing);
        tuning.Controls.Add(new Label { Text = "Visibility", AutoSize = true, ForeColor = Muted, Margin = new Padding(10, 8, 5, 0) });
        tuning.Controls.Add(_visibilityMode);
        tracking.Controls.Add(tuning, 0, 8); tracking.SetColumnSpan(tuning, 2);
        tracking.Controls.Add(_pupil, 0, 9); tracking.SetColumnSpan(_pupil, 2);
        tracking.Controls.Add(new Label { Text = "Pupil response", AutoSize = true, ForeColor = Muted, Margin = new Padding(24, 9, 10, 4) }, 0, 10);
        tracking.Controls.Add(_pupilSensitivity, 1, 10);
        tracking.Controls.Add(Info("Pupil tracking is experimental. Response runs from 1.0× to 3.0× in 0.2× steps. Look straight and hold steady until both eyes finish warming up. Independent gaze briefly restarts headset tracking when applied or restored."), 0, 11);
        tracking.SetColumnSpan(tracking.GetControlFromPosition(0, 11)!, 2);
        tracking.Controls.Add(_cameraPreview, 0, 12); tracking.SetColumnSpan(_cameraPreview, 2);
        tracking.Controls.Add(Info("Turn this off to hide the live camera windows. Tongue and pupil tracking will continue. Changes take effect the next time you start tracking."), 0, 13);
        tracking.SetColumnSpan(tracking.GetControlFromPosition(0, 13)!, 2);
        liveLayout.Controls.Add(tracking);
        var refresh = ActionButton("Refresh connection status", (_, _) => { ReloadProfiles(); _ = RefreshStatusAsync(); });
        refresh.Dock = DockStyle.Top;
        liveLayout.Controls.Add(refresh);

        var setupPage = NewPage();
        var setupLayout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 5 };
        setupLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < 5; row++) setupLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        setupPage.Controls.Add(setupLayout);
        var connectionCard = Card(); connectionCard.Dock = DockStyle.Top;
        connectionCard.ColumnCount = 1;
        connectionCard.Controls.Add(SectionTitle("Connect your Quest Pro"));
        var connectionPicker = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = new Padding(5, 3, 5, 7) };
        connectionPicker.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
        connectionPicker.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        connectionPicker.Controls.Add(new Label { Text = "Connection type", AutoSize = true, ForeColor = Muted, Margin = new Padding(0, 8, 8, 3) }, 0, 0);
        connectionPicker.Controls.Add(_connectionMode, 1, 0);
        connectionCard.Controls.Add(connectionPicker);
        _connectionModeNote.Margin = new Padding(5, 2, 5, 8);
        connectionCard.Controls.Add(_connectionModeNote);
        _wirelessSetup.Margin = new Padding(4, 3, 4, 8);
        _wirelessSetup.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _wirelessSetup.Controls.Add(Info("Enter the Quest's Wi-Fi IP and press Connect to Quest. If it reports ready, skip pairing and start tracking. Use Pair and connect only when Android shows a six-digit pairing code; enter the temporary pairing IP:port from that dialog, which differs from the regular connection port. A one-time USB connection can also enable wireless ADB."));
        TableLayoutPanel ConnectionField(string title, TextBox input)
        {
            var row = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = new Padding(3, 2, 3, 6) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.Controls.Add(new Label { Text = title, AutoSize = true, ForeColor = Muted, Margin = new Padding(0, 7, 8, 2) }, 0, 0);
            row.Controls.Add(input, 1, 0);
            return row;
        }
        _wirelessSetup.Controls.Add(ConnectionField("Quest IP:port", _wirelessAddress));
        _wirelessSetup.Controls.Add(_enableWirelessButton);
        _wirelessSetup.Controls.Add(_connectWirelessButton);
        _wirelessSetup.Controls.Add(ConnectionField("Pairing IP:port", _pairingEndpoint));
        _wirelessSetup.Controls.Add(ConnectionField("Six-digit code", _pairingCode));
        _wirelessSetup.Controls.Add(_pairWirelessButton);
        _wirelessSetup.Controls.Add(_disableWirelessButton);
        connectionCard.Controls.Add(_wirelessSetup);
        setupLayout.Controls.Add(connectionCard);
        var setupIntro = Card(); setupIntro.Dock = DockStyle.Top;
        setupIntro.Controls.Add(SectionTitle("Required setup checklist"));
        setupIntro.Controls.Add(Info("Install the latest VRCFaceTracking from Steam first. Complete the PC runtime and bridge steps once. Prepare independent gaze for that feature. Close VRCFaceTracking for bridge installation, then restart it."));
        setupLayout.Controls.Add(setupIntro);
        _setupProgressContainer.Dock = DockStyle.Top;
        _setupProgressContainer.AutoSize = true;
        _setupProgressContainer.ColumnCount = 1;
        _setupProgressContainer.BackColor = Panel;
        _setupProgressContainer.Padding = new Padding(12);
        _setupProgressContainer.Margin = new Padding(0, 0, 0, 12);
        _setupProgressContainer.Controls.Add(new Label { Text = "Setup progress", AutoSize = true, ForeColor = Color.White, Font = new Font(UiFontName, 10F, FontStyle.Bold) });
        _setupProgressContainer.Controls.Add(_setupProgressStatus);
        _setupProgressContainer.Controls.Add(_setupProgress);
        setupLayout.Controls.Add(_setupProgressContainer);
        _setupRuntimeButton.Click += async (_, _) => await RunSetupStepAsync("PC runtime setup", "setup-runtime.ps1", "PC runtime is ready.", "Next: close VRCFaceTracking and install the combined bridge.");
        _setupBridgeButton.Click += async (_, _) => await RunSetupStepAsync("Install bridge", "install-vrcft-eye-bridge.ps1", "The combined VRCFaceTracking bridge is installed.", "Restart VRCFaceTracking, then prepare gaze from the headset.");
        _uninstallBridgeButton.Click += async (_, _) =>
        {
            if (Process.GetProcessesByName("VRCFaceTracking").Any())
            {
                MessageBox.Show(this, "Close VRCFaceTracking, then press Uninstall bridge again.", "Close VRCFaceTracking", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(this,
                    "Remove the Qpro bridge from VRCFaceTracking and restore any Virtual Desktop modules saved by this Qpro copy?\n\nPersonal tongue models and captures will stay in place.",
                    "Uninstall bridge", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            await RunSetupStepAsync("Uninstall bridge", "uninstall-vrcft-eye-bridge.ps1",
                "The Qpro VRCFaceTracking bridge has been removed.",
                "Restart VRCFaceTracking. If normal Virtual Desktop face tracking is missing, install its official module again.");
        };
        _setupGazeButton.Click += async (_, _) => await PrepareGazeAsync();
        var setupCards = new TableLayoutPanel { Dock = DockStyle.Top, Height = 750, ColumnCount = 1, RowCount = 3 };
        setupCards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < 3; row++) setupCards.RowStyles.Add(new RowStyle(SizeType.Percent, 33.333F));
        setupCards.Controls.Add(SetupStepCard("1", "PC runtime", "Creates a separate Qpro Python environment and installs CPU/GPU libraries. Existing Python 3.12 can be reused safely.", _setupRuntimeStatus, _setupRuntimeButton), 0, 0);
        setupCards.Controls.Add(SetupStepCard("2", "VRCFT bridge", "Adds or removes the Qpro module. Close VRCFaceTracking before either action.", _setupBridgeStatus, _setupBridgeButton, _uninstallBridgeButton), 0, 1);
        setupCards.Controls.Add(SetupStepCard("3", "Independent gaze", "Creates a local gaze patch from your rooted headset.", _setupGazeStatus, _setupGazeButton), 0, 2);
        setupLayout.Controls.Add(setupCards);
        var amdCard = Card(); amdCard.Dock = DockStyle.Top;
        amdCard.Controls.Add(SectionTitle("Optional AMD ROCm acceleration"));
        amdCard.Controls.Add(Info("For AMD GPUs on the Windows ROCm 7.2.1 support list. Install the PC runtime first. The installer checks your GPU and model; once ready, tongue inference and training select ROCm automatically."));
        amdCard.Controls.Add(_amdGpuStatus);
        amdCard.Controls.Add(_amdManualConfirm);
        var amdSupportLink = new LinkLabel { Text = "See AMD's Windows ROCm 7.2.1 GPU list", AutoSize = true, LinkColor = Accent, ActiveLinkColor = Accent, VisitedLinkColor = Accent, Margin = new Padding(5, 3, 5, 8) };
        amdSupportLink.LinkClicked += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("https://rocm.docs.amd.com/projects/radeon-ryzen/en/docs-7.2.1/docs/compatibility/compatibilityrad/windows/windows_compatibility.html") { UseShellExecute = true }); }
            catch (Exception error) { MessageBox.Show(this, error.Message, "Could not open AMD support list"); }
        };
        amdCard.Controls.Add(amdSupportLink);
        amdCard.Controls.Add(_setupAmdStatus);
        amdCard.Controls.Add(_setupAmdButton);
        setupLayout.Controls.Add(amdCard);

        var personalizationPage = NewPage();
        var personalLayout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 3 };
        personalLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        personalLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        personalLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        personalLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        personalizationPage.Controls.Add(personalLayout);
        var personalIntro = Card(); personalIntro.Dock = DockStyle.Top;
        personalIntro.Controls.Add(SectionTitle("Personalize tongue tracking"));
        personalIntro.Controls.Add(Info("The bundled developer model works as a demo. Recording your own data improves fit for your mouth, headset position, and expressions."));
        personalLayout.Controls.Add(personalIntro);
        _trainingProgressContainer.Dock = DockStyle.Top;
        _trainingProgressContainer.AutoSize = true;
        _trainingProgressContainer.ColumnCount = 1;
        _trainingProgressContainer.BackColor = Panel;
        _trainingProgressContainer.Padding = new Padding(12);
        _trainingProgressContainer.Margin = new Padding(0, 0, 0, 12);
        _trainingProgressContainer.Controls.Add(new Label { Text = "Training progress", AutoSize = true, ForeColor = Color.White, Font = new Font(UiFontName, 10F, FontStyle.Bold) });
        _trainingProgressContainer.Controls.Add(_trainingProgressStatus);
        _trainingProgressContainer.Controls.Add(_trainingProgress);
        personalLayout.Controls.Add(_trainingProgressContainer);
        var choices = new TableLayoutPanel { Dock = DockStyle.Top, Height = 850, ColumnCount = 1, RowCount = 2 };
        choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        choices.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        choices.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        choices.Controls.Add(WorkflowCard("Quick refinement · 10–20 min", "Fastest. Corrects common false positives and direction gaps, but inherits some developer-model bias.", _quickDatasets, _quickQueueStatus, _quickRecordedDatasets,
            ActionButton("1. Record refinement", async (_, _) => await ConfirmCaptureAsync(true)), ActionButton("2. Train personalized copy", async (_, _) => await TrainTongueAsync(true)),
            ActionButton("Delete selected dataset…", (_, _) => DeleteRecordedDataset(true))), 0, 0);
        choices.Controls.Add(WorkflowCard("Full dataset · 45–90 min", "Best individual coverage and independence from v8. Requires more careful capture time.", _fullDatasets, _fullQueueStatus, _fullRecordedDatasets,
            ActionButton("1. Record full dataset", async (_, _) => await ConfirmCaptureAsync(false)), ActionButton("2. Train new personal model", async (_, _) => await TrainTongueAsync(false)),
            ActionButton("Delete selected dataset…", (_, _) => DeleteRecordedDataset(false))), 0, 1);
        personalLayout.Controls.Add(choices);

        var modelsPage = NewPage();
        var manager = Card(); manager.Dock = DockStyle.Fill; manager.AutoSize = false; manager.MinimumSize = new Size(0, 380);
        manager.ColumnCount = 1; manager.RowCount = 4;
        manager.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        manager.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        manager.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        manager.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        manager.Controls.Add(SectionTitle("Tongue model manager"), 0, 0);
        manager.Controls.Add(Info("Friendly names leave the paired model files intact. Export creates a portable .qptonguemodel package; import assigns a safe new version."), 0, 1);
        var modelBody = new Panel { Dock = DockStyle.Fill, BackColor = Inset };
        modelBody.Controls.Add(_modelList);
        modelBody.Controls.Add(_modelEmpty);
        manager.Controls.Add(modelBody, 0, 2);
        var modelActions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        modelActions.Controls.Add(ActionButton("Rename", (_, _) => RenameSelectedModel()));
        modelActions.Controls.Add(ActionButton("Export", (_, _) => ExportSelectedModel()));
        modelActions.Controls.Add(ActionButton("Import", (_, _) => ImportModel()));
        modelActions.Controls.Add(ActionButton("Delete", (_, _) => DeleteSelectedModel()));
        modelActions.Controls.Add(ActionButton("Refresh", (_, _) => ReloadProfiles()));
        manager.Controls.Add(modelActions, 0, 3);
        modelsPage.Controls.Add(manager);

        var activityPage = NewPage();
        var logCard = Card(); logCard.Dock = DockStyle.Fill; logCard.AutoSize = false;
        logCard.ColumnCount = 1; logCard.RowCount = 2;
        logCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        logCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        logCard.Controls.Add(SectionTitle("Activity and diagnostics"), 0, 0);
        logCard.Controls.Add(_log, 0, 1);
        activityPage.Controls.Add(logCard);

        var pageList = new[] { setupPage, livePage, personalizationPage, modelsPage, activityPage };
        var pageTextWidths = new Dictionary<Panel, int>();
        Panel? resizingPage = null;
        void FitPageText(Panel page)
        {
            if (ReferenceEquals(page, resizingPage)) return;
            // Use the outer width so the appearance of a vertical scrollbar does not
            // make the wrapped labels and the scrollbar repeatedly resize each other.
            var textWidth = Math.Max(240, page.Width - page.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 40);
            if (pageTextWidths.TryGetValue(page, out var previousWidth) && previousWidth == textWidth) return;
            pageTextWidths[page] = textWidth;
            void Fit(Control parent)
            {
                foreach (Control child in parent.Controls)
                {
                    if (child is Label label && Equals(label.Tag, "responsive-info"))
                    {
                        if (label.MaximumSize.Width != textWidth)
                            label.MaximumSize = new Size(textWidth, 0);
                    }
                    if (child.HasChildren) Fit(child);
                }
            }
            Fit(page);
        }
        foreach (var page in pageList)
        {
            page.ClientSizeChanged += (_, _) => FitPageText(page);
            FitPageText(page);
        }
        ResizeBegin += (_, _) =>
        {
            resizingPage = pageList.FirstOrDefault(page => page.Visible);
            resizingPage?.SuspendLayout();
        };
        ResizeEnd += (_, _) =>
        {
            if (resizingPage is not { } page) return;
            resizingPage = null;
            FitPageText(page);
            page.ResumeLayout(true);
        };
        var titles = new[] { "First-time setup", "Live tracking", "Tongue personalization", "Model manager", "Activity" };
        var subtitles = new[]
        {
            "Connect the Quest and prepare the PC runtime, bridge, gaze, and AMD acceleration.",
            "Check connections, choose features, and start a session.",
            "Record and train a model for your own face.",
            "Name, import, export, and manage personal models.",
            "Follow setup, training, and tracking output here.",
        };
        void ShowPage(int index)
        {
            for (var item = 0; item < pageList.Length; item++)
            {
                pageList[item].Visible = item == index;
                StyleNavigationButton(tabs[item], item == index);
            }
            pageList[index].BringToFront();
            pageTitle.Text = titles[index];
            pageSubtitle.Text = subtitles[index];
        }
        for (var index = 0; index < tabs.Length; index++)
        {
            var selected = index;
            tabs[index].Click += (_, _) => ShowPage(selected);
        }
        ShowPage(_environment.HasOpenedBefore ? 1 : 0);

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Panel, Padding = new Padding(12, 10, 12, 10), Margin = Padding.Empty };
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        var footerIdentity = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty, Padding = Padding.Empty };
        footerIdentity.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        footerIdentity.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        footerIdentity.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        _runStatus.AutoSize = false; _runStatus.Dock = DockStyle.Fill; _runStatus.TextAlign = ContentAlignment.MiddleLeft;
        footerIdentity.Controls.Add(_runStatus, 0, 0);
        footerIdentity.Controls.Add(new Label
        {
            Text = "made with love and dedication by Fwooffy",
            Dock = DockStyle.Fill,
            ForeColor = Muted,
            Font = new Font(UiFontName, 8.5F),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
        }, 0, 1);
        const string creditPrefix = "Thanks to n0tmast3r · based on ";
        const string originalProject = "Qpro-Enhanced-FT";
        const string originalReleasesUrl = "https://github.com/n0tmast3r/Qpro-Enhanced-FT/releases";
        var originalCredit = new LinkLabel
        {
            Text = creditPrefix + originalProject,
            LinkArea = new LinkArea(creditPrefix.Length, originalProject.Length),
            LinkBehavior = LinkBehavior.AlwaysUnderline,
            LinkColor = Accent,
            ActiveLinkColor = Accent,
            VisitedLinkColor = Accent,
            ForeColor = Muted,
            Dock = DockStyle.Fill,
            Font = new Font(UiFontName, 8.5F),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            UseMnemonic = false,
        };
        originalCredit.LinkClicked += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(originalReleasesUrl) { UseShellExecute = true });
            }
            catch (Exception error)
            {
                MessageBox.Show($"Could not open the original releases page: {error.Message}", "Open link", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        footerIdentity.Controls.Add(originalCredit, 0, 2);
        footer.Controls.Add(footerIdentity, 0, 0);
        _start.Dock = DockStyle.Fill; _stop.Dock = DockStyle.Fill;
        _start.AutoSize = false; _stop.AutoSize = false;
        footer.Controls.Add(_start, 1, 0);
        footer.Controls.Add(_stop, 2, 0);
        shell.Controls.Add(footer, 0, 1); shell.SetColumnSpan(footer, 2);
        return shell;
    }
}
