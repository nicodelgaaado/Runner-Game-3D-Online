using UnityEngine;
using UnityEngine.UIElements;

namespace RunnerGame.Online
{
    [RequireComponent(typeof(UIDocument))]
    public class BootstrapMenuView : MonoBehaviour
    {
        private const int JoinCodeMaxLength = 16;
        private const float JoinCodeFieldHeight = 48f;
        private const float JoinCodeFontSize = 18f;
        private const float JoinCodeLineHeight = 22f;
        private const float JoinCodeVerticalPadding = 4f;

        private SessionBootstrapper bootstrapper;
        private UIDocument document;
        private PanelSettings panelSettings;
        private Font menuFont;
        private VisualElement root;
        private VisualElement panel;
        private VisualElement sessionInfo;
        private VisualElement debugPanel;
        private Label statusLabel;
        private Label roomCodeLabel;
        private Label playersLabel;
        private Label modeLabel;
        private Label debugLabel;
        private TextField joinCodeField;
        private Button hostButton;
        private Button joinButton;
        private Button leaveButton;
        private bool canStartSession;

        public void Initialize(SessionBootstrapper owner, Font fontAsset)
        {
            bootstrapper = owner;
            EnsureDocument();
            BuildInterface(fontAsset);
        }

        public void Refresh(SessionBootstrapper.BootstrapMenuSnapshot snapshot)
        {
            if (root == null)
            {
                return;
            }

            SetDisplay(root, snapshot.ShouldShowMenu);
            if (!snapshot.ShouldShowMenu)
            {
                return;
            }

            statusLabel.text = snapshot.StatusMessage;
            roomCodeLabel.text = string.IsNullOrWhiteSpace(snapshot.SessionCode)
                ? "Room Code: --"
                : $"Room Code: {snapshot.SessionCode}";
            playersLabel.text = $"Players: {snapshot.PlayerCount}/{snapshot.MaxPlayers}";
            modeLabel.text = "Mode: Shared";
            debugLabel.text = snapshot.DebugDetails;

            SetDisplay(sessionInfo, snapshot.ShowSessionInfo);
            SetDisplay(leaveButton, snapshot.CanLeaveSession);
            SetDisplay(debugPanel, snapshot.ShowDebugPanel);

            canStartSession = snapshot.CanStartSession;
            hostButton.SetEnabled(canStartSession);
            joinCodeField.SetEnabled(canStartSession);
            RefreshJoinButtonState();
            leaveButton.SetEnabled(snapshot.CanLeaveSession);
        }

        private void EnsureDocument()
        {
            document = GetComponent<UIDocument>();
            if (document.panelSettings == null)
            {
                panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                panelSettings.name = "Bootstrap Menu Panel Settings";
                panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                panelSettings.referenceResolution = new Vector2Int(1920, 1080);
                panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                panelSettings.match = 0.5f;
                panelSettings.sortingOrder = 100;
                document.panelSettings = panelSettings;
            }

            document.sortingOrder = 100;
        }

