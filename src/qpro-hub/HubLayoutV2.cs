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
        var brandTitle = new Label { Text = "QPRO HUB", Dock = DockStyle.Fill, Font = new Font(UiFontName, 14F, FontStyle.Bold), ForeColor = Color.White, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
        var brandSubtitle = new Label { Text = "Face tracking control", Dock = DockStyle.Fill, ForeColor = Muted, TextAlign = ContentAlignment.MiddleLeft };
        brand.Controls.Add(brandTitle, 0, 0);
        brand.Controls.Add(brandSubtitle, 0, 1);
        sidebar.Controls.Add(brand, 0, 0);

        var liveTab = NavigationButton("Live tracking");
        var setupTab = NavigationButton("First-time setup");
        var personalizationTab = NavigationButton("Personalize");
        var modelsTab = NavigationButton("Model manager");
        var activityTab = NavigationButton("Activity");
        var tabs = new[] { setupTab, liveTab, personalizationTab, modelsTab, activityTab };
        for (var index = 0; index < tabs.Length; index++) sidebar.Controls.Add(tabs[index], 0, index + 1);
        var sidebarNotice = new Label
        {
            Text = "ROOTED QUEST PRO REQUIRED\nGaze and pupil are experimental",
            Dock = DockStyle.Fill,
            ForeColor = Muted,
            TextAlign = ContentAlignment.BottomLeft,
            Font = new Font(UiFontName, 8.5F),
        };
        sidebar.Controls.Add(sidebarNotice, 0, 6);
        sidebarViewport.Controls.Add(sidebar);
        shell.Controls.Add(sidebarViewport, 0, 0);
        void FitSidebarWidth()
        {
            var dpiScale = shell.DeviceDpi / 96F;
            var textWidth = tabs.Max(tab => TextRenderer.MeasureText(tab.Text, tab.Font).Width);
            var requiredWidth = textWidth + sidebar.Padding.Horizontal + (int)Math.Ceiling(18 * dpiScale);
            var sidebarWidth = Math.Max(
                Math.Clamp((int)(shell.ClientSize.Width * 0.2), (int)(180 * dpiScale), (int)(226 * dpiScale)),
                requiredWidth);
            if ((int)shell.ColumnStyles[0].Width != sidebarWidth)
                shell.ColumnStyles[0].Width = sidebarWidth;
        }
        shell.SizeChanged += (_, _) => FitSidebarWidth();

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
            // Each page scrolls independently while the tracking controls remain
            // fixed in the footer, including at small window sizes.
            var page = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Background, Padding = new Padding(22, 8, 22, 20), Visible = false, Tag = "hub-page" };
            pages.Controls.Add(page);
            return page;
        }

        var livePage = NewPage();
        var liveLayout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 5, Padding = Padding.Empty, Margin = Padding.Empty };
        liveLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        liveLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        liveLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
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
            ("Qpro module", _bridgeStatus), ("PC runtime", _runtimeStatus), ("Gaze support", _gazeStatus),
            ("Lower-face inference", _inferenceStatus), ("Pupil processing", _pupilStatus),
        };
        for (var index = 0; index < statusItems.Length; index++)
        {
            var row = 1 + (index / 2) * 2;
            var column = index % 2;
            statuses.Controls.Add(new Label { Text = statusItems[index].Item1, AutoSize = true, ForeColor = Muted, Margin = new Padding(8, 2, 8, 2) }, column, row);
            statuses.Controls.Add(statusItems[index].Item2, column, row + 1);
        }
        liveLayout.Controls.Add(statuses);

        var liveSource = Card(); liveSource.Dock = DockStyle.Top;
        liveSource.ColumnCount = 2;
        liveSource.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        liveSource.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        liveSource.Controls.Add(SectionTitle("Face-tracking source"), 0, 0);
        liveSource.SetColumnSpan(liveSource.GetControlFromPosition(0, 0)!, 2);
        liveSource.Controls.Add(new Label { Text = "Streaming app", AutoSize = true, ForeColor = Muted, Margin = new Padding(8, 9, 8, 4) }, 0, 1);
        liveSource.Controls.Add(_trackingSourceLive, 1, 1);
        _trackingSourceLiveNote.Margin = new Padding(8, 2, 8, 8);
        liveSource.Controls.Add(_trackingSourceLiveNote, 0, 2);
        liveSource.SetColumnSpan(_trackingSourceLiveNote, 2);
        liveLayout.Controls.Add(liveSource);

        var tracking = Card();
        tracking.Dock = DockStyle.Top;
        tracking.ColumnCount = 2;
        tracking.RowCount = 23;
        tracking.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        tracking.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tracking.Controls.Add(SectionTitle("Choose tracking features"), 0, 0);
        tracking.SetColumnSpan(tracking.GetControlFromPosition(0, 0)!, 2);
        tracking.Controls.Add(Info("Choose the features you want, then press Start tracking below."), 0, 1);
        tracking.SetColumnSpan(tracking.GetControlFromPosition(0, 1)!, 2);
        tracking.Controls.Add(_gaze, 0, 2); tracking.SetColumnSpan(_gaze, 2);
        tracking.Controls.Add(new Label { Text = "Eye profile", AutoSize = true, ForeColor = Muted, Margin = new Padding(24, 9, 10, 4) }, 0, 3);
        tracking.Controls.Add(_eyeProfiles, 1, 3);
        tracking.Controls.Add(_tongue, 0, 4); tracking.SetColumnSpan(_tongue, 2);
        tracking.Controls.Add(new Label { Text = "Lower-face model", AutoSize = true, ForeColor = Muted, Margin = new Padding(24, 9, 10, 4) }, 0, 5);
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
        tracking.Controls.Add(_individualCheekPuff, 0, 9); tracking.SetColumnSpan(_individualCheekPuff, 2);
        tracking.Controls.Add(new Label { Text = "Cheek puff style", AutoSize = true, ForeColor = Muted, Margin = new Padding(24, 9, 10, 4) }, 0, 10);
        var cheekPuffActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = Padding.Empty };
        cheekPuffActions.Controls.Add(_cheekPuffStyle);
        cheekPuffActions.Controls.Add(_calibrateCheekPuff);
        tracking.Controls.Add(cheekPuffActions, 1, 10);
        tracking.Controls.Add(Info("Calibrated gives a smooth response from relaxed to full puff, using the developer baseline until you calibrate. Calibrate each streaming app with its Qpro module running in VRCFaceTracking. 1/0 selects a full-strength cheek; Balanced is gentler. Turn off for native values. Changes take effect immediately."), 0, 11);
        tracking.SetColumnSpan(tracking.GetControlFromPosition(0, 11)!, 2);
        tracking.Controls.Add(_individualCheekSuck, 0, 12); tracking.SetColumnSpan(_individualCheekSuck, 2);
        tracking.Controls.Add(new Label { Text = "Cheek suck style", AutoSize = true, ForeColor = Muted, Margin = new Padding(24, 9, 10, 4) }, 0, 13);
        tracking.Controls.Add(_cheekSuckStyle, 1, 13);
        tracking.Controls.Add(Info("Strong selects the leading cheek suck side; Balanced is gentler. Turn off for the streaming app's original values. Changes take effect immediately."), 0, 14);
        tracking.SetColumnSpan(tracking.GetControlFromPosition(0, 14)!, 2);
        tracking.Controls.Add(_eyebrowBoost, 0, 15); tracking.SetColumnSpan(_eyebrowBoost, 2);
        tracking.Controls.Add(new Label { Text = "Eyebrow sensitivity", AutoSize = true, ForeColor = Muted, Margin = new Padding(24, 9, 10, 4) }, 0, 16);
        tracking.Controls.Add(_eyebrowSensitivity, 1, 16);
        tracking.Controls.Add(Info("Adjust left and right brow movement separately from 0.50× to 3.00×. Turn off for native values. Gaze and blinks stay unchanged."), 0, 17);
        tracking.SetColumnSpan(tracking.GetControlFromPosition(0, 17)!, 2);
        tracking.Controls.Add(_pupil, 0, 18); tracking.SetColumnSpan(_pupil, 2);
        tracking.Controls.Add(new Label { Text = "Pupil response", AutoSize = true, ForeColor = Muted, Margin = new Padding(24, 9, 10, 4) }, 0, 19);
        tracking.Controls.Add(_pupilSensitivity, 1, 19);
        tracking.Controls.Add(Info("Pupil tracking is experimental. Response runs from 1.0× to 3.0× in 0.2× steps. Look straight and hold steady until both eyes finish warming up. Independent gaze briefly restarts headset tracking when applied or restored."), 0, 20);
        tracking.SetColumnSpan(tracking.GetControlFromPosition(0, 20)!, 2);
        tracking.Controls.Add(_cameraPreview, 0, 21); tracking.SetColumnSpan(_cameraPreview, 2);
        tracking.Controls.Add(Info("Turn this off to hide the live camera windows. Tongue and pupil tracking will continue. Changes take effect the next time you start tracking."), 0, 22);
        tracking.SetColumnSpan(tracking.GetControlFromPosition(0, 22)!, 2);
        liveLayout.Controls.Add(tracking);
        var cameraCheeks = Card(); cameraCheeks.Dock = DockStyle.Top;
        cameraCheeks.Controls.Add(_cameraCheekPuff);
        cameraCheeks.Controls.Add(Info("Uses a trained tongue + cheeks copy selected under Lower-face model. Quick refinement and Full dataset include cheek camera poses. This is a separate experimental option; turn it off to use your selected native cheek puff style. Restart Qpro tracking after changing it."));
        liveLayout.Controls.Add(cameraCheeks);
        var refresh = ActionButton("Refresh connection status", (_, _) => { ReloadProfiles(); _ = RefreshStatusAsync(); });
        refresh.Dock = DockStyle.Top;
        liveLayout.Controls.Add(refresh);

        var setupPage = NewPage();
        var setupLayout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 6 };
        setupLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < 6; row++) setupLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
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
        var setupSource = Card(); setupSource.Dock = DockStyle.Top;
        setupSource.ColumnCount = 2;
        setupSource.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
        setupSource.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        setupSource.Controls.Add(SectionTitle("Choose your PCVR streaming app"), 0, 0);
        setupSource.SetColumnSpan(setupSource.GetControlFromPosition(0, 0)!, 2);
        setupSource.Controls.Add(new Label { Text = "Face source", AutoSize = true, ForeColor = Muted, Margin = new Padding(5, 8, 8, 3) }, 0, 1);
        setupSource.Controls.Add(_trackingSourceSetup, 1, 1);
        _trackingSourceSetupNote.Margin = new Padding(5, 2, 5, 8);
        setupSource.Controls.Add(_trackingSourceSetupNote, 0, 2);
        setupSource.SetColumnSpan(_trackingSourceSetupNote, 2);
        setupLayout.Controls.Add(setupSource);
        var setupIntro = Card(); setupIntro.Dock = DockStyle.Top;
        setupIntro.Controls.Add(SectionTitle("Required setup checklist"));
        setupIntro.Controls.Add(Info("Install the latest VRCFaceTracking from Steam first. Set up the PC runtime, then install the Qpro module for your streaming app. Installing one source removes the other Qpro source module. Close VRCFaceTracking before installing or uninstalling a module, then start it again afterward."));
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
        _setupRuntimeButton.Click += async (_, _) => await RunSetupStepAsync("PC runtime setup", "setup-runtime.ps1", "PC runtime is ready.", "Next: close VRCFaceTracking and install the Qpro module for your streaming app.");
        _setupBridgeButton.Click += async (_, _) => await InstallQproModuleAsync(steamLink: false);
        _setupSteamLinkModuleButton.Click += async (_, _) => await InstallQproModuleAsync(steamLink: true);
        _uninstallBridgeButton.Click += async (_, _) =>
        {
            if (VrcftModuleProcessRunning())
            {
                MessageBox.Show(this, "Close VRCFaceTracking and wait for its ModuleProcess helper to exit, then press Uninstall Qpro module again.", "Close VRCFaceTracking", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(this,
                    "Remove the Qpro module from VRCFaceTracking and restore any Virtual Desktop modules saved by this Qpro copy?\n\nPersonal tongue models and captures will stay in place.",
                    "Uninstall Qpro module", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            await RunSetupStepAsync("Uninstall Qpro module", "uninstall-vrcft-eye-bridge.ps1",
                "The Qpro VRCFaceTracking module has been removed.",
                "Restart VRCFaceTracking. If normal Virtual Desktop face tracking is missing, install its official module again.");
        };
        _setupGazeButton.Click += async (_, _) => await PrepareGazeAsync();
        _recoverGazeButton.Click += async (_, _) => await RecoverGazeAsync();
        _inspectGazeButton.Click += async (_, _) => await InspectGazeAsync();
        _resetLegacyGazeButton.Click += async (_, _) => await ResetLegacyGazeAsync();
        // Step cards size to their copy and actions, avoiding large empty bands
        // at high resolutions and clipped descriptions in narrow windows.
        var setupCards = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 3 };
        setupCards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < 3; row++) setupCards.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        setupCards.Controls.Add(SetupStepCard("1", "PC runtime", "Prepares Qpro's private Python and installs CPU/GPU libraries. Works whether Python is installed on this PC or not.", _setupRuntimeStatus, _setupRuntimeButton), 0, 0);
        var moduleCard = SetupStepCard("2", "VRCFT module", "Choose Virtual Desktop or Steam Link. Installing one removes the other Qpro source module. Close VRCFaceTracking first.",
            _setupBridgeStatus, _setupBridgeButton, _setupSteamLinkModuleButton, _uninstallBridgeButton);
        setupCards.Controls.Add(moduleCard, 0, 1);
        setupCards.Controls.Add(SetupStepCard("3", "Independent gaze",
            "Start with Check gaze setup to identify the headset's gaze method and see what to do next. Use one independent gaze method at a time.\n\n" +
            "Recover Qpro gaze: Restores the previous eye-model state saved for a recorded Qpro session.\n" +
            "Reset legacy gaze: Chooses normal, nonexperimental gaze selection for an older session without a record, after confirmation.\n" +
            "Neither recovery button disables a Magisk module or uninstalls the PC module.",
            _setupGazeStatus, _inspectGazeButton, _setupGazeButton, _recoverGazeButton, _resetLegacyGazeButton), 0, 2);
        setupLayout.Controls.Add(setupCards);
        var amdCard = Card(); amdCard.Dock = DockStyle.Top;
        amdCard.Controls.Add(SectionTitle("Optional AMD ROCm acceleration"));
        amdCard.Controls.Add(Info("Install ROCm 10.0 for a mapped discrete Radeon. This Qpro path is experimental until GPU training and inference checks pass. An existing verified ROCm 7.2.1 environment remains a fallback on AMD's older supported GPU list. Install the PC runtime first."));
        amdCard.Controls.Add(_amdGpuStatus);
        var amdSupportLink = new LinkLabel { Text = "ROCm 7.2.1 fallback GPU list", AutoSize = true, LinkColor = Accent, ActiveLinkColor = Accent, VisitedLinkColor = Accent, Margin = new Padding(5, 3, 5, 8) };
        amdSupportLink.LinkClicked += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("https://rocm.docs.amd.com/projects/radeon-ryzen/en/docs-7.2.1/docs/compatibility/compatibilityrad/windows/windows_compatibility.html") { UseShellExecute = true }); }
            catch (Exception error) { MessageBox.Show(this, error.Message, "Could not open AMD support list"); }
        };
        var amdExperimentalLink = new LinkLabel { Text = "AMD TheRock ROCm 10.0 GPU targets", AutoSize = true, LinkColor = Accent, ActiveLinkColor = Accent, VisitedLinkColor = Accent, Margin = new Padding(5, 3, 5, 8) };
        amdExperimentalLink.LinkClicked += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("https://github.com/ROCm/TheRock/blob/main/RELEASES.md") { UseShellExecute = true }); }
            catch (Exception error) { MessageBox.Show(this, error.Message, "Could not open AMD TheRock details"); }
        };
        amdCard.Controls.Add(amdExperimentalLink);
        amdCard.Controls.Add(amdSupportLink);
        amdCard.Controls.Add(_setupAmdStatus);
        amdCard.Controls.Add(_setupAmdButton);
        setupLayout.Controls.Add(amdCard);

        var personalizationPage = NewPage();
        var personalLayout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 4 };
        personalLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        personalLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        personalLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        personalLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        personalLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        personalizationPage.Controls.Add(personalLayout);
        var personalIntro = Card(); personalIntro.Dock = DockStyle.Top;
        personalIntro.Controls.Add(SectionTitle("Lower-face calibration"));
        personalIntro.Controls.Add(Info("Record tongue and cheek poses to fit tracking to your face and headset position. Quick refinement and Full dataset include 21 cheek camera cards. The camera cheek option is experimental and remains off until you enable it."));
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
        var choices = new TableLayoutPanel { Dock = DockStyle.Top, Height = 1200, ColumnCount = 1, RowCount = 3 };
        choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        choices.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33F));
        choices.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33F));
        choices.RowStyles.Add(new RowStyle(SizeType.Percent, 33.34F));
        choices.Controls.Add(WorkflowCard("Quick refinement · 15–30 min", "Refines the selected tongue model, then trains cheek camera outputs from 21 guided cheek cards. Inherits some parent-model bias.", _quickDatasets, _quickQueueStatus, _quickRecordedDatasets,
            ActionButton("1. Record refinement", async (_, _) => await ConfirmCaptureAsync(TongueDatasetKind.Quick)), ActionButton("2. Train personalized copy", async (_, _) => await TrainTongueAsync(TongueDatasetKind.Quick)),
            ActionButton("Delete selected dataset…", (_, _) => DeleteRecordedDataset(TongueDatasetKind.Quick))), 0, 0);
        choices.Controls.Add(WorkflowCard("Focused diagonals + facial hair · 15–30 min", "Targets diagonal tongue errors and facial-hair shadows with matched hidden and visible poses. Fine-tunes a new model.", _focusedDatasets, _focusedQueueStatus, _focusedRecordedDatasets,
            ActionButton("1. Record focused dataset", async (_, _) => await ConfirmCaptureAsync(TongueDatasetKind.Focused)), ActionButton("2. Train focused copy", async (_, _) => await TrainTongueAsync(TongueDatasetKind.Focused)),
            ActionButton("Delete selected dataset…", (_, _) => DeleteRecordedDataset(TongueDatasetKind.Focused))), 0, 1);
        choices.Controls.Add(WorkflowCard("Full dataset · 60–120 min", "Broad tongue coverage plus 21 cheek camera cards. Trains a new lower-face copy and requires careful capture time.", _fullDatasets, _fullQueueStatus, _fullRecordedDatasets,
            ActionButton("1. Record full dataset", async (_, _) => await ConfirmCaptureAsync(TongueDatasetKind.Full)), ActionButton("2. Train new personal model", async (_, _) => await TrainTongueAsync(TongueDatasetKind.Full)),
            ActionButton("Delete selected dataset…", (_, _) => DeleteRecordedDataset(TongueDatasetKind.Full))), 0, 2);
        personalLayout.Controls.Add(choices);
        var cameraCheekTraining = Card(); cameraCheekTraining.Dock = DockStyle.Top;
        cameraCheekTraining.Controls.Add(SectionTitle("Experimental tongue + cheeks model"));
        cameraCheekTraining.Controls.Add(Info("Adds cheek camera outputs to a new copy of a selected tongue model. Native cheek calibration remains available. Record all guided poses and test the resulting copy before using it for a session."));
        cameraCheekTraining.Controls.Add(new Label { Text = "Parent tongue model", AutoSize = true, ForeColor = Muted });
        cameraCheekTraining.Controls.Add(_cheekCameraBaseModels);
        cameraCheekTraining.Controls.Add(new Label { Text = "Completed cheek camera dataset", AutoSize = true, ForeColor = Muted });
        cameraCheekTraining.Controls.Add(_cheekCameraDatasets);
        cameraCheekTraining.Controls.Add(_cheekCameraDatasetNote);
        var cameraCheekActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        cameraCheekActions.Controls.Add(_recordCameraCheeks);
        cameraCheekActions.Controls.Add(_trainCameraCheeks);
        cameraCheekTraining.Controls.Add(cameraCheekActions);
        personalLayout.Controls.Add(cameraCheekTraining);

        var modelsPage = NewPage();
        var manager = Card(); manager.Dock = DockStyle.Fill; manager.AutoSize = false; manager.MinimumSize = new Size(0, 380);
        manager.ColumnCount = 1; manager.RowCount = 4;
        manager.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        manager.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        manager.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        manager.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        manager.Controls.Add(SectionTitle("Lower-face model manager"), 0, 0);
        manager.Controls.Add(Info("Manage tongue models and experimental tongue + camera cheeks copies. Export creates a portable .qptonguemodel package containing the paired files; import assigns a safe new version."), 0, 1);
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
            // A changing child width can make AutoScroll retain a horizontal
            // offset; keep headings anchored to the left while preserving Y.
            if (page.AutoScrollPosition.X != 0)
                page.AutoScrollPosition = new Point(0, -page.AutoScrollPosition.Y);
            // Use the outer width so the appearance of a vertical scrollbar does not
            // make the wrapped labels and the scrollbar repeatedly resize each other.
            // Cap long setup notes on large displays so they remain easy to read.
            var availableTextWidth = page.Width - page.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 40;
            var textWidth = Math.Min((int)Math.Ceiling(900 * DeviceDpi / 96F), Math.Max(240, availableTextWidth));
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
            // Deferring text reflow until mouse resizing ends keeps the wireless
            // setup page responsive while its taller notes are on screen.
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
        var titles = new[] { "First-time setup", "Live tracking", "Lower-face calibration", "Model manager", "Activity" };
        var subtitles = new[]
        {
            "Connect your Quest and set up tracking.",
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
        var madeByCredit = new Label
        {
            Text = "made with love and dedication by Fwooffy",
            Dock = DockStyle.Fill,
            ForeColor = Muted,
            Font = new Font(UiFontName, 8.5F),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
        };
        footerIdentity.Controls.Add(madeByCredit, 0, 1);
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

        void FitSidebarHeight()
        {
            var scale = DeviceDpi / 96F;
            int Px(int logical) => Math.Max(1, (int)Math.Ceiling(logical * scale));
            var textWidth = Math.Max(Px(90), sidebar.Width - sidebar.Padding.Horizontal - Px(18));
            var noticeHeight = Math.Max(Px(86), TextRenderer.MeasureText(sidebarNotice.Text,
                sidebarNotice.Font, new Size(textWidth, int.MaxValue), TextFormatFlags.WordBreak).Height + Px(12));
            var baseHeight = sidebar.Padding.Vertical + (int)Math.Ceiling(
                sidebar.RowStyles.Cast<RowStyle>().Take(6).Sum(style => style.Height));
            var availableHeight = shell.ClientSize.Height - (int)Math.Ceiling(shell.RowStyles[1].Height);
            // The compact sidebar still has all five navigation buttons; the
            // decorative notice yields to them when the fixed footer needs room.
            var showNotice = availableHeight >= baseHeight + noticeHeight + Px(4);
            sidebarNotice.Visible = showNotice;
            sidebar.RowStyles[6].Height = showNotice ? noticeHeight : 0;
            sidebar.Height = baseHeight + (showNotice ? noticeHeight : 0);
        }
        shell.SizeChanged += (_, _) => FitSidebarHeight();

        // TableLayoutPanel absolute rows and owner-drawn controls do not all grow
        // with WinForms' font scaling. Recalculate them from the monitor DPI so
        // large text cannot be cut off at 150–300% display scaling.
        void ApplyDpiLayout()
        {
            var scale = DeviceDpi / 96F;
            int Px(int logical) => Math.Max(1, (int)Math.Ceiling(logical * scale));
            FitSidebarWidth();
            shell.SuspendLayout();
            main.SuspendLayout();
            tracking.SuspendLayout();
            try
            {
                int TextHeight(Control control) => (int)Math.Ceiling(control.Font.GetHeight()) + Px(10);
                header.RowStyles[0].Height = Math.Max(Px(48), TextHeight(pageTitle));
                header.RowStyles[1].Height = Math.Max(Px(28), TextHeight(pageSubtitle));
                main.RowStyles[0].Height = Math.Max(Px(100),
                    header.Padding.Vertical + (int)header.RowStyles[0].Height + (int)header.RowStyles[1].Height + Px(8));
                var creditHeight = Math.Max(TextHeight(madeByCredit), TextHeight(originalCredit));
                footerIdentity.RowStyles[0].Height = Math.Max(Px(30), TextHeight(_runStatus));
                shell.RowStyles[1].Height = Math.Max(Px(110), footer.Padding.Vertical
                    + (int)footerIdentity.RowStyles[0].Height + creditHeight * 2 + Px(8));
                liveLayout.RowStyles[3].Height = Px(52);
                var sidebarTextWidth = Math.Max(Px(90), sidebar.Width - sidebar.Padding.Horizontal - Px(18));
                var brandSubtitleHeight = TextRenderer.MeasureText(brandSubtitle.Text, brandSubtitle.Font,
                    new Size(sidebarTextWidth, int.MaxValue), TextFormatFlags.WordBreak).Height;
                brand.RowStyles[0].Height = Math.Max(Px(43), TextHeight(brandTitle));
                brand.RowStyles[1].Height = Math.Max(Px(30), brandSubtitleHeight + Px(6));
                sidebar.RowStyles[0].Height = Math.Max(Px(88),
                    (int)brand.RowStyles[0].Height + (int)brand.RowStyles[1].Height + Px(12));
                for (var row = 1; row <= 5; row++)
                    sidebar.RowStyles[row].Height = Math.Max(Px(52), TextHeight(tabs[row - 1]) + Px(12));
                FitSidebarHeight();
                tracking.ColumnStyles[0].Width = Math.Max(Px(160),
                    Math.Max(TextRenderer.MeasureText("Eyebrow sensitivity", Font).Width,
                        TextRenderer.MeasureText("Lower-face model", Font).Width) + Px(64));
                connectionPicker.ColumnStyles[0].Width = Px(155);
                setupSource.ColumnStyles[0].Width = Px(155);
                liveSource.ColumnStyles[0].Width = Px(160);
                foreach (TableLayoutPanel card in setupCards.Controls)
                {
                    var actionCount = card.GetControlFromPosition(0, 4) is TableLayoutPanel actions ? actions.RowCount : 1;
                    card.RowStyles[4].Height = Px(54 * actionCount);
                }
                choices.Height = Px(1200);

                foreach (var toggle in new[] { _gaze, _tongue, _pupil, _cameraPreview,
                    _individualCheekPuff, _individualCheekSuck, _eyebrowBoost })
                    toggle.Height = Math.Max(Px(38), (int)Math.Ceiling(toggle.Font.GetHeight()) + Px(14));
                _fps.Width = Px(84);
                _pupilSensitivity.Width = Px(220);
                _eyebrowSensitivity.Width = Px(220);
                _cheekPuffStyle.Width = Px(245);
                _cheekSuckStyle.Width = Px(245);
                _visibilityMode.Width = Math.Max(Px(265),
                    TextRenderer.MeasureText("Weighted camera + native", _visibilityMode.Font).Width + Px(44));
                _smoothing.Width = Px(180);
                _smoothing.Height = Px(32);
                foreach (var box in new[] { _eyeProfiles, _tongueModels, _fps, _pupilSensitivity,
                    _eyebrowSensitivity, _cheekPuffStyle, _cheekSuckStyle,
                    _visibilityMode, _connectionMode, _trackingSourceSetup, _trackingSourceLive,
                    _quickDatasets, _focusedDatasets, _fullDatasets,
                    _quickRecordedDatasets, _focusedRecordedDatasets, _fullRecordedDatasets })
                    box.ItemHeight = Math.Max(Px(26), (int)Math.Ceiling(box.Font.GetHeight()) + Px(8));
                _modelList.ItemHeight = Math.Max(Px(40), (int)Math.Ceiling(_modelList.Font.GetHeight()) + Px(12));
                foreach (var button in new[] { _setupRuntimeButton, _setupBridgeButton, _setupSteamLinkModuleButton, _uninstallBridgeButton,
                    _setupGazeButton, _recoverGazeButton, _inspectGazeButton, _resetLegacyGazeButton, _setupAmdButton, _enableWirelessButton, _connectWirelessButton,
                    _pairWirelessButton, _disableWirelessButton })
                    button.Height = Px(42);
            }
            finally
            {
                tracking.ResumeLayout(true);
                main.ResumeLayout(true);
                shell.ResumeLayout(true);
            }
            foreach (var page in pageList) FitPageText(page);
        }
        Shown += (_, _) => ApplyDpiLayout();
        DpiChanged += (_, _) => BeginInvoke((Action)ApplyDpiLayout);
        return shell;
    }
}
