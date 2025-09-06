using Content.Client._Ronstation.UserInterface.Systems.Emotes.Controls;
using Content.Client._Ronstation.UserInterface.Systems.Emotes.Windows;
using Content.Client.Gameplay;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.MenuBar.Widgets;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared.Chat.Prototypes;
using Content.Shared.Input;
using Content.Shared.Speech;
using Content.Shared.Whitelist;
using JetBrains.Annotations;
using Robust.Client.Player;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Input.Binding;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using static Robust.Client.UserInterface.Controls.BaseButton;

namespace Content.Client._Ronstation.UserInterface.Systems.Emotes;

[UsedImplicitly]
public sealed class EmotesUIController : UIController, IOnStateChanged<GameplayState>
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    private MenuButton? EmotesButton => UIManager.GetActiveUIWidgetOrNull<GameTopMenuBar>()?.EmotesButton;
    private SimpleRadialMenu? _menu;
    private EmotesWindow? _altMenu;

    private static readonly Dictionary<EmoteCategory, (string Tooltip, SpriteSpecifier Sprite)> EmoteGroupingInfo =
        new()
        {
            [EmoteCategory.General] = ("emote-menu-category-general",
                new SpriteSpecifier.Rsi(new ResPath("/Textures/Clothing/Head/Soft/mimesoft.rsi"), "icon")),
            [EmoteCategory.Hands] = ("emote-menu-category-hands",
                new SpriteSpecifier.Rsi(new ResPath("/Textures/Clothing/Hands/Gloves/latex.rsi"), "icon")),
            [EmoteCategory.Vocal] = ("emote-menu-category-vocal",
                new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/Emotes/vocal.png"))),
        };

    public void OnStateEntered(GameplayState state)
    {
        DebugTools.Assert(_menu == null && _altMenu == null);

        // Setup original menu
        _menu = new SimpleRadialMenu();

        _menu.OnClose += UpdateButton;
        _menu.OnOpen += UpdateButton;

        // Setup alternate menu
        _altMenu = UIManager.CreateWindow<EmotesWindow>();
        LayoutContainer.SetAnchorPreset(_altMenu, LayoutContainer.LayoutPreset.Center);

        _altMenu.OnClose += UpdateButton;
        _altMenu.OnOpen += UpdateButton;
        
        // Fill in the menus
        UpdateEmotes();

        // Bind key
        CommandBinds.Builder
            .Bind(ContentKeyFunctions.OpenEmotesMenu,
                InputCmdHandler.FromDelegate(_ => ToggleEmotesMenu(false)))
            .Register<EmotesUIController>();
    }

    public void UpdateEmotes()
    {
        if (_menu == null || _altMenu == null)
            return;

        var prototypes = _prototypeManager.EnumeratePrototypes<EmotePrototype>();
        var emotesByCategory = GetEmotesByCategory(prototypes);

        var opts = MakeRadialMenuOptions(emotesByCategory);
        _menu.SetButtons(opts);

        // Update alternate menu
        var buttons = MakeEmoteButtons(emotesByCategory);
        _altMenu.SetButtons(buttons);
    }

    public void OnStateExited(GameplayState state)
    {
        if (_menu != null)
        {
            _menu.Close();
            _menu = null;
        }

        if (_altMenu != null)
        {
            _altMenu.Close();
            _altMenu = null;
        }
        
        CommandBinds.Unregister<EmotesUIController>();
    }

    private void ToggleEmotesMenu(bool centered)
    {
        if (_menu == null || _altMenu == null)
            return;

        var isOpen = _menu.IsOpen || _altMenu.IsOpen;

        EmotesButton?.SetClickPressed(!isOpen);

        if (isOpen)
        {
            _menu?.Close();
            _altMenu?.Close();
        }
        else
        {
            // Make sure the emotes are up-to-date
            UpdateEmotes();

            if (_cfg.GetCVar(CCVars.AlternativeEmotesMenu))
            {
                _altMenu.Open();
            }
            else if (centered)
            {
                _menu.OpenCentered();
            }
            else
            {
                _menu.OpenOverMouseScreenPosition();
            }
        }
    }

    public void UnloadButton()
    {
        if (EmotesButton == null)
            return;

        EmotesButton.OnPressed -= ActionButtonPressed;
    }

    public void LoadButton()
    {
        if (EmotesButton == null)
            return;

        EmotesButton.OnPressed += ActionButtonPressed;
    }

    private void ActionButtonPressed(ButtonEventArgs args)
    {
        ToggleEmotesMenu(true);
    }

    private void UpdateButton()
    {
        EmotesButton?.SetClickPressed((_menu?.IsOpen ?? false) || (_altMenu?.IsOpen ?? false));
    }

    private Dictionary<EmoteCategory, List<EmotePrototype>> GetEmotesByCategory(IEnumerable<EmotePrototype> emotePrototypes)
    {
        var whitelistSystem = EntitySystemManager.GetEntitySystem<EntityWhitelistSystem>();
        var player = _playerManager.LocalSession?.AttachedEntity;

        Dictionary<EmoteCategory, List<EmotePrototype>> emotesByCategory = new();
        foreach (var emote in emotePrototypes)
        {
            // only valid emotes that have ways to be triggered by chat and player have access / no restriction on
            if (emote.Category == EmoteCategory.Invalid
                || emote.ChatTriggers.Count == 0
                || !(player.HasValue && whitelistSystem.IsWhitelistPassOrNull(emote.Whitelist, player.Value))
                || whitelistSystem.IsBlacklistPass(emote.Blacklist, player.Value))
                continue;

            if (!emote.Available
                && EntityManager.TryGetComponent<SpeechComponent>(player.Value, out var speech)
                && !speech.AllowedEmotes.Contains(emote.ID))
                continue;

            if (!emotesByCategory.TryGetValue(emote.Category, out var list))
            {
                list = new List<EmotePrototype>();
                emotesByCategory.Add(emote.Category, list);
            }

            list.Add(emote);
        }

        return emotesByCategory;
    }

    private IEnumerable<RadialMenuOption> MakeRadialMenuOptions(Dictionary<EmoteCategory, List<EmotePrototype>> emotesByCategory)
    {
        var models = new RadialMenuOption[emotesByCategory.Count];
        var i = 0;
        foreach (var (key, rawList) in emotesByCategory)
        {
            var list = new List<RadialMenuOption>();
            foreach (var emote in rawList)
            {
                var actionOption = new RadialMenuActionOption<EmotePrototype>(HandleRadialButtonClick, emote) {
                    Sprite = emote.Icon,
                    ToolTip = Loc.GetString(emote.Name)
                };
                list.Add(actionOption);
            }

            var tuple = EmoteGroupingInfo[key];

            models[i] = new RadialMenuNestedLayerOption(list)
            {
                Sprite = tuple.Sprite,
                ToolTip = Loc.GetString(tuple.Tooltip)
            };
            i++;
        }

        return models;
    }

    private IEnumerable<EmoteButton> MakeEmoteButtons(Dictionary<EmoteCategory, List<EmotePrototype>> emotesByCategory)
    {
        var list = new List<EmoteButton>();

        foreach (var (key, rawList) in emotesByCategory)
        {
            foreach (var emote in rawList)
            {
                var button = new EmoteButton(emote);
                button.OnPressed += _ => HandleRadialButtonClick(emote);
                list.Add(button);
            }
        }

        return list;
    }

    private void HandleRadialButtonClick(EmotePrototype prototype)
    {
        _entityManager.RaisePredictiveEvent(new PlayEmoteMessage(prototype.ID));
    }
}