        private void BuildInterface(Font fontAsset)
        {
            menuFont = LoadMenuFont(fontAsset);
            root = document.rootVisualElement;
            root.Clear();
            root.name = "bootstrap-menu-root";
            root.pickingMode = PickingMode.Position;
            root.style.flexGrow = 1f;
            root.style.alignItems = Align.Center;
            root.style.justifyContent = Justify.Center;
            root.style.paddingLeft = 28f;
            root.style.paddingRight = 28f;
            root.style.paddingTop = 28f;
            root.style.paddingBottom = 28f;
            ApplyFont(root);

            panel = new VisualElement { name = "bootstrap-menu-panel" };
            panel.style.width = Length.Percent(92f);
            panel.style.maxWidth = 540f;
            panel.style.minWidth = 300f;
            panel.style.paddingLeft = 30f;
            panel.style.paddingRight = 30f;
            panel.style.paddingTop = 28f;
            panel.style.paddingBottom = 28f;
            panel.style.backgroundColor = new Color(0.035f, 0.047f, 0.065f, 0.88f);
            panel.style.borderTopLeftRadius = 8f;
            panel.style.borderTopRightRadius = 8f;
            panel.style.borderBottomLeftRadius = 8f;
            panel.style.borderBottomRightRadius = 8f;
            panel.style.borderLeftWidth = 1f;
            panel.style.borderRightWidth = 1f;
            panel.style.borderTopWidth = 1f;
            panel.style.borderBottomWidth = 1f;
            panel.style.borderLeftColor = new Color(1f, 1f, 1f, 0.12f);
            panel.style.borderRightColor = new Color(1f, 1f, 1f, 0.08f);
            panel.style.borderTopColor = new Color(1f, 1f, 1f, 0.12f);
            panel.style.borderBottomColor = new Color(1f, 1f, 1f, 0.08f);
            panel.style.alignItems = Align.Stretch;
            ApplyFont(panel);
            root.Add(panel);

            Label eyebrow = MakeFixedLabel("PRIVATE RACE", 12, new Color(0.55f, 0.73f, 0.96f, 1f), TextAnchor.MiddleCenter, 18f);
            eyebrow.style.marginBottom = 8f;
            panel.Add(eyebrow);

            Label title = MakeFixedLabel("RUNNER GAME ONLINE", 34, Color.white, TextAnchor.MiddleCenter, 44f);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 14f;
            panel.Add(title);

            statusLabel = MakeFixedLabel(string.Empty, 15, new Color(0.82f, 0.88f, 0.94f, 1f), TextAnchor.MiddleCenter, 42f);
            statusLabel.style.marginBottom = 22f;
            statusLabel.style.whiteSpace = WhiteSpace.Normal;
            panel.Add(statusLabel);

            hostButton = MakeButton("Host Private Match", OnHostClicked, true);
            panel.Add(hostButton);

            Label joinLabel = MakeFixedLabel("Join Code", 12, new Color(0.62f, 0.69f, 0.76f, 1f), TextAnchor.MiddleLeft, 18f);
            joinLabel.style.marginTop = 18f;
            joinLabel.style.marginBottom = 6f;
            panel.Add(joinLabel);

            joinCodeField = new TextField { name = "join-code-field", isDelayed = false };
            joinCodeField.focusable = true;
            joinCodeField.pickingMode = PickingMode.Position;
            joinCodeField.maxLength = JoinCodeMaxLength;
            joinCodeField.style.height = JoinCodeFieldHeight;
            joinCodeField.style.marginBottom = 10f;
            joinCodeField.style.paddingLeft = 12f;
            joinCodeField.style.paddingRight = 12f;
            joinCodeField.style.paddingTop = JoinCodeVerticalPadding;
            joinCodeField.style.paddingBottom = 0f;
            joinCodeField.style.alignSelf = Align.Stretch;
            joinCodeField.style.flexShrink = 0f;
            joinCodeField.style.backgroundColor = new Color(0.08f, 0.105f, 0.14f, 0.98f);
            joinCodeField.style.color = Color.white;
            joinCodeField.style.fontSize = JoinCodeFontSize;
            joinCodeField.style.unityTextAlign = TextAnchor.MiddleLeft;
            joinCodeField.style.borderTopLeftRadius = 6f;
            joinCodeField.style.borderTopRightRadius = 6f;
            joinCodeField.style.borderBottomLeftRadius = 6f;
            joinCodeField.style.borderBottomRightRadius = 6f;
            joinCodeField.style.borderLeftWidth = 1f;
            joinCodeField.style.borderRightWidth = 1f;
            joinCodeField.style.borderTopWidth = 1f;
            joinCodeField.style.borderBottomWidth = 1f;
            joinCodeField.style.borderLeftColor = new Color(1f, 1f, 1f, 0.12f);
            joinCodeField.style.borderRightColor = new Color(1f, 1f, 1f, 0.12f);
            joinCodeField.style.borderTopColor = new Color(1f, 1f, 1f, 0.12f);
            joinCodeField.style.borderBottomColor = new Color(1f, 1f, 1f, 0.12f);
            joinCodeField.RegisterValueChangedCallback(OnJoinCodeChanged);
            joinCodeField.RegisterCallback<KeyDownEvent>(OnJoinCodeKeyDown);
            ApplyTextFieldTextStyle(joinCodeField);
            panel.Add(joinCodeField);

            joinButton = MakeButton("Join Match", OnJoinClicked, false);
            panel.Add(joinButton);

            sessionInfo = new VisualElement { name = "session-info" };
            sessionInfo.style.marginTop = 18f;
            sessionInfo.style.paddingTop = 14f;
            sessionInfo.style.paddingBottom = 14f;
            sessionInfo.style.paddingLeft = 16f;
            sessionInfo.style.paddingRight = 16f;
            sessionInfo.style.backgroundColor = new Color(0.07f, 0.09f, 0.12f, 0.92f);
            sessionInfo.style.borderTopLeftRadius = 6f;
            sessionInfo.style.borderTopRightRadius = 6f;
            sessionInfo.style.borderBottomLeftRadius = 6f;
            sessionInfo.style.borderBottomRightRadius = 6f;
            panel.Add(sessionInfo);

            roomCodeLabel = MakeFixedLabel(string.Empty, 14, Color.white, TextAnchor.MiddleLeft, 22f);
            playersLabel = MakeFixedLabel(string.Empty, 14, new Color(0.80f, 0.86f, 0.92f, 1f), TextAnchor.MiddleLeft, 22f);
            modeLabel = MakeFixedLabel(string.Empty, 14, new Color(0.80f, 0.86f, 0.92f, 1f), TextAnchor.MiddleLeft, 22f);
            sessionInfo.Add(roomCodeLabel);
            sessionInfo.Add(playersLabel);
            sessionInfo.Add(modeLabel);

            leaveButton = MakeButton("Leave Match", OnLeaveClicked, false);
            leaveButton.style.marginTop = 12f;
            panel.Add(leaveButton);

            debugPanel = new VisualElement { name = "bootstrap-debug-panel" };
            debugPanel.style.marginTop = 16f;
            debugPanel.style.paddingTop = 12f;
            debugPanel.style.paddingBottom = 12f;
            debugPanel.style.paddingLeft = 14f;
            debugPanel.style.paddingRight = 14f;
            debugPanel.style.backgroundColor = new Color(0.01f, 0.015f, 0.02f, 0.85f);
            debugPanel.style.borderTopLeftRadius = 6f;
            debugPanel.style.borderTopRightRadius = 6f;
            debugPanel.style.borderBottomLeftRadius = 6f;
            debugPanel.style.borderBottomRightRadius = 6f;
            panel.Add(debugPanel);

            Label debugTitle = MakeFixedLabel("Bootstrap Debug", 12, new Color(0.60f, 0.78f, 1f, 1f), TextAnchor.MiddleLeft, 18f);
            debugTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            debugTitle.style.marginBottom = 8f;
            debugPanel.Add(debugTitle);

            debugLabel = MakeFixedLabel(string.Empty, 11, new Color(0.75f, 0.82f, 0.90f, 1f), TextAnchor.MiddleLeft, 260f);
            debugLabel.style.whiteSpace = WhiteSpace.Normal;
            debugPanel.Add(debugLabel);
        }

        private Label MakeLabel(string text, int fontSize, Color color, TextAnchor alignment)
        {
            Label label = new Label(text);
            label.style.fontSize = fontSize;
            label.style.color = color;
            label.style.unityTextAlign = alignment;
            label.style.marginLeft = 0f;
            label.style.marginRight = 0f;
            label.style.marginTop = 0f;
            label.style.marginBottom = 0f;
            label.style.whiteSpace = WhiteSpace.Normal;
            ApplyFont(label);
            return label;
        }

        private Button MakeButton(string text, System.Action callback, bool primary)
        {
            Button button = new Button(callback) { text = text };
            button.focusable = true;
            button.pickingMode = PickingMode.Position;
            button.style.height = 48f;
            button.style.flexShrink = 0f;
            button.style.marginTop = 0f;
            button.style.marginBottom = 10f;
            button.style.borderTopLeftRadius = 6f;
            button.style.borderTopRightRadius = 6f;
            button.style.borderBottomLeftRadius = 6f;
            button.style.borderBottomRightRadius = 6f;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.fontSize = 15f;
            button.style.color = primary ? new Color(0.015f, 0.025f, 0.04f, 1f) : Color.white;
            button.style.backgroundColor = primary
                ? new Color(0.47f, 0.72f, 1f, 1f)
                : new Color(0.13f, 0.16f, 0.20f, 1f);
            button.style.borderLeftWidth = 0f;
            button.style.borderRightWidth = 0f;
            button.style.borderTopWidth = 0f;
            button.style.borderBottomWidth = 0f;
            ApplyFont(button);
            button.RegisterCallback<AttachToPanelEvent>(_ => ApplyButtonTextStyle(button, primary));
            ApplyButtonTextStyle(button, primary);
            return button;
        }

        private static Font LoadMenuFont(Font fontAsset)
        {
            if (fontAsset != null)
            {
                return fontAsset;
            }

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return font != null ? font : Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        private void ApplyFont(VisualElement element)
        {
            if (element != null && menuFont != null)
            {
                element.style.unityFont = menuFont;
            }
        }

        private Label MakeFixedLabel(string text, int fontSize, Color color, TextAnchor alignment, float minHeight)
        {
            Label label = MakeLabel(text, fontSize, color, alignment);
            label.style.minHeight = minHeight;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        private void ApplyButtonTextStyle(Button button, bool primary)
        {
            Color textColor = primary ? new Color(0.015f, 0.025f, 0.04f, 1f) : Color.white;
            button.style.color = textColor;
            button.style.unityTextAlign = TextAnchor.MiddleCenter;
            button.style.whiteSpace = WhiteSpace.Normal;
            ApplyFont(button);

            button.Query<TextElement>().ForEach(text =>
            {
                ApplyFont(text);
                text.style.color = textColor;
                text.style.fontSize = 15f;
                text.style.unityFontStyleAndWeight = FontStyle.Bold;
                text.style.unityTextAlign = TextAnchor.MiddleCenter;
                text.style.whiteSpace = WhiteSpace.Normal;
            });
        }

        private void ApplyTextFieldTextStyle(TextField field)
        {
            ApplyFont(field);
            field.RegisterCallback<AttachToPanelEvent>(_ => ApplyTextFieldTextStyleNow(field));
            ApplyTextFieldTextStyleNow(field);
        }

        private void ApplyTextFieldTextStyleNow(TextField field)
        {
            Color transparent = new Color(0f, 0f, 0f, 0f);

            field.Query<VisualElement>().ForEach(element =>
            {
                if (element is TextElement)
                {
                    return;
                }

                bool isInputContainer = element.ClassListContains("unity-base-field__input")
                    || element.ClassListContains("unity-base-text-field__input");
                if (!isInputContainer)
                {
                    return;
                }

                ApplyFont(element);
                element.style.flexGrow = 1f;
                element.style.flexShrink = 1f;
                element.style.alignItems = Align.Center;
                element.style.justifyContent = Justify.Center;
                element.style.marginLeft = 0f;
                element.style.marginRight = 0f;
                element.style.marginTop = 0f;
                element.style.marginBottom = 0f;
                element.style.paddingLeft = 0f;
                element.style.paddingRight = 0f;
                element.style.paddingTop = 0f;
                element.style.paddingBottom = 0f;
                element.style.backgroundColor = transparent;
                element.style.borderLeftWidth = 0f;
                element.style.borderRightWidth = 0f;
                element.style.borderTopWidth = 0f;
                element.style.borderBottomWidth = 0f;
                element.style.color = Color.white;
                element.style.fontSize = JoinCodeFontSize;
                element.style.unityTextAlign = TextAnchor.MiddleLeft;
                element.style.whiteSpace = WhiteSpace.NoWrap;
            });

            field.Query<TextElement>().ForEach(text =>
            {
                ApplyFont(text);
                text.style.color = Color.white;
                text.style.fontSize = JoinCodeFontSize;
                text.style.height = JoinCodeLineHeight;
                text.style.marginLeft = 0f;
                text.style.marginRight = 0f;
                text.style.marginTop = 0f;
                text.style.marginBottom = 0f;
                text.style.unityTextAlign = TextAnchor.MiddleLeft;
                text.style.whiteSpace = WhiteSpace.NoWrap;
            });
        }

        private void OnHostClicked()
        {
            if (!canStartSession)
            {
                return;
            }

            bootstrapper?.SubmitHostFromMenu();
        }

        private void OnJoinClicked()
        {
            string joinCode = NormalizeJoinCode(joinCodeField.value);
            if (!canStartSession || string.IsNullOrWhiteSpace(joinCode))
            {
                return;
            }

            bootstrapper?.SubmitJoinFromMenu(joinCode);
        }

        private void OnLeaveClicked()
        {
            bootstrapper?.SubmitLeaveFromMenu();
        }

        private void OnJoinCodeChanged(ChangeEvent<string> evt)
        {
            RefreshJoinButtonState();
        }

        private void OnJoinCodeKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(NormalizeJoinCode(joinCodeField.value)))
            {
                OnJoinClicked();
                evt.StopPropagation();
            }
        }

        private void RefreshJoinButtonState()
        {
            if (joinButton == null || joinCodeField == null)
            {
                return;
            }

            bool hasJoinCode = !string.IsNullOrWhiteSpace(NormalizeJoinCode(joinCodeField.value));
            joinButton.SetEnabled(canStartSession && hasJoinCode);
        }

        private static string NormalizeJoinCode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string normalized = value.Trim().ToUpperInvariant();
            return normalized.Length <= JoinCodeMaxLength
                ? normalized
                : normalized.Substring(0, JoinCodeMaxLength);
        }

        private static void SetDisplay(VisualElement element, bool visible)
        {
            if (element != null)
            {
                element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void OnDestroy()
        {
            if (panelSettings == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(panelSettings);
            }
            else
            {
                DestroyImmediate(panelSettings);
            }
        }
    }
}
